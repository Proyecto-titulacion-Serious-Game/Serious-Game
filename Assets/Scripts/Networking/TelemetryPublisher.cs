using System.Collections;
using UnityEngine;

/// <summary>
/// Publica la telemetría del sandbox del Reto 4 (que corre en el Explorador) hacia el
/// Técnico vía <see cref="GameSession.RPC_PublicarTelemetria"/>. El Técnico/Host no tiene
/// los motores (ProtoboardSimulator/ArduinoCore) localmente, así que sin esto su panel no
/// mostraría V/I/P/ADC en el setup asimétrico de 2 escenas.
///
/// Se auto-arranca (no requiere ponerlo en ninguna escena) y solo publica cuando hay
/// GameSession en red Y un ProtoboardSimulator presente (i.e., en el Explorador). En la
/// escena del Técnico no hay simulador → queda inactivo sin coste.
/// </summary>
public class TelemetryPublisher : MonoBehaviour
{
    [Tooltip("Frecuencia de publicación en Hz.")]
    public float rateHz = 5f;

    private ProtoboardSimulator _sim;
    private ArduinoCore         _core;

    // Serial online: buffer de Serial.print del ArduinoCore local (solo existe en el Explorador),
    // flusheado por RPC al mismo ritmo que la telemetría. Los errores de runtime viajan al instante.
    private readonly System.Text.StringBuilder _serialBuf = new System.Text.StringBuilder();
    private const int SerialChunk = 380;   // margen bajo el límite de string de un RPC de Fusion

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("[TelemetryPublisher]");
        DontDestroyOnLoad(go);
        go.AddComponent<TelemetryPublisher>();
    }

    void OnEnable()
    {
        ArduinoCore.OnProgramSerial += HandleSerial;
        ArduinoCore.OnProgramError  += HandleError;
    }

    void OnDisable()
    {
        ArduinoCore.OnProgramSerial -= HandleSerial;
        ArduinoCore.OnProgramError  -= HandleError;
    }

    void HandleSerial(string s)
    {
        if (!SesionValida()) return;   // offline: la consola local ya lo muestra, no hay red
        _serialBuf.Append(s);
        // Sketch que imprime a chorro (Serial.println en cada loop sin delay): conservar lo último.
        if (_serialBuf.Length > 4000) _serialBuf.Remove(0, _serialBuf.Length - 2000);
    }

    void HandleError(string s)
    {
        if (!SesionValida()) return;
        GameSession.Instance.RPC_PublicarSerialError(s.Length > SerialChunk ? s.Substring(0, SerialChunk) : s);
    }

    static bool SesionValida()
    {
        var gs = GameSession.Instance;
        return gs != null && gs.Object != null && gs.Object.IsValid;
    }

    IEnumerator Start()
    {
        while (true)
        {
            float interval = rateHz > 0f ? 1f / rateHz : 0.2f;
            yield return new WaitForSeconds(interval);

            // Sin red compartida no hay a quién publicar (offline/escena única usa lectura local).
            if (GameSession.Instance == null) continue;

            FlushSerial();

            // Unity sobrecarga ==, así que un fake-null tras cambio de escena re-dispara la búsqueda.
            // Ojo: un sim DESACTIVADO no es fake-null. La escena tiene 2 ProtoboardSimulator
            // (Reto 2 y Reto 4) y las zonas se prenden/apagan por reto: si nos quedamos con el
            // del reto anterior, publicaríamos telemetría congelada del protoboard equivocado.
            if (_sim == null || !_sim.isActiveAndEnabled)
                _sim = FindAnyObjectByType<ProtoboardSimulator>();
            if (_sim  == null) continue;   // no hay sandbox en esta escena (p.ej. Técnico) → nada que publicar
            if (_core == null || !_core.isActiveAndEnabled)
                _core = FindAnyObjectByType<ArduinoCore>();

            int status = _sim.isShortCircuited ? 1
                       : (_sim.totalCurrentmA <= 0.0001f ? 2 : 0);
            int adc    = _core != null ? _core.GetAnalogReadA0() : 0;

            // Caída real del LED encendido (los nodos del MNA ya incluyen la Vf ~2 V).
            // Didáctico: el Técnico ve que "el LED consume ~2 V y la resistencia el resto".
            float vLed = 0f;
            foreach (var led in _sim.GetComponentsInChildren<LED>(false))
            {
                if (led == null || !led.isOn || led.nodeA == null || led.nodeB == null) continue;
                vLed = Mathf.Abs(led.nodeA.voltage - led.nodeB.voltage);
                break;
            }

            GameSession.Instance.RPC_PublicarTelemetria(
                _sim.sourceVoltage, _sim.totalCurrentmA, _sim.totalPowerW, adc, status, vLed);
        }
    }

    /// <summary>Publica el Serial acumulado en trozos ≤SerialChunk, cortando en salto de línea
    /// cuando se puede para no partir una línea a la mitad entre dos RPCs.</summary>
    void FlushSerial()
    {
        if (_serialBuf.Length == 0 || !SesionValida()) return;

        int n = Mathf.Min(_serialBuf.Length, SerialChunk);
        string chunk = _serialBuf.ToString(0, n);
        if (n < _serialBuf.Length)
        {
            int cut = chunk.LastIndexOf('\n');
            if (cut > 0) { chunk = chunk.Substring(0, cut + 1); n = cut + 1; }
        }
        _serialBuf.Remove(0, n);
        GameSession.Instance.RPC_PublicarSerial(chunk);
    }
}

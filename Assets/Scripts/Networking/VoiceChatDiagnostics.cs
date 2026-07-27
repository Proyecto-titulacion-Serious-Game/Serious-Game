using Photon.Voice.Unity;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Diagnóstico en vivo del chat de voz (ambos roles, clave para el Técnico en PC):
/// cada pocos segundos loguea el estado REAL del micrófono — dispositivo capturado,
/// RecordingEnabled/TransmitEnabled, si está transmitiendo AHORA y el nivel de señal —
/// para poder distinguir de un vistazo en Player.log entre:
///   · mic equivocado (el device logueado no es el que habla el jugador),
///   · señal muerta (peak ≈ 0.000 → mic muteado en Windows o hardware),
///   · transmisión apagada (TransmitEnabled=false),
///   · problema de red/sala de Voice (el Recorder ni siquiera existe/inicializa).
///
/// Tecla F6 (solo PC/Editor): mute/unmute del micrófono propio (TransmitEnabled).
/// Auto-bootstrap: no requiere estar en ninguna escena.
/// </summary>
public class VoiceChatDiagnostics : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<VoiceChatDiagnostics>() != null) return;
        var go = new GameObject("[VoiceChatDiagnostics]");
        DontDestroyOnLoad(go);
        go.AddComponent<VoiceChatDiagnostics>();
    }

    Recorder _recorder;
    float    _nextLog;
    string   _ultimoEstado;

    void Update()
    {
        // F6 = mute/unmute del mic propio (útil si el Técnico quiere silenciarse un momento).
        var kb = Keyboard.current;
        if (kb != null && kb.f6Key.wasPressedThisFrame && _recorder != null)
        {
            _recorder.TransmitEnabled = !_recorder.TransmitEnabled;
            Debug.Log($"[VoiceDiag] F6 → micrófono {(_recorder.TransmitEnabled ? "ACTIVADO" : "MUTEADO")}.");
        }

        if (Time.unscaledTime < _nextLog) return;
        _nextLog = Time.unscaledTime + 5f;

        if (_recorder == null)
        {
            _recorder = FindAnyObjectByType<Recorder>();
            if (_recorder == null)
            {
                Reportar("SIN Recorder en escena — ¿ConnectionManager instanció FusionRunnerVoice? " +
                         "(sin ese prefab no hay chat de voz).");
                return;
            }
        }

        string device = "?";
        try { device = _recorder.MicrophoneDevice.ToString(); } catch { }

        float peak = 0f;
        try { if (_recorder.LevelMeter != null) peak = _recorder.LevelMeter.CurrentPeakAmp; } catch { }

        Reportar($"mic='{device}' grabando={_recorder.RecordingEnabled} " +
                 $"transmitir={_recorder.TransmitEnabled} transmitiendoAHORA={_recorder.IsCurrentlyTransmitting} " +
                 $"nivel(peak)={peak:F3}" +
                 (peak < 0.001f && _recorder.RecordingEnabled
                     ? "  [!] señal ≈ 0: mic muteado en el sistema, device equivocado o sin permiso"
                     : ""));
    }

    /// <summary>Loguea solo cuando el estado CAMBIA (evita spam en Player.log).</summary>
    void Reportar(string estado)
    {
        if (estado == _ultimoEstado) return;
        _ultimoEstado = estado;
        Debug.Log("[VoiceDiag] " + estado);
    }
}

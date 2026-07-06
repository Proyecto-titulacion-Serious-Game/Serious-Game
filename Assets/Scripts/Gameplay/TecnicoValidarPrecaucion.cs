using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// TÉCNICO (host, PC): tecla <b>F8 de PRECAUCIÓN</b>. Si el jugador cree que el circuito está completo
/// pero la victoria no disparó sola, el Técnico presiona F8 para re-verificar.
///
/// NO fuerza la victoria: reusa <see cref="GameSession.SolicitarValidacion"/> →
/// <c>GameManager.EvaluacionManualBotonFisico</c> → <c>EvaluarCircuitSimulator</c>, que SOLO completa
/// si <c>CumpleVictoriaRetos123()</c> es verdad (todos los LED encendidos y correctos). Si no está
/// realmente completo, no pasa nada.
///
/// Solo actúa en el <b>host</b> (el Técnico); en el cliente (Explorador en VR) se ignora. Auto-instancia.
/// </summary>
public class TecnicoValidarPrecaucion : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<TecnicoValidarPrecaucion>() != null) return;
        var go = new GameObject("TecnicoValidarPrecaucion");
        DontDestroyOnLoad(go);
        go.AddComponent<TecnicoValidarPrecaucion>();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || !kb.f8Key.wasPressedThisFrame) return;

        var gs = GameSession.Instance;
        if (gs == null || gs.Object == null || !gs.Object.IsValid) return;   // sin red → nada (usa el debug del editor)
        if (!gs.HasStateAuthority) return;                                    // solo el host = el Técnico

        gs.SolicitarValidacion();   // re-verifica el reto actual; completa SOLO si está realmente correcto
        Debug.Log("[TecnicoValidarPrecaucion] F8 (Técnico): re-verificando el circuito por precaución.");
    }
}

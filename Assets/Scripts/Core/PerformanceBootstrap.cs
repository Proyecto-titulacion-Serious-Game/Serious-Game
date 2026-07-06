using UnityEngine;

/// <summary>
/// Capa el framerate al arrancar (antes de cargar escena), en ambos roles.
///
/// Motivo: ni los QualitySettings ni ningún script fijaban un tope de fps
/// (vSyncCount = 0 en todos los niveles). En el Técnico (PC plano) eso hace que
/// el juego renderice SIN LÍMITE —miles de fps— y sature un core de CPU sin
/// ninguna ganancia visible. En el Explorador (Quest) el runtime XR controla el
/// present, pero fijar el target al refresh del visor evita esperas dobles.
///
/// Se ejecuta solo (RuntimeInitializeOnLoadMethod), no hace falta ponerlo en
/// ninguna escena. Cambio puramente de rendimiento: no toca lógica de juego.
/// </summary>
public static class PerformanceBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void CapFrameRate()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Quest 3 / 3S corren a 90 Hz. El compositor XR manda igual; esto solo
        // alinea el target. vSync se ignora bajo XR, lo dejamos en 0.
        QualitySettings.vSyncCount   = 0;
        Application.targetFrameRate  = 90;
#else
        // PC (Técnico) y editor: sin esto la app corre descapada y quema CPU.
        // vSync = 0 para que mande el cap por targetFrameRate sin atarse al
        // refresh real del monitor (que puede ser 144 Hz y seguir alto el CPU).
        QualitySettings.vSyncCount   = 0;
        Application.targetFrameRate  = 72;
#endif
    }
}

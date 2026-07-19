using UnityEngine;

/// <summary>
/// Activa Fixed Foveated Rendering (FFR) en el Explorador (Quest 3/3S standalone) al arrancar.
///
/// Motivo: el feature "Foveated Rendering" de OpenXR ya está habilitado en la configuración
/// del proyecto (Assets/XR/Settings/OpenXRPackageSettings.asset, Android), pero eso solo
/// registra la extensión — nada fijaba el NIVEL en tiempo de ejecución, así que la GPU seguía
/// sombreando a resolución completa hasta el borde de la lente sin ningún beneficio.
///
/// FFR reduce la resolución de sombreado en la periferia de la vista (donde el ojo casi no
/// nota el detalle) y la mantiene completa en el centro — ahorro real de GPU en Quest con
/// impacto visual mínimo. Se usa modo dinámico (useDynamicFoveatedRendering) para que el
/// sistema solo aplique tanta foveación como haga falta según la carga real de GPU, con
/// "Medium" como techo (conservador; no la más alta).
///
/// Requiere OVRManager en la escena (ya presente en Explorador.unity) — sus wrappers estáticos
/// no hacen nada si el plugin nativo aún no está listo, así que es seguro invocarlos temprano.
/// Cambio puramente de rendimiento: no toca lógica de juego.
/// </summary>
public static class FoveatedRenderingBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnableFoveatedRendering()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (OVRManager.instance == null) return; // Técnico/PCVR o escena sin OVRManager: no aplica

        OVRManager.useDynamicFoveatedRendering = true;
        OVRManager.foveatedRenderingLevel      = OVRManager.FoveatedRenderingLevel.Medium;
#endif
    }
}

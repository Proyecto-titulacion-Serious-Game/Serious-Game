using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Garantiza que SIEMPRE haya exactamente un AudioListener activo en la cámara en uso del
/// Técnico. Sin esto, al CAMINAR no se oía nada (música ni SFX): la cámara de caminar
/// (WalkerCamera) no tiene AudioListener y la del puesto (PC_Camera, que sí lo tiene) está
/// desactivada hasta que el técnico se sienta. Resultado: audio mudo mientras caminás.
///
/// Auto-bootstrap, SOLO Técnico (PC plano). El Explorador (VR) maneja su propio listener en
/// el HMD, así que no se toca.
/// </summary>
public class AudioListenerGuard : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return;                                   // APK (Explorador): no aplica
#else
        if (!IsTecnicoLoaded()) return;           // solo la escena del Técnico
        if (FindAnyObjectByType<AudioListenerGuard>() != null) return;
        var go = new GameObject("[AudioListenerGuard]");
        go.AddComponent<AudioListenerGuard>();
        DontDestroyOnLoad(go);
#endif
    }

    static bool IsTecnicoLoaded()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).name == "Tecnico") return true;
        return false;
    }

    float _cd;

    void Update()
    {
        _cd -= Time.unscaledDeltaTime;
        if (_cd > 0f) return;
        _cd = 0.5f;                               // chequeo barato cada medio segundo
        EnsureSingleListener();
    }

    static void EnsureSingleListener()
    {
        // Cámara activa: preferimos Camera.main; si no, la primera cámara habilitada.
        Camera cam = Camera.main;
        if (cam == null || !cam.isActiveAndEnabled)
        {
            cam = null;
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c.isActiveAndEnabled) { cam = c; break; }
        }
        if (cam == null) return;

        var listener = cam.GetComponent<AudioListener>();
        if (listener == null) listener = cam.gameObject.AddComponent<AudioListener>();
        if (!listener.enabled) listener.enabled = true;

        // Apagar cualquier otro AudioListener para no tener duplicados (Unity solo admite uno).
        foreach (var other in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (other != listener && other.enabled) other.enabled = false;
    }
}

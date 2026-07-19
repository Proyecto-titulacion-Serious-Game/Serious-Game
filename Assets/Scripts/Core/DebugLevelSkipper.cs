using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Salto rápido de retos con <b>F1-F4</b> en Play Mode (ayuda de prueba, dev-only).
///   F1 → Reto 1 · F2 → Reto 2 · F3 → Reto 3 · F4 → completar reto actual (como ganado) y avanzar
///
/// El Quest standalone no siempre tiene teclado a mano, así que F4 (completar reto) tiene también
/// un combo de control VR: mantener presionados A y X (primaryButton) en AMBOS controles a la vez.
/// F1-F3 quedan solo por teclado (uso poco frecuente en VR — saltar a un reto puntual).
///
/// Auto-bootstrap: se crea solo en Editor / Development Build, en CUALQUIER escena (incluida
/// Tecnico.unity, que no tiene GameManager local — ahí el salto viaja por red al Host). Antes el
/// script solo existía en Explorador.unity y su Update abortaba si no había GameManager, así que
/// F1-F4 NO funcionaban desde el PC del Técnico. NO entra en builds de release.
/// </summary>
public class DebugLevelSkipper : MonoBehaviour
{
    static DebugLevelSkipper _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_instance != null) return;
        var go = new GameObject("[DebugLevelSkipper]");
        _instance = go.AddComponent<DebugLevelSkipper>();
        DontDestroyOnLoad(go);
#endif
    }

    private GameManager _gameManager;

    // Combo VR para F4 (completar reto) — A+X (primaryButton) en ambos controles a la vez, sin
    // teclado. Creadas en Awake, no en OnEnable, para no depender de que exista un XR Rig todavía.
    private InputAction _vrPrimaryRight;
    private InputAction _vrPrimaryLeft;
    private bool         _vrComboHeldLastFrame;

    // Si el script también está colocado a mano en una escena, evita duplicar con el bootstrap.
    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;

        _vrPrimaryRight = new InputAction("DebugSkipVRPrimaryRight", InputActionType.Button);
        _vrPrimaryRight.AddBinding("<XRController>{RightHand}/primaryButton");
        _vrPrimaryRight.Enable();

        _vrPrimaryLeft = new InputAction("DebugSkipVRPrimaryLeft", InputActionType.Button);
        _vrPrimaryLeft.AddBinding("<XRController>{LeftHand}/primaryButton");
        _vrPrimaryLeft.Enable();
    }

    void OnDestroy()
    {
        _vrPrimaryRight?.Disable();
        _vrPrimaryLeft?.Disable();
    }

    /// <summary>Localiza el GameManager (si lo hay en esta escena) y le fuerza _debugMode = true.</summary>
    void EnsureGameManager()
    {
        if (_gameManager == null)
            _gameManager = FindAnyObjectByType<GameManager>();

        if (_gameManager != null)
        {
            var field = typeof(GameManager).GetField("_debugMode",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null && !(bool)field.GetValue(_gameManager))
                field.SetValue(_gameManager, true);
        }
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if      (kb.f1Key.wasPressedThisFrame) JumpTo(0);
            else if (kb.f2Key.wasPressedThisFrame) JumpTo(1);
            else if (kb.f3Key.wasPressedThisFrame) JumpTo(2);
            else if (kb.f4Key.wasPressedThisFrame) CompleteCurrentReto();
        }

        // Combo VR (sin teclado): A+X en ambos controles a la vez → equivalente a F4.
        bool comboHeld = _vrPrimaryRight != null && _vrPrimaryLeft != null &&
                          _vrPrimaryRight.IsPressed() && _vrPrimaryLeft.IsPressed();
        if (comboHeld && !_vrComboHeldLastFrame)
        {
            Debug.Log("[DEBUG] Combo VR (A+X ambos controles) → equivalente a F4.");
            CompleteCurrentReto();
        }
        _vrComboHeldLastFrame = comboHeld;
    }

    /// <summary>F4: marca el reto ACTUAL como COMPLETADO (como si se hubiera ganado) — registra la
    /// métrica, dispara ¡FELICIDADES! y transiciona al siguiente reto. Útil para recorrer y probar
    /// los retos (p. ej. el Reto 2). Host-autoritativo: el cliente se lo pide al Host.</summary>
    private void CompleteCurrentReto()
    {
        EnsureGameManager();

        var gs = GameSession.Instance;
        bool enRed = gs != null && gs.Object != null && gs.Object.IsValid;

        // Cliente (Explorador / Técnico sin autoridad): pedir al Host que complete. Él corre
        // CompleteLevel en su GameManager (métrica) y propaga el avance a todos por RPC_CambiarReto.
        if (enRed && !gs.Object.HasStateAuthority)
        {
            Debug.Log("[DEBUG] F4: pidiendo al Host completar el reto actual (como ganado)...");
            gs.RPC_SolicitarCompletarReto();
            return;
        }

        // Host en red, o GameManager local (offline): completar aquí.
        if (_gameManager != null)
        {
            Debug.Log($"[DEBUG] F4: completando el Reto {(int)_gameManager.currentLevel + 1} " +
                      "(como ganado) y avanzando al siguiente...");
            _gameManager.DebugCompleteCurrentLevel();
        }
        else
        {
            Debug.LogWarning("[DEBUG] F4: no hay GameManager ni autoridad de red para completar el reto.");
        }
    }

    private void JumpTo(int index)
    {
        EnsureGameManager();

        var gs = GameSession.Instance;
        bool enRed = gs != null && gs.Object != null && gs.Object.IsValid;

        // Caso 1 — en red y NO somos el Host (Explorador cliente, o Técnico sin GameManager local):
        // pedimos el cambio al Host. Él aplica AvanzarReto y lo propaga a todos por RPC_CambiarReto.
        // (Si lo hiciéramos local, el Host nos revertiría en el siguiente tick.)
        if (enRed && !gs.Object.HasStateAuthority)
        {
            Debug.Log($"[DEBUG] Solicitando al Host saltar al Reto {index + 1}...");
            gs.RPC_SolicitarCambioReto(index);
            return;
        }

        // Caso 2 — somos el Host en red: aplicar y propagar a los clientes.
        if (enRed && gs.Object.HasStateAuthority)
        {
            Debug.Log($"[DEBUG] Host: saltando al Reto {index + 1} (se propaga a los clientes)...");
            gs.AvanzarReto(index);
            return;
        }

        // Caso 3 — offline / sin red: directo al GameManager local.
        if (_gameManager != null)
        {
            Debug.Log($"[DEBUG] Offline: saltando al Reto {index + 1}...");
            _gameManager.GoToLevel(index);
        }
        else
        {
            Debug.LogWarning($"[DEBUG] No hay GameManager ni GameSession para saltar al Reto {index + 1}.");
        }
    }
}

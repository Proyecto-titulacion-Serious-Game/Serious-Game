#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Traspasa el AGARRE de cada punta del multímetro de la esfera diminuta (Probe_Red_Tip /
/// Probe_Black_Tip) al mango cilíndrico que ya la envuelve (Rod_Visual, hoy puramente
/// cosmético — sin Collider/Rigidbody/XRGrabInteractable).
///
/// Por qué (pedido explícito del usuario 2026-07-24, sesión de multímetro):
///   · Un multímetro real se sostiene por el MANGO, no pellizcando la punta metálica.
///   · La esfera-collider de agarre (0.9 no-trigger) es la causa más probable del "duelo" de
///     agarre contra el cuerpo del multímetro y del empujón al jugador, ya diagnosticado en
///     MultimeterProbeGrabRemovalFix.cs (nunca se llegó a aplicar sobre este prefab).
///   · Dejar la punta como TRIGGER-ONLY (ya tenía un SphereCollider trigger r=1.1 para el
///     contacto físico, ver MultimeterProbe.OnTriggerEnter/Exit) la convierte en un sensor de
///     proximidad puro: acercarla a un nodo/slot mide, sin competir por el foco de agarre.
///
/// Pasos (sobre el prefab real vía API, no YAML a mano):
///   1. Localiza Probe_{color}_Tip en cualquier parte del árbol y toma su padre — debe llamarse
///      "Rod_Visual" (si no, se aborta esa punta con una advertencia).
///   2. Rod_Visual gana: CapsuleCollider (no-trigger, calza el mesh Cylinder tal cual — radio 0.5,
///      altura 2, eje Y, igual que el collider por defecto de una cápsula sobre un cilindro),
///      Rigidbody (mismos valores que tenía el de la punta: masa 1, angularDamping 0.05,
///      gravedad+kinemático true — el kinemático real en juego lo decide MultimeterCable.Start()),
///      XRGrabInteractable (VelocityTracking, throwOnDetach=false — igual que el original de la
///      punta) con 'colliders' acotado a SU PROPIO CapsuleCollider (mismo patrón defensivo que
///      Multimeter.Awake()/MultimeterProbe.Awake(): un XRGrabInteractable con la lista vacía
///      auto-recolecta TODOS los colliders del GO, incluido el trigger de la punta hija).
///   3. Probe_{color}_Tip pierde su Rigidbody y su XRGrabInteractable, y el SphereCollider
///      NO-trigger (r=0.9, el que servía para agarrar) se destruye. Solo queda el SphereCollider
///      trigger (r=1.1) — sensor de proximidad puro, sigue siendo hijo de Rod_Visual así que se
///      mueve con él cuando se agarra el mango.
///   4. Cable_{color}.MultimeterCable.probeRigidbody, que apuntaba al Rigidbody de la punta
///      (ahora destruido), pasa a apuntar al Rigidbody nuevo de Rod_Visual — si no se actualiza,
///      el resorte/cable del cable físico queda roto (NullReferenceException en Start()).
///
/// Menú: Tools → TITA → Multímetro → Fix 5 — agarre por Rod_Visual (headless-safe)
/// </summary>
public static class MultimeterRodGrabHandoff
{
    const string PREFAB_PATH = "Assets/Prefabs/Multimeter_VR_Art.prefab";

    [MenuItem("Tools/TITA/Multímetro/Fix 5 — agarre por Rod_Visual (headless-safe)")]
    public static void Apply()
    {
        var go = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
        if (go == null) { Debug.LogError($"[MultimeterRodGrabHandoff] No se pudo cargar {PREFAB_PATH}"); return; }

        int hechos = 0;

        foreach (var colorName in new[] { "Red", "Black" })
        {
            var tip = FindDeep(go.transform, $"Probe_{colorName}_Tip");
            if (tip == null)
            {
                Debug.LogWarning($"[MultimeterRodGrabHandoff] No se encontró Probe_{colorName}_Tip.");
                continue;
            }

            var rod = tip.parent;
            if (rod == null || rod.name != "Rod_Visual")
            {
                Debug.LogWarning($"[MultimeterRodGrabHandoff] El padre de Probe_{colorName}_Tip no es " +
                                  $"'Rod_Visual' (es '{(rod != null ? rod.name : "null")}') — se omite esta punta.");
                continue;
            }

            // ── 1) Capturar valores del Rigidbody/XRGrabInteractable ORIGINALES de la punta ──
            var tipRb    = tip.GetComponent<Rigidbody>();
            var tipGrab  = tip.GetComponent<XRGrabInteractable>();

            float mass = tipRb != null ? tipRb.mass : 1f;
            float angularDamping = tipRb != null ? tipRb.angularDamping : 0.05f;

            // ── 2) Rod_Visual gana Collider + Rigidbody + XRGrabInteractable ─────────────────
            var rodGO = rod.gameObject;

            var capsule = rodGO.GetComponent<CapsuleCollider>();
            if (capsule == null) capsule = rodGO.AddComponent<CapsuleCollider>();
            capsule.direction  = 1;     // eje Y local — coincide con el mesh Cylinder del mango
            capsule.radius     = 0.5f;
            capsule.height     = 2f;
            capsule.center     = Vector3.zero;
            capsule.isTrigger  = false;

            var rodRb = rodGO.GetComponent<Rigidbody>();
            if (rodRb == null) rodRb = rodGO.AddComponent<Rigidbody>();
            rodRb.mass                  = mass;
            rodRb.linearDamping         = 0f;
            rodRb.angularDamping        = angularDamping;
            rodRb.useGravity            = true;
            rodRb.isKinematic           = true;   // MultimeterCable.Start() lo reafirma en runtime
            rodRb.interpolation         = RigidbodyInterpolation.Interpolate;
            rodRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var rodGrab = rodGO.GetComponent<XRGrabInteractable>();
            if (rodGrab == null) rodGrab = rodGO.AddComponent<XRGrabInteractable>();
            rodGrab.movementType   = XRBaseInteractable.MovementType.VelocityTracking;
            rodGrab.throwOnDetach  = false;
            rodGrab.useDynamicAttach = false;
            rodGrab.colliders.Clear();
            rodGrab.colliders.Add(capsule);

            // ── 3) La punta pierde Rigidbody + XRGrabInteractable + collider no-trigger ──────
            if (tipGrab != null) Object.DestroyImmediate(tipGrab, true);
            if (tipRb   != null) Object.DestroyImmediate(tipRb, true);

            int triggersRestantes = 0;
            foreach (var sc in tip.GetComponents<SphereCollider>())
            {
                if (sc.isTrigger) triggersRestantes++;
                else Object.DestroyImmediate(sc, true);
            }
            if (triggersRestantes == 0)
                Debug.LogWarning($"[MultimeterRodGrabHandoff] Probe_{colorName}_Tip se quedó sin ningún " +
                                  "SphereCollider trigger — la detección de proximidad no va a funcionar.");

            // ── 4) Rewiring: Cable_{color}.MultimeterCable.probeRigidbody → Rigidbody de Rod_Visual ──
            var cableT = go.transform.Find($"Cable_{colorName}");
            var cable  = cableT != null ? cableT.GetComponent<MultimeterCable>() : null;
            if (cable != null)
            {
                cable.probeRigidbody = rodRb;
            }
            else
            {
                Debug.LogWarning($"[MultimeterRodGrabHandoff] No se encontró MultimeterCable en Cable_{colorName} " +
                                  "— probeRigidbody no se pudo re-conectar.");
            }

            hechos++;
            Debug.Log($"[MultimeterRodGrabHandoff] '{colorName}': agarre movido a Rod_Visual " +
                      $"(CapsuleCollider+Rigidbody+XRGrabInteractable), punta = sensor de proximidad puro, " +
                      $"MultimeterCable.probeRigidbody re-conectado.");
        }

        PrefabUtility.SaveAsPrefabAsset(go, PREFAB_PATH);
        PrefabUtility.UnloadPrefabContents(go);
        AssetDatabase.Refresh();

        Debug.Log($"[MultimeterRodGrabHandoff] ✓ Listo. Puntas migradas={hechos}/2.");
        if (Application.isBatchMode) EditorApplication.Exit(hechos == 2 ? 0 : 1);
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
#endif

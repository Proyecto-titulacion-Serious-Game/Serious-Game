#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Dos fixes reportados tras probar en VR real el rediseño de <see cref="MultimeterRodGrabHandoff"/>
/// (2026-07-24, sesión de la tarde):
///
/// 1. "No se pueden agarrar los Rod_Visual": el <c>CapsuleCollider</c> que
///    <c>MultimeterRodGrabHandoff</c> le puso a cada Rod_Visual calzaba EXACTO con el mesh Cylinder
///    visible (radio local 0.5, altura local 2) — pero Rod_Visual tiene una escala local minúscula
///    (~0.009 en X/Z, ~0.019 en Y), así que el collider real en mundo terminaba en ~4.5 mm de radio
///    y ~3.8 cm de largo. Un objetivo de 9 mm de diámetro es demasiado pequeño para agarrar con
///    precisión con un controlador de Quest — el mismo problema de fondo que ya se había resuelto
///    una vez para la esfera de la punta en `MultimeterProbeGrabRadiusFix.cs` (sesión anterior),
///    y que se reintrodujo al mover el agarre al mango. Fix: agranda el CapsuleCollider (SOLO el de
///    agarre, no el mesh visible) a un tamaño cómodo en MUNDO, calculado con lossyScale — no es un
///    número mágico en espacio local que dependa de la escala actual del arte.
///
/// 2. "El multímetro rota libre en la mano y es difícil leer la pantalla": pedido explícito del
///    usuario — que al agarrar el CUERPO del multímetro, su rotación quede fija en los 3 ejes (no
///    siga el giro de la muñeca) para que la pantalla se pueda leer sin pelear con la orientación
///    de la mano. El cuerpo usa movementType=Kinematic (ver Multimeter_VR_Art.prefab), así que no
///    hay fuerzas físicas que "congelar" — el único interruptor real es
///    <c>XRGrabInteractable.trackRotation</c>: en false, el objeto NO copia la rotación del
///    interactor, solo su posición (sigue la mano en traslación, pero mantiene la rotación que
///    tenía al agarrarlo). Solo aplica al CUERPO — las puntas/Rod_Visual siguen rotando libre, así
///    se pueden apuntar a los nodos.
///
/// Menú: Tools → TITA → Multímetro → Fix 6 — agrandar agarre de Rod_Visual + fijar rotación del cuerpo
/// </summary>
public static class MultimeterGrabRadiusAndLockRotation
{
    const string PREFAB_PATH = "Assets/Prefabs/Multimeter_VR_Art.prefab";

    // Tamaño de agarre objetivo EN MUNDO — cómodo para un controlador de Quest sin verse
    // absurdamente grueso comparado con el mango real (el mesh visible mide ~0.9 cm de radio con
    // el escalado actual; el collider de agarre queda más grande que el mesh a propósito, es
    // invisible, es una técnica estándar de "grab helper" en VR).
    const float TARGET_WORLD_RADIUS = 0.018f;  // 1.8 cm
    const float TARGET_WORLD_HEIGHT = 0.07f;   // 7 cm (el mesh visible mide ~3.8 cm de largo)

    [MenuItem("Tools/TITA/Multímetro/Fix 6 — agrandar agarre Rod_Visual + fijar rotación cuerpo")]
    public static void Apply()
    {
        var go = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
        if (go == null) { Debug.LogError($"[MultimeterGrabRadiusAndLockRotation] No se pudo cargar {PREFAB_PATH}"); return; }

        int rodsAgrandados = 0;

        // ── 1) Agrandar el CapsuleCollider de agarre de cada Rod_Visual ──────────────────
        foreach (var colorName in new[] { "Red", "Black" })
        {
            var tip = FindDeep(go.transform, $"Probe_{colorName}_Tip");
            var rod = tip != null ? tip.parent : null;
            if (rod == null || rod.name != "Rod_Visual")
            {
                Debug.LogWarning($"[MultimeterGrabRadiusAndLockRotation] Probe_{colorName}_Tip / Rod_Visual " +
                                  "no tienen la jerarquía esperada (¿corriste MultimeterRodGrabHandoff antes?) — se omite.");
                continue;
            }

            var capsule = rod.GetComponent<CapsuleCollider>();
            if (capsule == null)
            {
                Debug.LogWarning($"[MultimeterGrabRadiusAndLockRotation] Rod_Visual ({colorName}) no tiene " +
                                  "CapsuleCollider — se omite.");
                continue;
            }

            Vector3 scale = rod.lossyScale;
            float scaleXZ = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
            float scaleY  = Mathf.Max(0.0001f, Mathf.Abs(scale.y));  // eje del capsule (direction=1 → Y local)

            float radioAnteriorMundo = capsule.radius * scaleXZ;
            float alturaAnteriorMundo = capsule.height * scaleY;

            capsule.radius = TARGET_WORLD_RADIUS / scaleXZ;
            capsule.height = TARGET_WORLD_HEIGHT / scaleY;

            rodsAgrandados++;
            Debug.Log($"[MultimeterGrabRadiusAndLockRotation] Rod_Visual ({colorName}): collider de agarre " +
                      $"{radioAnteriorMundo * 1000f:F1}mm→{TARGET_WORLD_RADIUS * 1000f:F1}mm radio, " +
                      $"{alturaAnteriorMundo * 100f:F1}cm→{TARGET_WORLD_HEIGHT * 100f:F1}cm largo (mundo).");
        }

        // ── 2) Fijar rotación del CUERPO al agarrarlo (trackRotation = false) ────────────
        var bodyGrab = go.GetComponent<XRGrabInteractable>();
        bool rotacionFijada = bodyGrab != null;
        if (bodyGrab != null)
        {
            bodyGrab.trackRotation = false;
            Debug.Log("[MultimeterGrabRadiusAndLockRotation] Cuerpo del multímetro: trackRotation → false " +
                      "(mantiene la rotación que tenía al agarrarlo, solo traslada con la mano).");
        }
        else
        {
            Debug.LogWarning("[MultimeterGrabRadiusAndLockRotation] No se encontró XRGrabInteractable en la " +
                              "raíz del prefab — no se pudo fijar la rotación del cuerpo.");
        }

        PrefabUtility.SaveAsPrefabAsset(go, PREFAB_PATH);
        PrefabUtility.UnloadPrefabContents(go);
        // 'go'/'bodyGrab' quedan destruidos (Unity fake-null) a partir de acá — usar solo las
        // variables bool ya capturadas ANTES del Unload, nunca los Object refs después.
        AssetDatabase.Refresh();

        Debug.Log($"[MultimeterGrabRadiusAndLockRotation] ✓ Listo. Rod_Visual agrandados={rodsAgrandados}/2, " +
                  $"rotación del cuerpo fijada={rotacionFijada}.");
        if (Application.isBatchMode) EditorApplication.Exit(rodsAgrandados == 2 && rotacionFijada ? 0 : 1);
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
#endif

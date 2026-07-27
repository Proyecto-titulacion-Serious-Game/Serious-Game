#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tercera pasada sobre Multimeter_VR_Art.prefab: el collider de AGARRE (no-trigger) de cada
/// punta tiene radius=0.9 en espacio LOCAL, pero la punta tiene localScale ≈ (0.003, 0.008, 0.003)
/// — el radio EFECTIVO en el mundo real es apenas ~0.9 × 0.008 ≈ 0.007 m (7 mm de radio, 14 mm de
/// diámetro), demasiado ajustado para agarrar con precisión de mano en VR. Sube el radio local a
/// 1.6 (~13 mm de radio / 26 mm de diámetro efectivo) SOLO en el collider de agarre — el de
/// detección/contacto (trigger, radius=1.1) queda igual, no participa del agarre.
///
/// Menú: Tools → TITA → Multímetro → Fix 3 — agrandar collider de agarre (headless-safe)
/// </summary>
public static class MultimeterProbeGrabRadiusFix
{
    const string PREFAB_PATH = "Assets/Prefabs/Multimeter_VR_Art.prefab";
    const float NEW_GRAB_RADIUS = 1.6f;   // antes 0.9

    [MenuItem("Tools/TITA/Multímetro/Fix 3 — agrandar collider de agarre (headless-safe)")]
    public static void Apply()
    {
        var go = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
        if (go == null) { Debug.LogError($"[MultimeterProbeGrabRadiusFix] No se pudo cargar {PREFAB_PATH}"); return; }

        int fixedCount = 0;
        foreach (var probe in go.GetComponentsInChildren<MultimeterProbe>(true))
        {
            foreach (var col in probe.GetComponents<SphereCollider>())
            {
                if (col.isTrigger) continue;   // el de detección/contacto no se toca
                float before = col.radius;
                col.radius = NEW_GRAB_RADIUS;
                fixedCount++;
                Debug.Log($"[MultimeterProbeGrabRadiusFix] '{probe.name}': collider de agarre radius {before} → {NEW_GRAB_RADIUS} " +
                          $"(mundo ≈ {NEW_GRAB_RADIUS * probe.transform.lossyScale.y:F4} m con escala actual).");
            }
        }

        PrefabUtility.SaveAsPrefabAsset(go, PREFAB_PATH);
        PrefabUtility.UnloadPrefabContents(go);
        AssetDatabase.Refresh();

        Debug.Log($"[MultimeterProbeGrabRadiusFix] ✓ Listo. Colliders de agarre agrandados={fixedCount}");
        if (Application.isBatchMode) EditorApplication.Exit(fixedCount == 2 ? 0 : 1);
    }
}
#endif

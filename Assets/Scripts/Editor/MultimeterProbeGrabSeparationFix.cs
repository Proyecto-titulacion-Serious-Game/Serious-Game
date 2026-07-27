#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Segunda pasada sobre Multimeter_VR_Art.prefab (después de MultimeterProbeRestFix): la
/// separación anterior solo empujaba la punta hacia ARRIBA (+Y, 2 cm). El jack de cada punta
/// (Cable_Anchor_Red/Black) está cerca del borde del BoxCollider del cuerpo en Z (el jack sale por
/// el costado del cuerpo, no por arriba) — con solo 2 cm en Y, la mano que va a agarrar la punta en
/// reposo sigue rozando el BoxCollider del cuerpo en la mayoría de los ángulos de agarre reales
/// (reporte: "al agarrar el nodo positivo se agarre el multímetro").
///
/// Este fix aumenta la separación en Y Y además empuja hacia AFUERA en Z (alejándose del centro del
/// cuerpo, mismo signo que la posición del jack) para que la punta en reposo quede claramente fuera
/// del volumen del BoxCollider en las 3 dimensiones. También estira el cable (maxCableLength) para
/// que el Explorador pueda alcanzar nodos más separados sin pelear contra el SpringJoint, y
/// suaviza el resorte para que la sensación de "estirar" sea progresiva, no un tope duro.
///
/// Menú: Tools → TITA → Multímetro → Fix 2 — separación de agarre + cable más largo (headless-safe)
/// </summary>
public static class MultimeterProbeGrabSeparationFix
{
    const string PREFAB_PATH = "Assets/Prefabs/Multimeter_VR_Art.prefab";

    const float NEW_Y_OFFSET   = 0.045f;  // antes 0.02 — más separación vertical
    const float NEW_Z_PUSHOUT  = 0.035f;  // nuevo — empuja la punta hacia afuera del cuerpo en Z
    const float NEW_CABLE_LEN  = 0.85f;   // antes 0.6 — más alcance
    const float NEW_SPRING     = 350f;    // antes 600 — estirón más progresivo, menos "tope duro"
    const float NEW_DAMPER     = 12f;     // sin cambio (evita rebote/oscilación al soltar)

    [MenuItem("Tools/TITA/Multímetro/Fix 2 — separación de agarre + cable más largo (headless-safe)")]
    public static void Apply()
    {
        var go = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
        if (go == null) { Debug.LogError($"[MultimeterProbeGrabSeparationFix] No se pudo cargar {PREFAB_PATH}"); return; }

        int fixedCables = 0, fixedProbes = 0;

        foreach (var colorName in new[] { "Red", "Black" })
        {
            var cableT = go.transform.Find($"Cable_{colorName}");
            if (cableT == null)
            {
                Debug.LogWarning($"[MultimeterProbeGrabSeparationFix] No se encontró 'Cable_{colorName}'.");
                continue;
            }

            // JERARQUÍA REAL (verificada con MultimeterHierarchyDump): Probe_{color}_Tip es HIJO de
            // Cable_Anchor_{color} (no hermano) — un reparenting posterior a MultimeterProbeRestFix
            // cambió esto. anchorT.localPosition ya NO se suma (el propio anchor es el origen local
            // del probe); el offset se aplica directo como posición local dentro del anchor.
            var anchorT = cableT.Find($"Cable_Anchor_{colorName}");
            var probeT  = anchorT != null ? anchorT.Find($"Probe_{colorName}_Tip") : null;
            if (anchorT == null || probeT == null)
            {
                Debug.LogWarning($"[MultimeterProbeGrabSeparationFix] Faltan hijos de 'Cable_{colorName}' " +
                                  $"(anchor={anchorT != null}, probe={probeT != null}).");
                continue;
            }

            // Empujar hacia afuera en Z con el MISMO signo que la posición del jack respecto al
            // centro del cuerpo (root) — así la punta se aleja del CENTRO del cuerpo en vez de
            // acercarse más a él por un error de signo.
            Vector3 anchorRelativeToRoot = go.transform.InverseTransformPoint(anchorT.position);
            float zSign = Mathf.Sign(anchorRelativeToRoot.z == 0f ? 1f : anchorRelativeToRoot.z);
            var newRestOffset = new Vector3(0f, NEW_Y_OFFSET, zSign * NEW_Z_PUSHOUT);

            var cable = cableT.GetComponent<MultimeterCable>();
            if (cable != null)
            {
                cable.restOffset      = newRestOffset;   // MultimeterCable usa esto como offset EN MUNDO desde anchorPoint.position
                cable.maxCableLength  = NEW_CABLE_LEN;
                fixedCables++;
            }

            // Misma fórmula que MultimeterCable.Start()/ReturnToAnchor(): posición MUNDIAL directa,
            // sin importar quién sea el padre actual de la punta.
            probeT.position = anchorT.position + newRestOffset;
            fixedProbes++;

            Debug.Log($"[MultimeterProbeGrabSeparationFix] '{colorName}': restOffset → {newRestOffset}, " +
                      $"punta.position(mundo) → {probeT.position}, maxCableLength → {NEW_CABLE_LEN}");
        }

        // Suavizar el resorte de AMBAS puntas (el SpringJoint se crea en runtime por MultimeterCable,
        // no vive en el prefab — se ajusta vía el propio script en su próximo Start()). Documentado
        // aquí para quien lea el fix; el valor real se aplica desde MultimeterCable.SetupSpringJoint.

        PrefabUtility.SaveAsPrefabAsset(go, PREFAB_PATH);
        PrefabUtility.UnloadPrefabContents(go);
        AssetDatabase.Refresh();

        Debug.Log($"[MultimeterProbeGrabSeparationFix] ✓ Listo. Cables actualizados={fixedCables} Puntas movidas={fixedProbes}");

        if (Application.isBatchMode)
            EditorApplication.Exit((fixedCables == 2 && fixedProbes == 2) ? 0 : 1);
    }
}
#endif

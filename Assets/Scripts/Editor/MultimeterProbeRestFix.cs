#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Parche puntual sobre el prefab EXISTENTE Multimeter_VR_Art.prefab: separa la posición de
/// reposo/agarre de cada punta (Probe_Red_Tip / Probe_Black_Tip) del volumen del BoxCollider del
/// cuerpo del multímetro, y agranda un poco su collider de agarre.
///
/// Causa raíz (medida en el prefab): las puntas descansan en jackPos = (±0.02085, 0.00720,
/// -0.02539), que cae DENTRO del BoxCollider del cuerpo (centro (0,-0.0074,-0.0008), tamaño
/// (0.0786, 0.0315, 0.1649)) — la mano, al ir por la punta, siempre roza también el cuerpo.
///
/// No usa MultimeterArtSetupTool.Create() porque ese método muestra un diálogo interactivo
/// "¿Sobreescribir?" que bloquea en batchmode. Este parche edita el asset directo vía
/// LoadPrefabContents/SaveAsPrefabAsset, sin diálogos — seguro para -executeMethod headless.
/// </summary>
public static class MultimeterProbeRestFix
{
    const string PREFAB_PATH = "Assets/Prefabs/Multimeter_VR_Art.prefab";
    // Hacia ARRIBA (+Y), no abajo: un offset hacia abajo puede meter la punta dentro de la mesa
    // donde se apoya el multímetro — al mover el cuerpo, la física intenta sacarla de la geometría
    // con un impulso fuerte que empuja al jugador (bug real reportado tras la primera versión).
    static readonly Vector3 REST_OFFSET = new Vector3(0f, 0.02f, 0f);
    const float NEW_GRAB_RADIUS = 0.9f;

    [MenuItem("Tools/TITA/Multímetro/Fix — separar puntas del cuerpo (headless-safe)")]
    public static void Apply()
    {
        var go = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
        if (go == null)
        {
            Debug.LogError($"[MultimeterProbeRestFix] No se pudo cargar {PREFAB_PATH}");
            return;
        }

        int fixedCables = 0, fixedProbes = 0;

        foreach (var colorName in new[] { "Red", "Black" })
        {
            var cableT = go.transform.Find($"Cable_{colorName}");
            if (cableT == null)
            {
                Debug.LogWarning($"[MultimeterProbeRestFix] No se encontró 'Cable_{colorName}'.");
                continue;
            }

            var cable = cableT.GetComponent<MultimeterCable>();
            if (cable != null)
            {
                cable.restOffset = REST_OFFSET;
                fixedCables++;
            }

            var anchorT = cableT.Find($"Cable_Anchor_{colorName}");
            var probeT  = cableT.Find($"Probe_{colorName}_Tip");
            if (anchorT == null || probeT == null)
            {
                Debug.LogWarning($"[MultimeterProbeRestFix] Faltan hijos de 'Cable_{colorName}' " +
                                  $"(anchor={anchorT != null}, probe={probeT != null}).");
                continue;
            }

            // Mismo padre (cableT) → aritmética directa en local space es válida.
            probeT.localPosition = anchorT.localPosition + REST_OFFSET;

            var cols = probeT.GetComponents<SphereCollider>();
            foreach (var c in cols)
            {
                if (!c.isTrigger) c.radius = NEW_GRAB_RADIUS;   // el de agarre, no el trigger de contacto
            }

            fixedProbes++;
            Debug.Log($"[MultimeterProbeRestFix] '{colorName}': punta movida a {probeT.localPosition} " +
                      $"(anchor sigue en {anchorT.localPosition}), radio de agarre → {NEW_GRAB_RADIUS}");
        }

        PrefabUtility.SaveAsPrefabAsset(go, PREFAB_PATH);
        PrefabUtility.UnloadPrefabContents(go);
        AssetDatabase.Refresh();

        Debug.Log($"[MultimeterProbeRestFix] ✓ Listo. Cables parchados={fixedCables} Puntas parchadas={fixedProbes}");

        if (Application.isBatchMode)
            EditorApplication.Exit((fixedCables == 2 && fixedProbes == 2) ? 0 : 1);
    }
}
#endif

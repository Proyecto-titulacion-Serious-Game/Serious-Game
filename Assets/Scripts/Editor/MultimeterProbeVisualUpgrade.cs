#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Reemplaza el modelo visual de cada punta del multímetro (hoy una esfera lisa) por una forma
/// reconocible de "punta de prueba" real: mango cilíndrico del color del cable + vástago metálico
/// delgado en la punta (la parte que realmente toca el nodo). La esfera original se mantiene oculta
/// (sigue siendo el collider de agarre/contacto — no se toca ninguna lógica de interacción).
///
/// El vástago apunta en +Z local, mismo eje que usa MultimeterProbe.FindNearestAhead()
/// (SphereCast en transform.forward) — así lo que se VE apuntando es lo que realmente detecta.
/// </summary>
public static class MultimeterProbeVisualUpgrade
{
    const string PREFAB_PATH = "Assets/Prefabs/Multimeter_VR_Art.prefab";

    // Dimensiones en WORLD SPACE (metros) — se convierten a local dividiendo por la escala del padre.
    static readonly Vector3 ROD_SIZE = new Vector3(0.009f, 0.009f, 0.038f); // mango: 9mm x 9mm x 38mm
    static readonly Vector3 TIP_SIZE = new Vector3(0.003f, 0.003f, 0.016f); // vástago: 3mm x 3mm x 16mm
    const float GAP = 0.001f; // separación entre mango y vástago para que no se fundan visualmente

    [MenuItem("Tools/TITA/Multímetro/Fix — modelo de puntas de prueba (headless-safe)")]
    public static void Apply()
    {
        var go = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
        if (go == null) { Debug.LogError($"[MultimeterProbeVisualUpgrade] No se pudo cargar {PREFAB_PATH}"); return; }

        int hechas = 0;

        foreach (var (colorName, color) in new[] { ("Red", new Color(0.85f, 0.1f, 0.1f)), ("Black", new Color(0.12f, 0.12f, 0.12f)) })
        {
            var cableT = go.transform.Find($"Cable_{colorName}");
            var probeT = cableT != null ? cableT.Find($"Probe_{colorName}_Tip") : null;
            if (probeT == null)
            {
                Debug.LogWarning($"[MultimeterProbeVisualUpgrade] No se encontró Probe_{colorName}_Tip.");
                continue;
            }

            // 1) Ocultar el mesh de la esfera original (sigue viva para sus 2 SphereCollider + Rigidbody
            //    + XRGrabInteractable + MultimeterProbe — solo se apaga su render).
            var sphereRenderer = probeT.GetComponent<MeshRenderer>();
            if (sphereRenderer != null) sphereRenderer.enabled = false;

            // 2) Limpiar una corrida anterior de este mismo fix (idempotente).
            var oldRod = probeT.Find("Rod_Visual");
            var oldTip = probeT.Find("Tip_Visual");
            if (oldRod != null) Object.DestroyImmediate(oldRod.gameObject);
            if (oldTip != null) Object.DestroyImmediate(oldTip.gameObject);

            Vector3 parentScale = probeT.lossyScale;

            // 3) Mango — cilindro del color del cable, mitad "de atrás" de la punta.
            var rod = CreateVisualChild(probeT, "Rod_Visual", $"{colorName}_Rod", PrimitiveType.Cylinder, color, parentScale,
                ROD_SIZE, localZ: -(TIP_SIZE.z * 0.5f + GAP));

            // 4) Vástago metálico — cilindro delgado plateado, "de adelante" (la parte que toca el nodo).
            var tipColor = new Color(0.75f, 0.76f, 0.78f); // plateado/metálico
            var tip = CreateVisualChild(probeT, "Tip_Visual", $"{colorName}_Tip", PrimitiveType.Cylinder, tipColor, parentScale,
                TIP_SIZE, localZ: (ROD_SIZE.z * 0.5f + GAP));

            hechas++;
            Debug.Log($"[MultimeterProbeVisualUpgrade] '{colorName}': mango+vástago creados, esfera original oculta (sigue como collider).");
        }

        PrefabUtility.SaveAsPrefabAsset(go, PREFAB_PATH);
        PrefabUtility.UnloadPrefabContents(go);
        AssetDatabase.Refresh();

        Debug.Log($"[MultimeterProbeVisualUpgrade] ✓ Listo. Puntas actualizadas={hechas}");
        if (Application.isBatchMode) EditorApplication.Exit(hechas == 2 ? 0 : 1);
    }

    /// <summary>Crea un hijo cilíndrico SIN collider (la física la sigue manejando la esfera padre),
    /// orientado a lo largo de Z local, con el tamaño mundial pedido convertido a escala local.
    /// <paramref name="matKey"/> es la clave de archivo del material — debe ser ÚNICA por color/pieza
    /// (name solo no alcanza: Rod_Visual se repite en Red y Black, y pisaba el mismo .mat).</summary>
    static Transform CreateVisualChild(Transform parent, string name, string matKey, PrimitiveType prim, Color color,
        Vector3 parentLossyScale, Vector3 worldSize, float localZ)
    {
        var child = GameObject.CreatePrimitive(prim);
        child.name = name;
        child.transform.SetParent(parent, false);

        // Cilindro de Unity: eje largo = Y local, altura=2, radio=0.5 a escala 1.
        // Lo rotamos 90° en X para que su eje largo pase a ser Z local (coincide con transform.forward,
        // el mismo eje que usa MultimeterProbe.FindNearestAhead para el SphereCast de apuntado).
        child.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        child.transform.localPosition = new Vector3(0f, 0f, localZ / Mathf.Max(parentLossyScale.z, 0.0001f));

        Vector3 local = new Vector3(
            worldSize.x / Mathf.Max(parentLossyScale.x, 0.0001f),   // diámetro X (radio*2 a escala 1 = 1 unidad)
            worldSize.z / 2f / Mathf.Max(parentLossyScale.y, 0.0001f), // altura Y del cilindro (2 unidades a escala 1) -> mitad
            worldSize.y / Mathf.Max(parentLossyScale.z, 0.0001f));  // diámetro Z
        child.transform.localScale = local;

        var col = child.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);   // la física ya la maneja la esfera padre

        child.GetComponent<Renderer>().sharedMaterial = MakePersistentMat(color, matKey);

        return child.transform;
    }

    /// <summary>
    /// Crea el material COMO ASSET (no en memoria) — un Material in-memory asignado a un renderer
    /// se pierde al serializar con SaveAsPrefabAsset (mismo problema que ya documentaba
    /// MultimeterArtSetupTool.SaveColorMat), dejando el renderer sin material → invisible en build.
    /// </summary>
    static Material MakePersistentMat(Color color, string matKey)
    {
        const string folder = "Assets/Materials/Multimeter";
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Materials", "Multimeter");

        string path = $"{folder}/{matKey.ToLowerInvariant()}_mesh.mat";
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader);
        mat.SetColor("_BaseColor", color);
        mat.color = color;

        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mat, path);
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }
}
#endif

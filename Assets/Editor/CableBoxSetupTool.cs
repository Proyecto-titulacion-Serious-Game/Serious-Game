#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// Genera el prop industrial "Dispensador de Cables Jumper" y el prefab Cable_Jumper.
///
/// Crea:
///   Assets/Prefabs/Cable_Jumper.prefab   — cable XRGrabInteractable con puntas de color
///   CableBox_VR (en escena activa)       — dispensador con máquina de estados visual
///   ExplorerWorkstation.prefab           — parcheado con CableBox si falta
///
/// Menú: Tools → TITA → Reto 4 → Generar CableBox + Cable Prefab
public static class CableBoxSetupTool
{
    const string CABLE_PATH = "Assets/Prefabs/Cable_Jumper.prefab";
    const string WS_PATH    = "Assets/Prefabs/ExplorerWorkstation.prefab";

    // Dimensiones del cuerpo principal
    const float W = 0.12f, H = 0.10f, D = 0.08f;
    const float TRIM = 0.004f;

    // ─────────────────────────────────────────
    [MenuItem("Tools/TITA/Reto 4/Generar CableBox + Cable Prefab")]
    public static void Run()
    {
        var cablePrefab = BuildCablePrefab();

        int sceneResult = PlaceCableBoxInScene(cablePrefab);
        bool wsResult   = PatchWorkstationPrefab(cablePrefab);
        SaveStandalonePrefab(cablePrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("CableBox Setup",
            "Cable_Jumper.prefab  ✅\n" +
            (sceneResult == 2  ? "CableBox_VR reconstruido (prop industrial)  ✅\n" :
             sceneResult == 1  ? "CableBox_VR creado en escena  ✅\n" :
             sceneResult == 0  ? "CableBox_VR ya existía  ♻\n" :
                                 "CableBox_VR sin escena abierta  ⚠\n") +
            (wsResult ? "ExplorerWorkstation.prefab actualizado  ✅" :
                        "ExplorerWorkstation.prefab: CableBox ya presente  ♻"),
            "OK");
    }

    // ═════════════════════════════════════════
    //  CABLE JUMPER PREFAB
    // ═════════════════════════════════════════
    public static void SaveStandalonePrefab(GameObject cablePrefab = null)
    {
        if (cablePrefab == null)
            cablePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CABLE_PATH)
                       ?? BuildCablePrefab();

        const string PREFAB_PATH = "Assets/Prefabs/CableBox_VR.prefab";
        var go = BuildBoxGO(cablePrefab);
        PrefabUtility.SaveAsPrefabAsset(go, PREFAB_PATH);
        Object.DestroyImmediate(go);
        Debug.Log($"[CableBoxSetupTool] {PREFAB_PATH} guardado.");
    }

    public static GameObject BuildCablePrefab()
    {
        var root = new GameObject("Cable_Jumper");

        // Rigidbody
        var rb             = root.AddComponent<Rigidbody>();
        rb.mass            = 0.02f;
        rb.linearDamping   = 2f;
        rb.angularDamping  = 4f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Collider de agarre (más ancho = más fácil agarrar en VR)
        var col       = root.AddComponent<CapsuleCollider>();
        col.direction = 2;          // eje Z
        col.radius    = 0.007f;
        col.height    = 0.12f;
        col.center    = new Vector3(0f, 0f, 0.04f);

        // XRGrabInteractable
        var grab              = root.AddComponent<XRGrabInteractable>();
        grab.movementType     = XRBaseInteractable.MovementType.VelocityTracking;
        grab.throwOnDetach    = true;
        grab.retainTransformParent = false;

        // Cuerpo del cable (cilindro gris oscuro, más grueso que antes)
        var body = Prim(PrimitiveType.Cylinder, "Cable_Body", root.transform,
            new Vector3(0.008f, 0.055f, 0.008f),
            new Vector3(0f, 0f, 0.04f),
            Quaternion.Euler(90f, 0f, 0f));
        body.GetComponent<Renderer>().sharedMaterial =
            LitMat(new Color(0.12f, 0.12f, 0.12f), 0f, 0.25f);

        // Punta A — color cálido por defecto (se sobreescribe con MPB al spawn)
        var probeA = Probe("Probe_A", root.transform, Vector3.zero,
            new Color(0.75f, 0.62f, 0.37f), metallic: 0.9f, smooth: 0.72f);

        // Punta B — color frío por defecto
        var probeB = Probe("Probe_B", root.transform, new Vector3(0f, 0f, 0.08f),
            new Color(0.52f, 0.63f, 0.75f), metallic: 0.9f, smooth: 0.72f);

        // LineRenderer para visualizar el cable
        var lr                   = root.AddComponent<LineRenderer>();
        lr.positionCount         = 8;
        lr.startWidth            = lr.endWidth = 0.006f;
        lr.useWorldSpace         = true;
        lr.shadowCastingMode     = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.sharedMaterial        = LineMat(new Color(0.12f, 0.12f, 0.12f));

        var vcr           = root.AddComponent<VRCableRenderer>();
        vcr.origin        = probeA.transform;
        vcr.target        = probeB.transform;
        vcr.segments      = 20;
        vcr.arcUpward     = true;
        vcr.arcPerMeter   = 0.4f;
        vcr.minArc        = 0.02f;
        vcr.maxArc        = 0.12f;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, CABLE_PATH);
        Object.DestroyImmediate(root);
        Debug.Log($"[CableBoxSetupTool] {CABLE_PATH} guardado.");
        return prefab;
    }

    static GameObject Probe(string name, Transform parent, Vector3 pos,
        Color color, float metallic, float smooth)
    {
        var go = Prim(PrimitiveType.Sphere, name, parent,
            Vector3.one * 0.018f, pos, Quaternion.identity);
        go.GetComponent<Renderer>().sharedMaterial = LitMat(color, metallic, smooth);
        return go;
    }

    // ═════════════════════════════════════════
    //  CABLEBOX EN ESCENA ACTIVA
    // ═════════════════════════════════════════
    static int PlaceCableBoxInScene(GameObject cablePrefab)
    {
        var existing = Object.FindAnyObjectByType<CableBoxSpawner>();
        if (existing != null)
        {
            // Si es el cubo plano viejo (sin hijos), reemplazar por el prop completo
            if (existing.transform.childCount == 0)
            {
                var parent   = existing.transform.parent;
                var worldPos = existing.transform.position;
                Undo.DestroyObjectImmediate(existing.gameObject);

                var newBox = BuildBoxGO(cablePrefab);
                if (parent != null) newBox.transform.SetParent(parent, false);
                newBox.transform.position   = worldPos;
                newBox.transform.localScale = Vector3.one;
                Undo.RegisterCreatedObjectUndo(newBox, "Rebuild CableBox_VR");
                EditorSceneManager.MarkSceneDirty(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                return 2; // rebuilt
            }
            PatchSpawner(existing, cablePrefab);
            return 0;
        }

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!scene.IsValid()) return -1;

        var mesa = FindGO("Mesa_Explorador", "ExplorerTable", "Workbench_VR");
        var box  = BuildBoxGO(cablePrefab);
        box.transform.localPosition = new Vector3(-0.15f, 0.05f, 0f);
        if (mesa != null) box.transform.SetParent(mesa.transform, false);

        Undo.RegisterCreatedObjectUndo(box, "Crear CableBox_VR");
        EditorSceneManager.MarkSceneDirty(scene);
        return 1;
    }

    // ═════════════════════════════════════════
    //  CONSTRUIR EL PROP INDUSTRIAL
    // ═════════════════════════════════════════
    public static GameObject BuildBoxGO(GameObject cablePrefab)
    {
        var root    = new GameObject("CableBox_VR");
        var boxCol  = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(W, H, D);
        boxCol.isTrigger = true;
        root.AddComponent<XRSimpleInteractable>();

        var spawner             = root.AddComponent<CableBoxSpawner>();
        spawner.cablePrefab     = cablePrefab;
        spawner.spawnOffset     = new Vector3(0f, H * 0.5f + 0.03f, 0f);
        spawner.maxActiveCables = 20;

        // ── Cuerpo principal (policarbonato semitransparente) ──
        var body = Prim(PrimitiveType.Cube, "Body_Main", root.transform,
            new Vector3(W, H, D), Vector3.zero, Quaternion.identity);
        body.GetComponent<Renderer>().sharedMaterial = TransparentMat(Hex("2A2D35"), 0.30f);

        // ── Panel superior inclinado ──────────────────────────
        var top = Prim(PrimitiveType.Cube, "Body_Top", root.transform,
            new Vector3(W, 0.003f, D),
            new Vector3(0f, H * 0.5f + 0.001f, 0f),
            Quaternion.Euler(-10f, 0f, 0f));
        top.GetComponent<Renderer>().sharedMaterial = LitMat(Hex("1C1E22"), 0.1f, 0.45f);

        // ── Tiras de trim naranja (bordes frontales) ──────────
        Color orange = Hex("E8820C");
        Trim("Trim_Top_F",   root.transform, orange, new Vector3(W, TRIM, TRIM),
             new Vector3(0f,  H*0.5f,  -D*0.5f + TRIM*0.5f));
        Trim("Trim_Bot_F",   root.transform, orange, new Vector3(W, TRIM, TRIM),
             new Vector3(0f, -H*0.5f,  -D*0.5f + TRIM*0.5f));
        Trim("Trim_Left_F",  root.transform, orange, new Vector3(TRIM, H, TRIM),
             new Vector3(-W*0.5f + TRIM*0.5f, 0f, -D*0.5f + TRIM*0.5f));
        Trim("Trim_Right_F", root.transform, orange, new Vector3(TRIM, H, TRIM),
             new Vector3( W*0.5f - TRIM*0.5f, 0f, -D*0.5f + TRIM*0.5f));

        // ── Ranura de dispensación (dos jambas + compuerta) ───
        // Jamba izquierda
        Solid("Slot_JambaL", root.transform, Hex("1A1C20"),
            new Vector3(W * 0.38f, 0.012f, 0.006f),
            new Vector3(-W * 0.29f, H*0.5f - 0.018f, -D*0.5f - 0.002f));
        // Jamba derecha
        Solid("Slot_JambaR", root.transform, Hex("1A1C20"),
            new Vector3(W * 0.38f, 0.012f, 0.006f),
            new Vector3( W * 0.29f, H*0.5f - 0.018f, -D*0.5f - 0.002f));

        // Compuerta (arranca arriba = abierta, baja al llegar al límite)
        var gate = Solid("Gate_Plate", root.transform, Hex("8E8E93"),
            new Vector3(0.028f, 0.011f, 0.004f),
            new Vector3(0f, H*0.5f - 0.008f, -D*0.5f - 0.002f));
        gate.GetComponent<Renderer>().sharedMaterial = LitMat(Hex("9E9EA6"), 0.85f, 0.70f);
        spawner.gatePlate = gate.transform;

        // ── Botón arcade ───────────────────────────────────────
        // Base fija (aro oscuro)
        Prim(PrimitiveType.Cylinder, "Button_Base", root.transform,
            new Vector3(0.032f, 0.005f, 0.032f),
            new Vector3(0f, -H*0.5f + 0.028f, -D*0.5f - 0.003f),
            Quaternion.Euler(90f, 0f, 0f))
            .GetComponent<Renderer>().sharedMaterial = LitMat(Hex("111316"), 0.15f, 0.5f);

        // Capuchón animable (verde emisivo)
        var btnCap = Prim(PrimitiveType.Cylinder, "Button_Cap", root.transform,
            new Vector3(0.026f, 0.005f, 0.026f),
            new Vector3(0f, -H*0.5f + 0.028f, -D*0.5f - 0.008f),
            Quaternion.Euler(90f, 0f, 0f));
        var btnMat = LitMat(Hex("00C864"), 0f, 0.35f);
        btnMat.EnableKeyword("_EMISSION");
        btnMat.SetColor("_EmissionColor", Hex("00C864") * 0.8f);
        btnMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        btnCap.GetComponent<Renderer>().sharedMaterial = btnMat;
        spawner.buttonCap      = btnCap.transform;
        spawner.buttonRenderer = btnCap.GetComponent<Renderer>();

        // ── Tira de LED inferior ───────────────────────────────
        var led = Solid("LED_Strip", root.transform, Hex("00C864"),
            new Vector3(W - 0.008f, 0.004f, 0.007f),
            new Vector3(0f, -H*0.5f + 0.002f, -D*0.3f));
        var ledMat = LitMat(Hex("00C864"), 0f, 0.55f);
        ledMat.EnableKeyword("_EMISSION");
        ledMat.SetColor("_EmissionColor", Hex("00C864") * 2f);
        ledMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        led.GetComponent<Renderer>().sharedMaterial = ledMat;
        spawner.ledRenderer = led.GetComponent<Renderer>();

        // ── Plaquita metálica de identificación ───────────────
        Solid("Label_Plate", root.transform, Hex("8A8A8A"),
            new Vector3(0.072f, 0.019f, 0.002f),
            new Vector3(0f, -H*0.5f + 0.030f, -D*0.5f - 0.001f))
            .GetComponent<Renderer>().sharedMaterial = LitMat(Hex("8A8A8A"), 0.82f, 0.65f);

        // ── Patas metálicas (4 esquinas) ─────────────────────
        foreach (var lp in new[]
        {
            new Vector3( W*0.42f, -H*0.5f - 0.005f,  D*0.37f),
            new Vector3(-W*0.42f, -H*0.5f - 0.005f,  D*0.37f),
            new Vector3( W*0.42f, -H*0.5f - 0.005f, -D*0.37f),
            new Vector3(-W*0.42f, -H*0.5f - 0.005f, -D*0.37f),
        })
        {
            Prim(PrimitiveType.Cylinder, "Leg", root.transform,
                new Vector3(0.007f, 0.005f, 0.007f), lp, Quaternion.identity)
                .GetComponent<Renderer>().sharedMaterial = LitMat(Hex("5A5A5A"), 0.88f, 0.60f);
        }

        // ── Canvas holográfico contador ────────────────────────
        spawner.counterText = BuildCounterCanvas(root.transform);

        return root;
    }

    static TMPro.TextMeshProUGUI BuildCounterCanvas(Transform parent)
    {
        var canvasGO = new GameObject("Counter_Canvas");
        canvasGO.transform.SetParent(parent, false);
        canvasGO.transform.localPosition = new Vector3(0f, H*0.5f + 0.014f, -D*0.5f + 0.002f);
        canvasGO.transform.localScale    = Vector3.one * 0.001f;

        var canvas        = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt            = canvasGO.GetComponent<RectTransform>();
        rt.sizeDelta      = new Vector2(82f, 18f);

        // Fondo oscuro
        var bg    = new GameObject("Bg");
        bg.transform.SetParent(canvasGO.transform, false);
        var img   = bg.AddComponent<UnityEngine.UI.Image>();
        img.color = new Color(0.06f, 0.07f, 0.09f, 0.92f);
        var bgRT  = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;

        // Texto contador
        var txtGO = new GameObject("Counter_Text");
        txtGO.transform.SetParent(canvasGO.transform, false);
        var txt   = txtGO.AddComponent<TMPro.TextMeshProUGUI>();
        txt.text  = "0/20";
        txt.fontSize   = 11f;
        txt.color      = new Color(0.20f, 0.90f, 0.38f);
        txt.fontStyle  = TMPro.FontStyles.Bold;
        txt.alignment  = TMPro.TextAlignmentOptions.Center;
        txt.raycastTarget = false;

        var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        if (font == null)
            font = Resources.Load<TMPro.TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null) txt.font = font;

        var txtRT  = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(3, 2); txtRT.offsetMax = new Vector2(-3, -2);

        return txt;
    }

    // ═════════════════════════════════════════
    //  PATCH ExplorerWorkstation.prefab
    // ═════════════════════════════════════════
    static bool PatchWorkstationPrefab(GameObject cablePrefab)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(WS_PATH);
        if (asset == null) { Debug.LogWarning("[CableBoxSetupTool] ExplorerWorkstation.prefab no encontrado."); return false; }

        using var scope = new PrefabUtility.EditPrefabContentsScope(WS_PATH);
        var prefabRoot  = scope.prefabContentsRoot;

        var existing = prefabRoot.GetComponentInChildren<CableBoxSpawner>(true);
        if (existing != null) { PatchSpawner(existing, cablePrefab); return false; }

        var box = BuildBoxGO(cablePrefab);
        box.transform.SetParent(prefabRoot.transform, false);
        box.transform.localPosition = new Vector3(-0.15f, 0.05f, 0f);
        return true;
    }

    static void PatchSpawner(CableBoxSpawner s, GameObject cablePrefab)
    {
        if (s.cablePrefab == null) s.cablePrefab = cablePrefab;
        EditorUtility.SetDirty(s);
    }

    // ═════════════════════════════════════════
    //  Helpers de construcción
    // ═════════════════════════════════════════

    // Primitive sin collider, con scale/position/rotation
    static GameObject Prim(PrimitiveType type, string name, Transform parent,
        Vector3 scale, Vector3 pos, Quaternion rot)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localScale    = scale;
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        var col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
        return go;
    }

    // Sólido opaco (Cube)
    static GameObject Solid(string name, Transform parent, Color color,
        Vector3 scale, Vector3 pos)
    {
        var go = Prim(PrimitiveType.Cube, name, parent, scale, pos, Quaternion.identity);
        go.GetComponent<Renderer>().sharedMaterial = LitMat(color, 0.1f, 0.4f);
        return go;
    }

    // Tira de trim con emisión naranja
    static void Trim(string name, Transform parent, Color color, Vector3 scale, Vector3 pos)
    {
        var go  = Prim(PrimitiveType.Cube, name, parent, scale, pos, Quaternion.identity);
        var mat = LitMat(color, 0f, 0.3f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * 0.5f);
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static GameObject FindGO(params string[] names)
    {
        foreach (var n in names) { var go = GameObject.Find(n); if (go) return go; }
        return null;
    }

    // ═════════════════════════════════════════
    //  Factories de materiales (URP Lit)
    // ═════════════════════════════════════════
    static Material LitMat(Color color, float metallic, float smoothness)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.color = color;
        mat.SetFloat("_Metallic",   metallic);
        mat.SetFloat("_Smoothness", smoothness);
        return mat;
    }

    static Material TransparentMat(Color color, float alpha)
    {
        var mat  = LitMat(color, 0f, 0.4f);
        var c    = color; c.a = alpha; mat.color = c;
        mat.SetFloat("_Surface", 1f);   // 1 = Transparent
        mat.SetFloat("_Blend",   0f);   // Alpha
        mat.SetInt("_SrcBlend",  5);    // SrcAlpha
        mat.SetInt("_DstBlend",  10);   // OneMinusSrcAlpha
        mat.SetInt("_ZWrite",    0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetOverrideTag("RenderType", "Transparent");
        return mat;
    }

    static Material LineMat(Color color)
    {
        var sh  = Shader.Find("Universal Render Pipeline/Particles/Lit")
               ?? Shader.Find("Sprites/Default");
        var mat = sh != null ? new Material(sh) : new Material(Shader.Find("Standard"));
        mat.color = color;
        return mat;
    }

    static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }
}
#endif

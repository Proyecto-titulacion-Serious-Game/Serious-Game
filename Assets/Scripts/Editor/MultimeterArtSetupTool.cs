#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using TMPro;

/// <summary>
/// Crea Multimeter_VR_Art.prefab usando Assets/Art/MutliMeter.obj como cuerpo visual.
///
/// ORIENTACIÓN DEL MODELO (medida directamente del OBJ a escala 0.01):
///   - Eje principal = Z  (longitud 16.49 cm, de −8.32 a +8.17 cm)
///   - Ancho         = X  (7.86 cm, de −3.93 a +3.93 cm)
///   - Espesor       = Y  (3.15 cm, de −2.32 a +0.83 cm)
///   - Display (pPlane1) en la cara SUPERIOR (+Y):  centro (0, 0.0023, −0.0513)
///   - Jacks de punta: pCube7=(+0.021, 0.003, −0.025)  pCube6=(−0.021, 0.003, −0.025)
///   - Botón modo (pCube2): (−0.0006, 0.0058, +0.0056)
///
/// El Canvas se coloca exactamente sobre pPlane1 con rotation Euler(−90,0,0)
/// para que la cara activa del canvas apunte hacia +Y (visible al mirar de arriba).
///
/// Menú: Tools → TITA → Multímetro → Crear Multímetro desde Art Asset
/// </summary>
public static class MultimeterArtSetupTool
{
    const string ART_OBJ_PATH = "Assets/Art/MutliMeter.obj";
    const string ART_MAT_PATH = "Assets/Art/Materials/MultiMeter_LP_1001_BaseColor.mat";
    const string OUTPUT_PATH  = "Assets/Prefabs/Multimeter_VR_Art.prefab";
    const string MAT_FOLDER   = "Assets/Materials/Multimeter";
    const string URP_LIT_GUID = "933532a4fcc9baf4fa0491de14d08ed7";

    // ── Posiciones exactas medidas del OBJ (metros, escala 0.01) ────────────────
    //
    // Polysurface2 (cuerpo principal):
    //   X: −3.93 a +3.93 cm  |  Y: −2.32 a +0.83 cm  |  Z: −8.32 a +8.17 cm
    //   → modelo horizontal, display en cara superior (+Y)
    //
    // pPlane1 (LCD inferior): 4 vértices en Y=0.00227, X ±0.02812, Z −0.06241 a −0.04010
    // pPlane2 (LCD superior): 4 vértices en Y=0.00411, X ±0.02812, Z −0.06241 a −0.04010
    //   → Canvas se coloca 1 mm por encima de pPlane2 (Y=0.00511)
    //   → Rotation Euler(−90,0,0): forward canvas = +Y, canvas-V axis = world −Z
    //
    // pCube7 (jack rojo):  top Y=0.00620, X=[0.01865,0.02305], Z=[−0.02824,−0.02254]
    // pCube6 (jack negro): top Y=0.00620, X=[−0.02305,−0.01865], Z=[−0.02824,−0.02254]
    //   → jacks centrados en X=±0.02085, Z=−0.02539
    //
    // MultiMeter_LP (botón modo): top Y=0.00834, centro ≈ (−0.0006, −, +0.0056)
    // ─────────────────────────────────────────────────────────────────────────

    static readonly Bounds MODEL_BOUNDS = new Bounds(
        new Vector3(0f, -0.0074f, -0.0008f),
        new Vector3(0.0786f, 0.0315f, 0.1649f));

    // Canvas exactamente sobre pPlane2 + 1 mm (Y=0.00411 + 0.001)
    static readonly Vector3 SCREEN_CENTER = new Vector3(0f, 0.00511f, -0.05126f);
    // Dimensiones exactas de pPlane1/2: X = 0.02812×2, Z = 0.06241−0.04010
    static readonly Vector2 SCREEN_SIZE_M = new Vector2(0.05624f, 0.02231f);

    // Centros de los jacks (top surface + 1 mm de margen)
    static readonly Vector3 JACK_RED   = new Vector3( 0.02085f, 0.00720f, -0.02539f);
    static readonly Vector3 JACK_BLACK = new Vector3(-0.02085f, 0.00720f, -0.02539f);

    // Botón modo: top Y=0.00834 + 1 mm, centro XZ de MultiMeter_LP
    static readonly Vector3 BTN_MODE = new Vector3(-0.0006f, 0.00934f, 0.00563f);

    // ─────────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/TITA/Multímetro/Crear Multímetro desde Art Asset")]
    public static void Create()
    {
        var meshes = AssetDatabase.LoadAllAssetsAtPath(ART_OBJ_PATH).OfType<Mesh>().ToArray();
        if (meshes.Length == 0)
        {
            EditorUtility.DisplayDialog("Asset no encontrado",
                $"No se encontraron meshes en:\n{ART_OBJ_PATH}\n\n" +
                "Verifica que esté importado (Assets → Reimport).", "OK");
            return;
        }

        var artMat = AssetDatabase.LoadAssetAtPath<Material>(ART_MAT_PATH);
        if (artMat == null)
            Debug.LogWarning($"[MultimeterArtSetupTool] Material {ART_MAT_PATH} no encontrado.");

        if (AssetDatabase.LoadAssetAtPath<GameObject>(OUTPUT_PATH) != null)
        {
            if (!EditorUtility.DisplayDialog("Prefab ya existe",
                    $"{OUTPUT_PATH}\n¿Sobreescribir?", "Sí", "Cancelar"))
                return;
        }

        EnsureMatFolder();

        // ── Raíz ─────────────────────────────────────────────────────────────
        var root = new GameObject("Multimeter_VR_Art");

        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;

        var grab = root.AddComponent<XRGrabInteractable>();
        grab.movementType  = XRBaseInteractable.MovementType.Kinematic;
        grab.trackPosition = true;
        grab.trackRotation = true;
        grab.throwOnDetach = false;

        var col = root.AddComponent<BoxCollider>();
        col.center = MODEL_BOUNDS.center;
        col.size   = MODEL_BOUNDS.size;

        // No empuja al jugador: en vez de collider trigger (rompía el agarre de las puntas con
        // XRGrabInteractable en este proyecto), se ignora la colisión puntual contra el
        // CharacterController del jugador — ver MultimeterIgnorePlayerCollision.
        root.AddComponent<MultimeterIgnorePlayerCollision>();

        // ── Cuerpo visual ─────────────────────────────────────────────────────
        BuildVisualBody(root, meshes, artMat);

        // ── Screen Canvas ─────────────────────────────────────────────────────
        // Posición exacta de pPlane1; rotación −90°X para que el canvas quede
        // horizontal sobre la cara superior y sea visible al mirar desde arriba.
        var (txtV, txtI, txtS, txtM) = BuildScreenCanvas(root);

        // ── Indicadores LED (sobre la pantalla, a cada lado) ─────────────────
        // Zona superior del cuerpo cerca del borde de la pantalla
        var indRed   = BuildIndicator(root, "Indicator_Red",   new Color(0.85f,0.1f,0.1f),
                            new Vector3(-0.022f, 0.0045f, -0.074f));
        var indBlack = BuildIndicator(root, "Indicator_Black", new Color(0.15f,0.15f,0.15f),
                            new Vector3( 0.022f, 0.0045f, -0.074f));

        // ── Cables con puntas físicas (SpringJoint + LineRenderer + XRGrabInteractable) ──
        BuildCableAssembly(root, "Red",   JACK_RED,   new Color(0.9f,0.1f,0.1f),
                           ProbeType.Red,   UnityEngine.XR.XRNode.RightHand);
        BuildCableAssembly(root, "Black", JACK_BLACK, new Color(0.1f,0.1f,0.1f),
                           ProbeType.Black, UnityEngine.XR.XRNode.LeftHand);

        // ── Botón de modo ─────────────────────────────────────────────────────
        BuildModeButton(root, BTN_MODE);

        // ── Script Multimeter ─────────────────────────────────────────────────
        var mm = root.AddComponent<Multimeter>();
        mm.txtVoltage     = txtV;
        mm.txtCurrent     = txtI;
        mm.txtStatus      = txtS;
        mm.txtMode        = txtM;
        mm.indicatorRed   = indRed.GetComponent<Renderer>();
        mm.indicatorBlack = indBlack.GetComponent<Renderer>();

        foreach (var p in root.GetComponentsInChildren<MultimeterProbe>(true))
            p.multimeter = mm;
        foreach (var b in root.GetComponentsInChildren<MultimeterModeButton>(true))
            b.multimeter = mm;

        // ── Guardar prefab ────────────────────────────────────────────────────
        bool saved;
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, OUTPUT_PATH, out saved);
        Object.DestroyImmediate(root);

        if (saved)
        {
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(prefab);
            Debug.Log($"[MultimeterArtSetupTool] ✓ Prefab guardado: {OUTPUT_PATH}");

            EditorUtility.DisplayDialog("Multímetro Art creado",
                $"Prefab: {OUTPUT_PATH}\n\n" +
                "Modelo 3D real (7.86 × 3.15 × 16.49 cm):\n" +
                "  • Display canvas alineado con pPlane1\n" +
                "  • Cables rojo/negro: SpringJoint + LineRenderer bezier\n" +
                "  • Probe tips: XRGrabInteractable + MultimeterProbe\n" +
                "  • Botón modo sobre pCube2\n\n" +
                "PASOS SIGUIENTES:\n" +
                "1. Arrastra el prefab a la escena del Explorador.\n" +
                "2. Colócalo horizontal (display hacia arriba) sobre la mesa.\n" +
                "3. Asigna este prefab al campo 'multimeter' de MultimeterUI.\n" +
                "4. Los cables cuelgan al soltar la punta; al soltar regresa al jack.\n" +
                "5. Si el texto no se ve: Tools→TITA→Multímetro→Ajustar Canvas Art", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Error",
                "No se pudo guardar. Verifica que exista Assets/Prefabs/.", "OK");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Herramienta auxiliar: reajustar canvas (post-creación si está desalineado)
    // ─────────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/TITA/Multímetro/Ajustar Canvas Art")]
    public static void AdjustCanvas()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(OUTPUT_PATH) == null)
        {
            EditorUtility.DisplayDialog("Error",
                $"No existe:\n{OUTPUT_PATH}\n\nEjecuta primero 'Crear Multímetro desde Art Asset'.", "OK");
            return;
        }

        var go = PrefabUtility.LoadPrefabContents(OUTPUT_PATH);
        var canvasT = go.transform.Find("Screen_Canvas");

        if (canvasT != null)
        {
            var rt = canvasT.GetComponent<RectTransform>();
            rt.localPosition = SCREEN_CENTER + new Vector3(0, 0.001f, 0);
            rt.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            // Canvas units: escala 0.001 → 1 unidad = 1 mm
            rt.sizeDelta = new Vector2(SCREEN_SIZE_M.x * 1000f, SCREEN_SIZE_M.y * 1000f);
            Debug.Log($"[MultimeterArtSetupTool] Canvas ajustado → pos={rt.localPosition}  rot=-90X  size={rt.sizeDelta}");
        }
        else
        {
            Debug.LogError("[MultimeterArtSetupTool] No se encontró 'Screen_Canvas' en el prefab.");
        }

        PrefabUtility.SaveAsPrefabAsset(go, OUTPUT_PATH);
        PrefabUtility.UnloadPrefabContents(go);
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Canvas ajustado",
            "Screen_Canvas reposicionado sobre pPlane1.\n" +
            "Verifica en Scene view que el texto quede sobre la pantalla.", "OK");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Builders
    // ─────────────────────────────────────────────────────────────────────────

    static void BuildVisualBody(GameObject root, Mesh[] meshes, Material artMat)
    {
        var body = MakeChild(root, "Visual_Body");
        body.transform.localScale = Vector3.one * 0.01f;

        // Fallback material si no se cargó el art
        Material fallback = null;

        foreach (var mesh in meshes)
        {
            var sub = new GameObject(mesh.name);
            sub.transform.SetParent(body.transform, false);
            sub.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = sub.AddComponent<MeshRenderer>();

            if (artMat != null)
            {
                mr.sharedMaterial = artMat;
            }
            else
            {
                if (fallback == null)
                {
                    var shader = GetURPLitShader();
                    fallback = new Material(shader);
                    fallback.SetColor("_BaseColor", new Color(0.15f, 0.15f, 0.15f));
                }
                mr.sharedMaterial = fallback;
            }
        }
    }

    /// <summary>
    /// Canvas WorldSpace posicionado sobre la pantalla LCD (pPlane1).
    /// Rotation Euler(−90,0,0): forward del canvas apunta +Y (cara activa hacia arriba).
    ///
    /// A escala 0.001, 1 unidad de canvas = 1 mm en world space.
    /// pPlane1 mide 56.2 × 22.3 mm → sizeDelta (56, 22).
    ///
    /// Layout de textos (en unidades de canvas / mm):
    ///   TMP_Voltage  anchoredPos (0, +7)  — valor grande, parte alta de pantalla
    ///   TMP_Current  anchoredPos (0,  0)  — valor secundario, centro
    ///   TMP_Status   anchoredPos (0, −5)  — texto de estado
    ///   TMP_Mode     anchoredPos (0, −9)  — modo activo, parte baja
    /// </summary>
    static (TMP_Text v, TMP_Text i, TMP_Text s, TMP_Text m) BuildScreenCanvas(GameObject root)
    {
        var canvasGO = MakeChild(root, "Screen_Canvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        var rt = canvasGO.GetComponent<RectTransform>();
        // Posición: exactamente sobre pPlane1, 1 mm por encima de la superficie
        rt.localPosition = SCREEN_CENTER + new Vector3(0, 0.001f, 0);
        // Rotación: −90° en X → canvas queda horizontal, cara activa apunta +Y
        rt.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        // Escala 0.001: 1 unidad de canvas = 1 mm
        rt.localScale = Vector3.one * 0.001f;
        // Tamaño del canvas en unidades (mm): coincide con pPlane1
        rt.sizeDelta = new Vector2(
            SCREEN_SIZE_M.x * 1000f,   // 56.2
            SCREEN_SIZE_M.y * 1000f);  // 22.3

        // Layout comprimido para encajar en 56×22 mm
        // Los anchoredPos son en mm (canvas units); positivo = hacia arriba de pantalla (−Z world)
        var txtV = BuildTMP(canvasGO, "TMP_Voltage",  "—.— V",        new Vector2(0,  7f), 8f, Color.green);
        var txtI = BuildTMP(canvasGO, "TMP_Current",  "—.— mA",       new Vector2(0,  0f), 6f, Color.green);
        var txtS = BuildTMP(canvasGO, "TMP_Status",   "SIN CONTACTO", new Vector2(0, -5f), 5f, new Color(0.6f, 1f, 0.6f));
        var txtM = BuildTMP(canvasGO, "TMP_Mode",     "DC VOLTAGE",   new Vector2(0, -9f), 4f, new Color(0.4f, 0.8f, 0.4f));

        return (txtV, txtI, txtS, txtM);
    }

    static GameObject BuildIndicator(GameObject root, string name, Color color, Vector3 pos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale    = Vector3.one * 0.004f;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        SaveColorMat(go, color, "ind_" + name.ToLower().Replace(" ", "_"));
        return go;
    }

    /// Creates a full cable assembly: Cable parent (LineRenderer + MultimeterCable),
    /// anchor empty at jack exit, visual nub, and probe tip with XRGrabInteractable +
    /// Rigidbody + MultimeterProbe (two SphereColliders: grab + trigger contact).
    static void BuildCableAssembly(
        GameObject root, string colorName, Vector3 jackPos,
        Color color, ProbeType probeType, UnityEngine.XR.XRNode hand)
    {
        // ── Cable parent (LineRenderer + MultimeterCable) ─────────────────────
        var cableGO = MakeChild(root, $"Cable_{colorName}");
        var lr      = cableGO.AddComponent<LineRenderer>();
        var cable   = cableGO.AddComponent<MultimeterCable>();
        cable.maxCableLength = 0.6f;
        cable.segments       = 16;
        cable.cableWidth     = 0.003f;
        cable.sagAmount      = 0.08f;
        cable.restOffset     = new Vector3(0f, 0.02f, 0f);

        lr.useWorldSpace   = true;
        lr.startWidth      = cable.cableWidth;
        lr.endWidth        = cable.cableWidth;
        lr.numCapVertices  = 4;
        lr.textureMode     = LineTextureMode.Stretch;

        // Persistent material for LineRenderer
        string cableMatPath = $"{MAT_FOLDER}/cable_{colorName.ToLower()}.mat";
        var cableMat = new Material(GetURPLitShader());
        cableMat.SetColor("_BaseColor", color);
        cableMat.color = color;
        if (AssetDatabase.LoadAssetAtPath<Material>(cableMatPath) != null)
            AssetDatabase.DeleteAsset(cableMatPath);
        AssetDatabase.CreateAsset(cableMat, cableMatPath);
        lr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(cableMatPath);

        // ── Anchor (empty GO at jack exit — SpringJoint origin) ────────────────
        var anchorGO = new GameObject($"Cable_Anchor_{colorName}");
        anchorGO.transform.SetParent(cableGO.transform, false);
        anchorGO.transform.localPosition = jackPos;
        cable.anchorPoint = anchorGO.transform;

        // Small visual nub at jack exit port
        var nub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        nub.name = $"Jack_Nub_{colorName}";
        nub.transform.SetParent(anchorGO.transform, false);
        nub.transform.localPosition = new Vector3(0f, 0.002f, 0f);
        nub.transform.localScale    = new Vector3(0.003f, 0.004f, 0.003f);
        Object.DestroyImmediate(nub.GetComponent<Collider>());
        SaveColorMat(nub, new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f),
                     $"nub_{colorName.ToLower()}");

        // ── Probe tip ─────────────────────────────────────────────────────────
        var probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        probe.name = $"Probe_{colorName}_Tip";
        probe.transform.SetParent(cableGO.transform, false);
        // Reposo ARRIBA del jack (fuera del BoxCollider del cuerpo, ver MODEL_BOUNDS): el nub/jack
        // visual se queda en jackPos (alineado con el modelo 3D), pero la punta AGARRABLE se levanta
        // fuera del volumen del cuerpo — si no, su collider de agarre queda adentro del collider
        // grande del multímetro y la mano termina agarrando el cuerpo por error al ir por la punta.
        // Hacia ARRIBA, no abajo: abajo puede meterla dentro de la mesa donde se apoya el multímetro.
        probe.transform.localPosition = jackPos + new Vector3(0f, 0.02f, 0f);
        probe.transform.localScale    = Vector3.one * 0.012f;
        SaveColorMat(probe, color, $"probe_{colorName.ToLower()}_tip");

        // Grab collider (non-trigger) — required by XRGrabInteractable (con trigger=true el
        // agarre de la punta dejaba de funcionar en este proyecto). El empujón contra el jugador
        // se resuelve con MultimeterIgnorePlayerCollision (root), no con triggers. Radio subido de
        // 0.55→0.9 (world ≈0.66cm→1.08cm): la punta original era muy chica para agarrar en VR.
        var grabCol = probe.GetComponent<SphereCollider>();
        grabCol.isTrigger = false;
        grabCol.radius    = 0.9f;

        // Trigger collider — activates MultimeterProbe.OnTriggerEnter fallback
        var trigCol = probe.AddComponent<SphereCollider>();
        trigCol.isTrigger = true;
        trigCol.radius    = 1.1f;

        // Rigidbody — starts kinematic; VelocityTracking makes it dynamic when grabbed
        var rb = probe.AddComponent<Rigidbody>();
        rb.isKinematic            = true;
        rb.useGravity             = true;
        rb.interpolation          = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // XRGrabInteractable — VelocityTracking so SpringJoint forces are honoured
        var grab = probe.AddComponent<XRGrabInteractable>();
        grab.movementType  = XRBaseInteractable.MovementType.VelocityTracking;
        grab.trackPosition = true;
        grab.trackRotation = true;
        grab.throwOnDetach = false;

        // MultimeterProbe
        var mp = probe.AddComponent<MultimeterProbe>();
        mp.probeType      = probeType;
        mp.controllerNode = hand;

        // Wire cable component → probe Rigidbody
        cable.probeRigidbody = rb;
    }

    static void BuildModeButton(GameObject root, Vector3 pos)
    {
        var btn = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        btn.name = "Mode_Button";
        btn.transform.SetParent(root.transform, false);
        btn.transform.localPosition = pos;
        btn.transform.localScale    = new Vector3(0.010f, 0.005f, 0.010f);
        SaveColorMat(btn, new Color(1f, 0.85f, 0f), "mat_btn_mode_art");

        Object.DestroyImmediate(btn.GetComponent<Collider>());
        btn.AddComponent<BoxCollider>().size = new Vector3(1.2f, 2f, 1.2f);
        btn.AddComponent<XRSimpleInteractable>();
        btn.AddComponent<MultimeterModeButton>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    static GameObject MakeChild(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    static TMP_Text BuildTMP(GameObject parent, string name, string text,
                              Vector2 pos, float size, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text             = text;
        tmp.fontSize         = size;
        tmp.color            = color;
        tmp.alignment        = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta        = new Vector2(54f, 10f);
        return tmp;
    }

    /// <summary>
    /// Crea y persiste el material como Asset para que sobreviva a SaveAsPrefabAsset.
    /// Un Material in-memory se pierde durante la serialización del prefab.
    /// </summary>
    static void SaveColorMat(GameObject go, Color color, string matName)
    {
        var r = go.GetComponent<Renderer>();
        if (r == null) return;

        string path = $"{MAT_FOLDER}/{matName}.mat";
        var shader  = GetURPLitShader();
        var mat     = new Material(shader);
        mat.SetColor("_BaseColor", color);
        mat.color = color;

        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mat, path);
        r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    static Shader GetURPLitShader() =>
        AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(URP_LIT_GUID))
        ?? Shader.Find("Universal Render Pipeline/Lit")
        ?? Shader.Find("Standard");

    static void EnsureMatFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MAT_FOLDER))
            AssetDatabase.CreateFolder("Assets/Materials", "Multimeter");
    }
}
#endif

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Genera el prefab completo del Explorador VR, incluyendo el XR Origin.
///
/// Menú: Tools → TITA → Generar Prefab Explorador
/// Resultado: Assets/Prefabs/Explorer_Player.prefab
///
/// Jerarquía generada:
///   Explorer_Player  [CharacterController, PlayerController, PlayerInteraction,
///                     HapticFeedback, ExplorerAvatar]
///   ├─ XR_Origin_VR              ← xrRig (pre-cableado)
///   │   └─ Camera_Offset
///   │       ├─ Main_Camera       ← headCamera y xrCamera (pre-cableados)
///   │       ├─ LeftHand_Controller  (placeholder — añade TrackedPoseDriver/
///   │       └─ RightHand_Controller  ActionBasedController manualmente)
///   ├─ RobotKyle_Explorer
///   ├─ ComponentReceiver
///   │   ├─ Bandeja_Recepcion
///   │   ├─ Tray_Visual
///   │   └─ Tray_Label
///   └─ Explorer_StatusPanel
///
/// REFERENCIAS INTERNAS — ya cableadas al generar:
///   PlayerController.xrRig        → XR_Origin_VR.transform
///   PlayerController.headCamera   → Main_Camera (Camera)
///   PlayerController.interaction  → PlayerInteraction (mismo GO)
///   ExplorerAvatar.xrCamera       → Main_Camera.transform
///   ExplorerAvatar.avatarRoot     → RobotKyle_Explorer.transform
///   ExplorerComponentReceiver.puntoDeEntrega → Bandeja_Recepcion.transform
///
/// REFERENCIAS EXTERNAS — asignar en Inspector tras colocar en escena:
///   PlayerInteraction.gameManager → GameManager_System
///   PlayerInteraction.circuit     → CircuitManager activo
///   PlayerInteraction.multimeter  → Multimeter_VR
///
/// COMPONENTES XR A AÑADIR MANUALMENTE en Main_Camera:
///   • TrackedPoseDriver    (XR / Input System)
///   • XROrigin             (Unity.XR.CoreUtils) — requerido por XRI interactions
/// EN LeftHand_Controller y RightHand_Controller:
///   • ActionBasedController + XRRayInteractor (o XRDirectInteractor)
///   • Asignar Input Action Assets del proyecto
/// </summary>
public static class ExplorerPrefabGenerator
{
    private const string PREFAB_PATH = "Assets/Prefabs/Explorer_Player.prefab";
    private const string KYLE_PATH   = "Assets/UnityTechnologies/SpaceRobotKyle/Prefabs/RobotKyle.prefab";

    [MenuItem("Tools/TITA/Generar Prefab Explorador")]
    public static void Generate()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH) != null)
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Prefab ya existe",
                $"Ya existe un prefab en:\n{PREFAB_PATH}\n\n¿Sobreescribir?",
                "Sí, sobreescribir", "Cancelar");
            if (!overwrite) return;
        }

        // ── Raíz ────────────────────────────────────────────────────────────
        var root = new GameObject("Explorer_Player");

        var cc       = root.AddComponent<CharacterController>();
        cc.height    = 1.8f;
        cc.radius    = 0.3f;
        cc.center    = new Vector3(0f, 0.9f, 0f);
        cc.stepOffset = 0.3f;
        cc.skinWidth  = 0.08f;

        var playerCtrl  = root.AddComponent<PlayerController>();
        var playerInter = root.AddComponent<PlayerInteraction>();
        var haptics     = root.AddComponent<HapticFeedback>();
        var avatar      = root.AddComponent<ExplorerAvatar>();

        // Cableado interno en la raíz
        playerCtrl.interaction        = playerInter;
        playerInter.playerController  = playerCtrl;
        playerInter.haptics           = haptics;

        playerCtrl.useKatVR           = true;   // KAT VR por defecto; cae a joystick si no hay caminadora
        playerCtrl.walkSpeed          = 2f;
        playerCtrl.katSpeedMultiplier = 1f;

        // ── XR Origin ────────────────────────────────────────────────────────
        var xrOriginGO = new GameObject("XR_Origin_VR");
        xrOriginGO.transform.SetParent(root.transform, false);
        xrOriginGO.transform.localPosition = Vector3.zero;

        // Camera Offset — controla la altura del suelo (XROrigin.CameraYOffset)
        var cameraOffset = new GameObject("Camera_Offset");
        cameraOffset.transform.SetParent(xrOriginGO.transform, false);
        cameraOffset.transform.localPosition = new Vector3(0f, 0f, 0f);

        // Main Camera
        var mainCameraGO = new GameObject("Main_Camera");
        mainCameraGO.transform.SetParent(cameraOffset.transform, false);
        mainCameraGO.transform.localPosition = Vector3.zero;
        mainCameraGO.tag = "MainCamera";

        var mainCamera = mainCameraGO.AddComponent<Camera>();
        mainCamera.nearClipPlane = 0.01f;
        mainCameraGO.AddComponent<AudioListener>();
        // TrackedPoseDriver y XROrigin: añadir manualmente o mediante XRI Setup Wizard

        // Controladores — placeholders con tags; añadir ActionBasedController manualmente
        EnsureTag("LeftHand");
        EnsureTag("RightHand");

        var leftCtrl = new GameObject("LeftHand_Controller");
        leftCtrl.transform.SetParent(cameraOffset.transform, false);
        leftCtrl.tag = "LeftHand";

        var rightCtrl = new GameObject("RightHand_Controller");
        rightCtrl.transform.SetParent(cameraOffset.transform, false);
        rightCtrl.tag = "RightHand";

        // Cablear PlayerController → XR Origin y Main Camera
        playerCtrl.xrRig     = xrOriginGO.transform;
        playerCtrl.headCamera = mainCamera;

        // Cablear ExplorerAvatar → Main Camera
        avatar.xrCamera         = mainCameraGO.transform;
        avatar.hideHeadInVR     = true;
        avatar.rotationSmoothing = 10f;

        // ── Robot RobotKyle ─────────────────────────────────────────────────
        var kylePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(KYLE_PATH);
        GameObject kyle;

        if (kylePrefab != null)
        {
            kyle = (GameObject)PrefabUtility.InstantiatePrefab(kylePrefab, root.transform);
            kyle.name = "RobotKyle_Explorer";
            kyle.transform.localPosition = Vector3.zero;
            kyle.transform.localRotation = Quaternion.identity;
        }
        else
        {
            kyle = CreateBox(root, "RobotKyle_Explorer [PLACEHOLDER]",
                new Vector3(0.4f, 1.7f, 0.3f),
                new Vector3(0f, 0.85f, 0f),
                CreateMat("Mat_ExplorerPlaceholder", new Color(0.5f, 0.7f, 1f)));
            Debug.LogWarning("[ExplorerGenerator] RobotKyle.prefab no encontrado. " +
                             "Importa SpaceRobotKyle y reasigna avatarRoot manualmente.");
        }

        avatar.avatarRoot = kyle.transform;

        // ── Bandeja de Recepción ─────────────────────────────────────────────
        var receiverGO = new GameObject("ComponentReceiver");
        receiverGO.transform.SetParent(root.transform, false);
        receiverGO.transform.localPosition = new Vector3(0f, 1.0f, 0.5f);

        var trayAnchor = new GameObject("Bandeja_Recepcion");
        trayAnchor.transform.SetParent(receiverGO.transform, false);
        trayAnchor.transform.localPosition = Vector3.zero;

        // Slots individuales por tipo — los componentes aparecen separados
        var slotResistor   = new GameObject("Slot_Resistor");
        var slotLED        = new GameObject("Slot_LED");
        var slotCapacitor  = new GameObject("Slot_Capacitor");
        var slotArduinoPin = new GameObject("Slot_ArduinoPin");

        slotResistor  .transform.SetParent(trayAnchor.transform, false);
        slotLED       .transform.SetParent(trayAnchor.transform, false);
        slotCapacitor .transform.SetParent(trayAnchor.transform, false);
        slotArduinoPin.transform.SetParent(trayAnchor.transform, false);

        slotResistor  .transform.localPosition = new Vector3(-0.09f, 0f, 0f);
        slotLED       .transform.localPosition = new Vector3(-0.03f, 0f, 0f);
        slotCapacitor .transform.localPosition = new Vector3( 0.03f, 0f, 0f);
        slotArduinoPin.transform.localPosition = new Vector3( 0.09f, 0f, 0f);

        CreateBox(receiverGO, "Tray_Visual",
            new Vector3(0.25f, 0.02f, 0.2f), new Vector3(0f, -0.01f, 0f),
            CreateMat("Mat_ExplorerTray", new Color(0.15f, 0.22f, 0.35f)));

        var trayCollider      = receiverGO.AddComponent<BoxCollider>();
        trayCollider.size     = new Vector3(0.25f, 0.15f, 0.2f);
        trayCollider.center   = new Vector3(0f, 0.07f, 0f);
        trayCollider.isTrigger = true;

        var receiver = receiverGO.AddComponent<ExplorerComponentReceiver>();
        receiver.puntoDeEntrega = trayAnchor.transform;

        // Slots por tipo
        receiver.slotResistor   = slotResistor.transform;
        receiver.slotLED        = slotLED.transform;
        receiver.slotCapacitor  = slotCapacitor.transform;
        receiver.slotArduinoPin = slotArduinoPin.transform;

        // Prefabs base
        const string D = "Assets/Prefabs/Delivered";
        receiver.resistorPrefab   = AssetDatabase.LoadAssetAtPath<GameObject>(D + "/Delivered_Resistor.prefab");
        receiver.ledPrefab        = AssetDatabase.LoadAssetAtPath<GameObject>(D + "/Delivered_LED_Green.prefab")
                                 ?? AssetDatabase.LoadAssetAtPath<GameObject>(D + "/Delivered_LED.prefab");
        receiver.capacitorPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>(D + "/Delivered_Capacitor_Blue.prefab")
                                 ?? AssetDatabase.LoadAssetAtPath<GameObject>(D + "/Delivered_Capacitor.prefab");
        receiver.arduinoPinPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(D + "/Delivered_ArduinoPIn.prefab");

        // Variantes LED
        receiver.ledGreenPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>(D + "/Delivered_LED_Green.prefab");
        receiver.ledRedPrefab    = AssetDatabase.LoadAssetAtPath<GameObject>(D + "/Delivered_LED_Red.prefab");
        receiver.ledYellowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(D + "/Delivered_LED_Yellow.prefab");

        // Variantes Capacitor
        receiver.capacitorBluePrefab   = AssetDatabase.LoadAssetAtPath<GameObject>(D + "/Delivered_Capacitor_Blue.prefab");
        receiver.capacitorBlackPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>(D + "/Delivered_Capacitor_Black.prefab");
        receiver.capacitorOrangePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(D + "/Delivered_Capacitor_Orange.prefab");

        // Variante Resistor
        receiver.resistorVerticalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(D + "/Delivered_Resistor_Vertical.prefab");

        // Label de la bandeja
        var labelGO = new GameObject("Tray_Label");
        labelGO.transform.SetParent(receiverGO.transform, false);
        labelGO.transform.localPosition = new Vector3(0f, 0.12f, 0f);
        labelGO.transform.localScale    = Vector3.one * 0.003f;

        var labelCanvas       = labelGO.AddComponent<Canvas>();
        labelCanvas.renderMode = RenderMode.WorldSpace;
        labelGO.AddComponent<CanvasScaler>();
        labelGO.GetComponent<RectTransform>().sizeDelta = new Vector2(120f, 30f);

        var labelTxt = CreateTMP(labelGO, "TMP_BandejaLabel",
            "BANDEJA\nRecepción", Vector2.zero,
            new Vector2(115f, 28f), 9f, new Color(0.4f, 0.8f, 1f));
        labelTxt.alignment = TextAlignmentOptions.Center;

        // ── Panel de estado ──────────────────────────────────────────────────
        var statusGO = new GameObject("Explorer_StatusPanel");
        statusGO.transform.SetParent(root.transform, false);
        statusGO.transform.localPosition = new Vector3(0f, 1.5f, 0.6f);
        statusGO.transform.localScale    = Vector3.one * 0.001f;

        var statusCanvas       = statusGO.AddComponent<Canvas>();
        statusCanvas.renderMode = RenderMode.WorldSpace;
        statusGO.AddComponent<CanvasScaler>();
        statusGO.GetComponent<RectTransform>().sizeDelta = new Vector2(200f, 60f);

        var bgGO  = new GameObject("Background");
        bgGO.transform.SetParent(statusGO.transform, false);
        var bgRT  = bgGO.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;
        bgGO.AddComponent<Image>().color = new Color(0.05f, 0.08f, 0.18f, 0.85f);

        CreateTMP(statusGO, "TMP_Status",
            "Explorador listo",
            new Vector2(0f, 10f), new Vector2(190f, 22f),
            9f, new Color(0.4f, 1f, 0.6f));
        CreateTMP(statusGO, "TMP_Instruccion",
            "Usa el multímetro para medir",
            new Vector2(0f, -12f), new Vector2(190f, 18f),
            7f, new Color(0.9f, 0.9f, 0.6f));

        // ── Guardar prefab ────────────────────────────────────────────────────
        bool saved;
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH, out saved);
        Object.DestroyImmediate(root);

        if (saved)
        {
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(prefab);
            EditorUtility.DisplayDialog(
                "Explorer_Player creado",
                $"Guardado en:\n{PREFAB_PATH}\n\n" +
                "REFERENCIAS INTERNAS — ya cableadas:\n" +
                "  ✓ PlayerController.xrRig        → XR_Origin_VR\n" +
                "  ✓ PlayerController.headCamera   → Main_Camera\n" +
                "  ✓ PlayerController.interaction  → PlayerInteraction\n" +
                "  ✓ ExplorerAvatar.xrCamera       → Main_Camera\n" +
                "  ✓ ExplorerAvatar.avatarRoot     → RobotKyle_Explorer\n" +
                "  ✓ Receiver.puntoDeEntrega       → Bandeja_Recepcion\n" +
                "  ✓ Receiver.slotResistor/LED/Capacitor/ArduinoPin → asignados\n" +
                "  ✓ Receiver.prefabs base + variantes              → asignados\n\n" +
                "ASIGNAR MANUALMENTE en el Inspector:\n" +
                "  PlayerInteraction:\n" +
                "    • gameManager  → GameManager_System\n" +
                "    • circuit      → CircuitManager activo\n" +
                "    • multimeter   → Multimeter_VR\n\n" +
                "COMPONENTES XR a añadir en Main_Camera:\n" +
                "  • TrackedPoseDriver  (sigue la posición del HMD)\n" +
                "  • XROrigin           (en XR_Origin_VR, vía Unity.XR.CoreUtils)\n\n" +
                "EN LeftHand_Controller / RightHand_Controller:\n" +
                "  • ActionBasedController + XRRayInteractor\n" +
                "  • Asignar Input Action Assets del proyecto\n\n" +
                "TIP: usa el XRI Setup Wizard (Edit → Project Settings → XR Interaction\n" +
                "Toolkit → 'Install Starter Assets') para generar controladores listos.",
                "OK");
            Debug.Log($"[ExplorerPrefabGenerator] Prefab guardado en {PREFAB_PATH}");
        }
        else
        {
            EditorUtility.DisplayDialog("Error",
                "No se pudo guardar el prefab.\n" +
                "Verifica que exista la carpeta Assets/Prefabs/.", "OK");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static GameObject CreateBox(GameObject parent, string name,
                                 Vector3 scale, Vector3 localPos, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = scale;
        go.AddComponent<MeshFilter>().sharedMesh     = GetMesh(PrimitiveType.Cube);
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.AddComponent<BoxCollider>();
        return go;
    }

    static TMP_Text CreateTMP(GameObject parent, string name, string text,
                               Vector2 pos, Vector2 size, float fontSize, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text   = text;
        tmp.fontSize = fontSize;
        tmp.color    = color;
        tmp.alignment          = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        var rt              = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        return tmp;
    }

    static Material CreateMat(string matName, Color color)
    {
        string path     = $"Assets/Materials/{matName}.mat";
        var    existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat    = new Material(shader);
        mat.SetColor("_BaseColor", color);
        mat.SetColor("_Color",     color);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Mesh GetMesh(PrimitiveType type)
    {
        var tmp = GameObject.CreatePrimitive(type);
        var m   = tmp.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(tmp);
        return m;
    }

    static void EnsureTag(string tag)
    {
        var tagManager = new UnityEditor.SerializedObject(
            AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/TagManager.asset"));
        var tags = tagManager.FindProperty("tags");
        for (int i = 0; i < tags.arraySize; i++)
            if (tags.GetArrayElementAtIndex(i).stringValue == tag) return;
        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
        tagManager.ApplyModifiedProperties();
    }
}

using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Reemplaza el XR_Origin_VR interno del prefab Explorer_Player con el prefab oficial
/// "XR Origin (XR Rig)" de Starter Assets (XRI 3.4.1), que ya incluye:
///   - TrackedPoseDriver en Main Camera (centerEyePosition / centerEyeRotation)
///   - NearFarInteractor + TeleportInteractor en Left Controller / Right Controller
///   - ControllerInputActionManager en cada controller
///
/// Además añade al XR Origin instanciado:
///   - InputActionManager  → habilita XRI Default Input Actions en runtime
///   - XRInputModalityManager → alterna entre visual de controlador y mano Quest
///   - XR Controller Left/Right.prefab → visual del controlador (modo controlador)
///   - LeftHandQuestVisual/RightHandQuestVisual.prefab → visual de mano (modo mano)
///
/// Y cablea en PlayerController:
///   - xrRig      → transform raíz del nuevo XR Origin
///   - headCamera → Main Camera del XR Origin
///   - moveAction → XRI Left Locomotion/Move de XRI Default Input Actions
///
/// Menu: Tools → TITA → Actualizar Prefab Explorer_Player
/// </summary>
public static class ExplorerPrefabUpdater
{
    // ── Paths ──────────────────────────────────────────────────────────────
    const string k_PrefabPath      = "Assets/Prefabs/Explorer_Player.prefab";
    const string k_XROriginPath    = "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
    const string k_LeftVisualPath  = "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/Controllers/XR Controller Left.prefab";
    const string k_RightVisualPath = "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/Controllers/XR Controller Right.prefab";
    const string k_LeftHandVisual  = "Assets/Samples/XR Interaction Toolkit/3.4.1/Hands Interaction Demo/Prefabs/LeftHandQuestVisual.prefab";
    const string k_RightHandVisual = "Assets/Samples/XR Interaction Toolkit/3.4.1/Hands Interaction Demo/Prefabs/RightHandQuestVisual.prefab";

    // ── Script GUIDs (evita dependencias de assembly) ─────────────────────
    const string k_IAMGuid      = "017c5e3933235514c9520e1dace2a4b2"; // InputActionManager
    const string k_ModalityGuid = "82bc72d2ecc8add47b2fe00d40318500"; // XRInputModalityManager
    const string k_ActionsGuid  = "c348712bda248c246b8c49b3db54643f"; // XRI Default Input Actions

    [MenuItem("Tools/TITA/Actualizar Prefab Explorer_Player")]
    static void Run()
    {
        // ── Cargar assets ──────────────────────────────────────────────────
        var xrOriginPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>(k_XROriginPath);
        var leftVisual      = AssetDatabase.LoadAssetAtPath<GameObject>(k_LeftVisualPath);
        var rightVisual     = AssetDatabase.LoadAssetAtPath<GameObject>(k_RightVisualPath);
        var leftHandVisual  = AssetDatabase.LoadAssetAtPath<GameObject>(k_LeftHandVisual);
        var rightHandVisual = AssetDatabase.LoadAssetAtPath<GameObject>(k_RightHandVisual);

        if (xrOriginPrefab == null)
        {
            EditorUtility.DisplayDialog("Actualizar Prefab",
                "No se encontró 'XR Origin (XR Rig).prefab'.\n\n" +
                "Verifica que el sample 'Starter Assets' de XRI 3.4.1 está importado en:\n" +
                "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/", "OK");
            return;
        }

        var actionsAsset = LoadActionsAsset();

        var log = new StringBuilder();
        log.AppendLine($"[ExplorerPrefabUpdater] {k_PrefabPath}\n");

        var root = PrefabUtility.LoadPrefabContents(k_PrefabPath);
        try
        {
            // ── 1. Eliminar rig anterior ───────────────────────────────────
            // Puede llamarse XR_Origin_VR (estructura vieja) o XR Origin (XR Rig) (ejecución previa)
            foreach (var candidateName in new[] { "XR_Origin_VR", "XR Origin (XR Rig)", "XR Origin" })
            {
                var old = FindDeep(root, candidateName);
                if (old != null)
                {
                    Object.DestroyImmediate(old);
                    log.AppendLine($"  ✓ '{candidateName}' anterior eliminado");
                    break;
                }
            }

            // ── 2. Instanciar XR Origin (XR Rig) oficial ──────────────────
            var xrOriginGO = (GameObject)PrefabUtility.InstantiatePrefab(xrOriginPrefab, root.transform);
            xrOriginGO.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            log.AppendLine($"  ✓ '{xrOriginGO.name}' instanciado como nested prefab");

            // ── 3. Cablear PlayerController ────────────────────────────────
            var pc = root.GetComponent<PlayerController>();
            if (pc != null)
                WirePlayerController(pc, xrOriginGO, actionsAsset, log);
            else
                log.AppendLine("  ✗ PlayerController no encontrado en la raíz del prefab");

            // ── 4. InputActionManager en XR Origin ─────────────────────────
            if (actionsAsset != null)
                AddInputActionManager(xrOriginGO, actionsAsset, log);

            // ── 5. Visuales: controlador + mano en cada controller ─────────
            AddHandAndControllerVisuals(xrOriginGO, "Left Controller",
                leftVisual,     "XR Controller Left",
                leftHandVisual, "LeftHandQuestVisual", log);

            AddHandAndControllerVisuals(xrOriginGO, "Right Controller",
                rightVisual,     "XR Controller Right",
                rightHandVisual, "RightHandQuestVisual", log);

            // ── 6. XRInputModalityManager ──────────────────────────────────
            AddModalityManager(xrOriginGO, log);

            // ── 7. ExplorerAvatar ──────────────────────────────────────────
            var avatar = root.GetComponent<ExplorerAvatar>();
            if (avatar != null && pc?.headCamera != null)
            {
                avatar.xrCamera = pc.headCamera.transform;
                log.AppendLine($"  ✓ ExplorerAvatar.xrCamera → {pc.headCamera.name}");
            }

            PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath);
            log.AppendLine("\nPrefab guardado correctamente.");
        }
        catch (System.Exception ex)
        {
            log.AppendLine($"\n✗ Error: {ex.Message}");
            Debug.LogException(ex);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("Actualizar Prefab Explorer_Player", log.ToString(), "OK");
        AssetDatabase.Refresh();
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Secciones
    // ──────────────────────────────────────────────────────────────────────

    static void WirePlayerController(PlayerController pc, GameObject xrOriginGO,
                                      InputActionAsset actionsAsset, StringBuilder log)
    {
        log.AppendLine("\n[PlayerController]");

        // NO tocar useKatVR: lo gestiona Tools → TITA → Explorador → Activar/Desactivar KAT VR.
        // Antes esto lo forzaba a false y apagaba la caminadora cada vez que se corría el updater.
        log.AppendLine($"  — useKatVR preservado ({pc.useKatVR})");
        pc.xrRig              = xrOriginGO.transform;
        if (pc.snapTurnAngle    <= 0f) pc.snapTurnAngle    = 45f;
        if (pc.snapTurnThreshold <= 0f) pc.snapTurnThreshold = 0.5f;
        log.AppendLine($"  ✓ xrRig → {xrOriginGO.name}");

        var mainCam = FindDeep(xrOriginGO, "Main Camera")?.GetComponent<Camera>();
        if (mainCam != null)
        {
            pc.headCamera = mainCam;
            log.AppendLine("  ✓ headCamera → Main Camera");
        }
        else log.AppendLine("  ✗ headCamera: 'Main Camera' no encontrada en XR Origin");

        // moveAction: busca sub-asset InputActionReference dentro de XRI Default Input Actions
        if (actionsAsset != null && pc.moveAction == null)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(k_ActionsGuid);
            if (!string.IsNullOrEmpty(assetPath))
            {
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                {
                    if (a is InputActionReference iar && iar.action != null)
                    {
                        string mapName = iar.action.actionMap?.name ?? "";
                        if (iar.action.name == "Move" && mapName.Contains("Left") && mapName.Contains("Locomotion"))
                        {
                            pc.moveAction = iar;
                            log.AppendLine($"  ✓ moveAction → {mapName}/Move");
                            break;
                        }
                    }
                }
            }
            if (pc.moveAction == null)
                log.AppendLine("  ✗ moveAction: no se encontró 'XRI Left Locomotion/Move'");
        }
        else if (pc.moveAction != null)
            log.AppendLine($"  — moveAction ya asignado ({pc.moveAction.name})");
    }

    static void AddInputActionManager(GameObject xrOriginGO, InputActionAsset actionsAsset,
                                       StringBuilder log)
    {
        log.AppendLine("\n[InputActionManager]");

        var iamScript = AssetDatabase.LoadAssetAtPath<MonoScript>(
            AssetDatabase.GUIDToAssetPath(k_IAMGuid));
        if (iamScript == null)
        {
            log.AppendLine("  ✗ Script no encontrado (GUID: " + k_IAMGuid + ")");
            return;
        }

        var iamType = iamScript.GetClass();
        var iam = xrOriginGO.GetComponent(iamType);
        if (iam == null)
        {
            iam = xrOriginGO.AddComponent(iamType);
            log.AppendLine("  ✓ InputActionManager añadido");
        }
        else log.AppendLine("  — InputActionManager ya presente");

        var so = new SerializedObject(iam);
        so.Update();
        var assetsProp = so.FindProperty("m_ActionAssets");
        if (assetsProp == null) { log.AppendLine("  ✗ m_ActionAssets no encontrado"); return; }

        bool alreadyIn = false;
        for (int i = 0; i < assetsProp.arraySize; i++)
            if (assetsProp.GetArrayElementAtIndex(i).objectReferenceValue == actionsAsset)
            { alreadyIn = true; break; }

        if (!alreadyIn)
        {
            assetsProp.arraySize++;
            assetsProp.GetArrayElementAtIndex(assetsProp.arraySize - 1).objectReferenceValue = actionsAsset;
            so.ApplyModifiedProperties();
            log.AppendLine($"  ✓ '{actionsAsset.name}' asignado");
        }
        else log.AppendLine($"  — '{actionsAsset.name}' ya asignado");
    }

    static void AddHandAndControllerVisuals(GameObject xrOriginGO, string controllerName,
                                             GameObject controllerPrefab, string controllerChildName,
                                             GameObject handPrefab,       string handChildName,
                                             StringBuilder log)
    {
        var controllerGO = FindDeep(xrOriginGO, controllerName);
        if (controllerGO == null)
        {
            log.AppendLine($"\n  ✗ '{controllerName}' no encontrado en XR Origin");
            return;
        }

        log.AppendLine($"\n[{controllerName}]");

        // Visual controlador
        if (controllerPrefab != null && controllerGO.transform.Find(controllerChildName) == null)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(controllerPrefab, controllerGO.transform);
            inst.name = controllerChildName;
            inst.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            log.AppendLine($"  ✓ '{controllerChildName}' instanciado (visual controlador)");
        }
        else log.AppendLine($"  — '{controllerChildName}' ya presente");

        // Visual mano Quest (inactivo; XRInputModalityManager lo activa en runtime)
        if (handPrefab != null && controllerGO.transform.Find(handChildName) == null)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab, controllerGO.transform);
            inst.name = handChildName;
            inst.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.SetActive(false);
            log.AppendLine($"  ✓ '{handChildName}' instanciado (inactivo por defecto)");
        }
        else if (handPrefab == null)
            log.AppendLine("  — HandQuestVisual: sample 'Hands Interaction Demo' no importado");
        else
            log.AppendLine($"  — '{handChildName}' ya presente");
    }

    static void AddModalityManager(GameObject xrOriginGO, StringBuilder log)
    {
        log.AppendLine("\n[XRInputModalityManager]");

        var modalityScript = AssetDatabase.LoadAssetAtPath<MonoScript>(
            AssetDatabase.GUIDToAssetPath(k_ModalityGuid));
        if (modalityScript == null)
        {
            log.AppendLine("  ✗ Script no encontrado (GUID: " + k_ModalityGuid + ")");
            return;
        }

        var modalityType = modalityScript.GetClass();
        var modality = xrOriginGO.GetComponent(modalityType);
        if (modality == null)
        {
            modality = xrOriginGO.AddComponent(modalityType);
            log.AppendLine("  ✓ XRInputModalityManager añadido");
        }
        else log.AppendLine("  — XRInputModalityManager ya presente");

        var so = new SerializedObject(modality);
        so.Update();

        // m_LeftController / m_RightController → visual del controlador (se desactiva con manos)
        // m_LeftHand / m_RightHand             → visual de mano Quest   (se activa con manos)
        bool changed = false;
        changed |= SetModalityProp(so, "m_LeftController",  FindDeep(xrOriginGO, "XR Controller Left"),   log);
        changed |= SetModalityProp(so, "m_RightController", FindDeep(xrOriginGO, "XR Controller Right"),  log);
        changed |= SetModalityProp(so, "m_LeftHand",        FindDeep(xrOriginGO, "LeftHandQuestVisual"),  log);
        changed |= SetModalityProp(so, "m_RightHand",       FindDeep(xrOriginGO, "RightHandQuestVisual"), log);

        if (changed) so.ApplyModifiedProperties();
    }

    static bool SetModalityProp(SerializedObject so, string propName, GameObject target, StringBuilder log)
    {
        if (target == null) return false;
        var prop = so.FindProperty(propName);
        if (prop == null || prop.objectReferenceValue == target) return false;
        prop.objectReferenceValue = target;
        log.AppendLine($"  ✓ {propName} → '{target.name}'");
        return true;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────

    static InputActionAsset LoadActionsAsset()
    {
        var path = AssetDatabase.GUIDToAssetPath(k_ActionsGuid);
        if (!string.IsNullOrEmpty(path))
        {
            var a = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
            if (a != null) return a;
        }
        foreach (var g in AssetDatabase.FindAssets("XRI Default Input Actions t:InputActionAsset"))
        {
            var a = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetDatabase.GUIDToAssetPath(g));
            if (a != null) return a;
        }
        Debug.LogWarning("[ExplorerPrefabUpdater] XRI Default Input Actions no encontrado.");
        return null;
    }

    static GameObject FindDeep(GameObject root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t.gameObject;
        return null;
    }
}

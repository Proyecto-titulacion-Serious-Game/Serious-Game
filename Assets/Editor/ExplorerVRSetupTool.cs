using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

/// <summary>
/// Configura en un solo click todos los componentes VR del Explorador en la escena activa.
///
/// Menu: Tools → TITA → [LEGACY] Setup Explorador VR Específico
///
/// Qué hace:
///   1. PlayerController    — useKatVR, snapTurn, xrRig, headCamera, moveAction
///   2. ExplorerAvatar      — xrCamera, avatarRoot, avatarAnimator
///   3. Manos               — TrackedPoseDriver + HandModelController + XRDirectInteractor
///   4. RuntimeHandConsolidator — verifica que está en el XR Origin
///   5. ConnectionManager   — modoOffline = true para testing sin Fusion
///   6. XRRenderGuard       — verifica que está en Main_Camera
///   7. Cámaras secundarias — desactiva las que no tienen TrackedPoseDriver
/// </summary>
public static class ExplorerVRSetupTool
{
    [MenuItem("Tools/TITA/[LEGACY] Setup Explorador VR Específico")]
    static void Run()
    {
        var log     = new StringBuilder();
        bool changed = false;

        // ── 1. PlayerController ──────────────────────────────────────────────
        var pc = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (pc == null)
        {
            // Intentar añadir PlayerController directamente al XR Origin
            var xrOriginForPC = FindXROriginInScene();
            if (xrOriginForPC == null)
            {
                EditorUtility.DisplayDialog("Setup VR Explorador",
                    "No se encontró PlayerController ni XR Origin (XR Rig) en la escena.\n\n" +
                    "Pasos:\n" +
                    "1. Arrastra el prefab 'XR Origin (XR Rig)' desde:\n" +
                    "   Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/\n" +
                    "   (o usa GameObject → XR → XR Origin (VR))\n" +
                    "2. Vuelve a correr este tool.", "OK");
                return;
            }

            if (xrOriginForPC.GetComponent<CharacterController>() == null)
            {
                Undo.AddComponent<CharacterController>(xrOriginForPC);
                log.AppendLine($"  ✓ CharacterController añadido a '{xrOriginForPC.name}'");
                changed = true;
            }
            pc = Undo.AddComponent<PlayerController>(xrOriginForPC);
            log.AppendLine($"  ✓ PlayerController añadido a '{xrOriginForPC.name}'");
            changed = true;
        }
        else
        {
            log.AppendLine($"[PlayerController en '{pc.gameObject.name}']");
        }

        Undo.RecordObject(pc, "Setup VR Explorador — PlayerController");

        // useKatVR: NO tocar. Lo gestiona Tools → TITA → Explorador → Activar/Desactivar KAT VR.
        // Antes esto lo forzaba a false y apagaba la caminadora cada vez que se corría el setup.
        log.AppendLine($"  — useKatVR preservado ({pc.useKatVR})");

        // Snap turn desactivado (joystick derecho solo para interacción)
        if (pc.enableSnapTurn)
        {
            pc.enableSnapTurn = false;
            log.AppendLine("  ✓ enableSnapTurn = false");
            changed = true;
        }
        else log.AppendLine("  — enableSnapTurn ya era false");

        // xrRig:
        //   · Si PlayerController está EN el XR Origin → xrRig = pc.transform (mismo objeto)
        //   · Si no → busca como hijo, luego busca en toda la escena
        if (pc.xrRig == null)
        {
            Transform xrRigTransform = null;

            // Caso 1: PlayerController montado directamente en el XR Origin
            if (pc.gameObject.name.Contains("XR Origin") || pc.gameObject.name.Contains("XR Rig"))
                xrRigTransform = pc.transform;

            // Caso 2: XR Origin como hijo del GO del PlayerController
            if (xrRigTransform == null)
            {
                var child = FindChild(pc.gameObject, "XR Origin (XR Rig)")
                         ?? FindChild(pc.gameObject, "XR_Origin_VR")
                         ?? FindChild(pc.gameObject, "XR Origin");
                if (child != null) xrRigTransform = child.transform;
            }

            // Caso 3: Buscar XR Origin en toda la escena
            if (xrRigTransform == null)
            {
                var sceneXR = FindXROriginInScene();
                if (sceneXR != null) xrRigTransform = sceneXR.transform;
            }

            if (xrRigTransform != null)
            {
                pc.xrRig = xrRigTransform;
                log.AppendLine($"  ✓ xrRig → {xrRigTransform.name}");
                changed = true;
            }
            else log.AppendLine("  ✗ xrRig: no se encontró XR Origin en la escena");
        }
        else log.AppendLine($"  — xrRig ya asignado ({pc.xrRig.name})");

        // headCamera → busca dentro del xrRig si está asignado, si no en el GO del PC
        if (pc.headCamera == null)
        {
            var searchRoot = pc.xrRig != null ? pc.xrRig.gameObject : pc.gameObject;
            var camGO = FindChildWithTag(searchRoot, "MainCamera")
                     ?? FindChild(searchRoot, "Main Camera")
                     ?? FindChild(searchRoot, "Main_Camera")
                     ?? FindChild(searchRoot, "Camera");
            if (camGO != null)
            {
                var cam = camGO.GetComponent<Camera>();
                if (cam != null)
                {
                    pc.headCamera = cam;
                    log.AppendLine($"  ✓ headCamera → {camGO.name}");
                    changed = true;
                }
            }
            else log.AppendLine("  ✗ headCamera: no se encontró cámara con tag MainCamera");
        }
        else log.AppendLine($"  — headCamera ya asignado ({pc.headCamera.name})");

        // interaction
        if (pc.interaction == null)
        {
            var inter = pc.GetComponent<PlayerInteraction>();
            if (inter != null)
            {
                pc.interaction = inter;
                log.AppendLine("  ✓ interaction → PlayerInteraction");
                changed = true;
            }
        }

        // moveAction — busca Explorer/Move en cualquier InputActionAsset del proyecto
        if (pc.moveAction == null || pc.moveAction.action == null)
        {
            var action = FindInputAction("Explorer/Move")
                      ?? FindInputAction("XRI Left Locomotion/Move");
            if (action != null)
            {
                pc.moveAction = InputActionReference.Create(action);
                log.AppendLine($"  ✓ moveAction → {action.actionMap.name}/{action.name}");
                changed = true;
            }
            else
            {
                log.AppendLine("  ✗ moveAction: no se encontró 'Explorer/Move' ni 'XRI Left Locomotion/Move'.");
                log.AppendLine("    Asígnalo manualmente desde InputSystem_Actions o XRI Default Input Actions.");
            }
        }
        else log.AppendLine($"  — moveAction ya asignado ({pc.moveAction.name})");

        EditorUtility.SetDirty(pc);

        // ── 1b. Desactivar providers de locomoción de XRI (conflictan con PlayerController) ──
        log.AppendLine("\n[Locomoción XRI — desactivar conflictos]");
        int disabledProviders = 0;
        foreach (var provider in Object.FindObjectsByType<ContinuousMoveProvider>(FindObjectsInactive.Include))
        {
            if (provider.enabled)
            {
                Undo.RecordObject(provider, "Setup VR — deshabilitar ContinuousMoveProvider");
                provider.enabled = false;
                EditorUtility.SetDirty(provider);
                log.AppendLine($"  ✓ ContinuousMoveProvider desactivado ({provider.gameObject.name})");
                disabledProviders++;
                changed = true;
            }
        }
        foreach (var provider in Object.FindObjectsByType<ContinuousTurnProvider>(FindObjectsInactive.Include))
        {
            if (provider.enabled)
            {
                Undo.RecordObject(provider, "Setup VR — deshabilitar ContinuousTurnProvider");
                provider.enabled = false;
                EditorUtility.SetDirty(provider);
                log.AppendLine($"  ✓ ContinuousTurnProvider desactivado ({provider.gameObject.name})");
                disabledProviders++;
                changed = true;
            }
        }
        foreach (var provider in Object.FindObjectsByType<SnapTurnProvider>(FindObjectsInactive.Include))
        {
            if (provider.enabled)
            {
                Undo.RecordObject(provider, "Setup VR — deshabilitar SnapTurnProvider");
                provider.enabled = false;
                EditorUtility.SetDirty(provider);
                log.AppendLine($"  ✓ SnapTurnProvider desactivado ({provider.gameObject.name})");
                disabledProviders++;
                changed = true;
            }
        }
        if (disabledProviders == 0)
            log.AppendLine("  — No se encontraron providers de rotación XRI activos (ok)");

        // ── 2. ExplorerAvatar ────────────────────────────────────────────────
        var avatar = pc.GetComponent<ExplorerAvatar>();
        if (avatar != null)
        {
            Undo.RecordObject(avatar, "Setup VR Explorador — ExplorerAvatar");

            if (avatar.xrCamera == null && pc.headCamera != null)
            {
                avatar.xrCamera = pc.headCamera.transform;
                log.AppendLine($"  ✓ ExplorerAvatar.xrCamera → {pc.headCamera.name}");
                changed = true;
            }
            else if (avatar.xrCamera != null)
                log.AppendLine($"  — xrCamera ya asignado ({avatar.xrCamera.name})");

            if (avatar.avatarRoot == null)
            {
                var kyle = FindChild(pc.gameObject, "RobotKyle_Explorer")
                        ?? FindChild(pc.gameObject, "RobotKyle");
                if (kyle != null)
                {
                    avatar.avatarRoot = kyle.transform;
                    log.AppendLine($"  ✓ avatarRoot → {kyle.name}");
                    changed = true;
                }
                else log.AppendLine("  ✗ avatarRoot: no se encontró RobotKyle_Explorer");
            }
            else log.AppendLine($"  — avatarRoot ya asignado ({avatar.avatarRoot.name})");

            if (avatar.avatarAnimator == null && avatar.avatarRoot != null)
            {
                var anim = avatar.avatarRoot.GetComponentInChildren<Animator>();
                if (anim != null)
                {
                    avatar.avatarAnimator = anim;
                    log.AppendLine($"  ✓ avatarAnimator → {anim.gameObject.name}");
                    changed = true;
                }
            }

            EditorUtility.SetDirty(avatar);
        }
        else log.AppendLine("  — ExplorerAvatar: no encontrado en Explorer_Player");

        // ── 3. Manos VR (TrackedPoseDriver + HandModelController + XRDirectInteractor) ─
        log.AppendLine("\n[Manos]");
        changed |= SetupHand(FindHandGO("Left"),
            "<XRController>{LeftHand}/devicePosition",
            "<XRController>{LeftHand}/deviceRotation",
            HandModelController.HandSide.Left, log);
        changed |= SetupHand(FindHandGO("Right"),
            "<XRController>{RightHand}/devicePosition",
            "<XRController>{RightHand}/deviceRotation",
            HandModelController.HandSide.Right, log);

        // ── 4. RuntimeHandConsolidator ───────────────────────────────────────
        log.AppendLine("\n[Extras]");
        var xrOriginGO = pc.xrRig != null ? pc.xrRig.gameObject : null;
        if (xrOriginGO != null)
        {
            if (xrOriginGO.GetComponent<RuntimeHandConsolidator>() == null)
            {
                Undo.AddComponent<RuntimeHandConsolidator>(xrOriginGO);
                log.AppendLine($"  ✓ RuntimeHandConsolidator añadido a '{xrOriginGO.name}'");
                changed = true;
            }
            else log.AppendLine($"  — RuntimeHandConsolidator ya presente en '{xrOriginGO.name}'");
        }

        // ── 5. ConnectionManager — modoOffline ───────────────────────────────
        var connMgr = Object.FindAnyObjectByType<ConnectionManager>();
        if (connMgr != null)
        {
            if (!connMgr.modoOffline)
            {
                Undo.RecordObject(connMgr, "Setup VR — modoOffline");
                connMgr.modoOffline = true;
                EditorUtility.SetDirty(connMgr);
                log.AppendLine("  ✓ ConnectionManager.modoOffline = true");
                changed = true;
            }
            else log.AppendLine("  — modoOffline ya era true");
        }
        else log.AppendLine("  — ConnectionManager no encontrado");

        // ── 6. XRRenderGuard en Main_Camera ─────────────────────────────────
        if (pc.headCamera != null)
        {
            var camGO = pc.headCamera.gameObject;
            if (camGO.GetComponent<XRRenderGuard>() == null)
            {
                Undo.AddComponent<XRRenderGuard>(camGO);
                log.AppendLine($"  ✓ XRRenderGuard añadido a '{camGO.name}'");
                changed = true;
            }
            else log.AppendLine($"  — XRRenderGuard ya presente en '{camGO.name}'");
        }

        // ── 7. Desactivar cámaras sin TrackedPoseDriver ──────────────────────
        int disabledCams = 0;
        foreach (var cam in Object.FindObjectsByType<Camera>())
        {
            if (cam.CompareTag("MainCamera")) continue;
            if (cam.GetComponent<TrackedPoseDriver>() != null) continue;
            if (!cam.enabled || !cam.gameObject.activeInHierarchy) continue;

            Undo.RecordObject(cam, "Setup VR — desactivar cámara");
            cam.enabled = false;
            EditorUtility.SetDirty(cam);
            disabledCams++;
            changed = true;
        }
        if (disabledCams > 0)
            log.AppendLine($"  ✓ {disabledCams} cámara(s) no-VR desactivada(s)");

        // ── Resultado ────────────────────────────────────────────────────────
        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        Debug.Log("[ExplorerVRSetupTool]\n" + log);
        EditorUtility.DisplayDialog(
            changed ? "Setup VR Explorador — Cambios aplicados" : "Setup VR Explorador — Sin cambios",
            log.ToString() + (changed ? "\n\nGuarda la escena con Ctrl+S." : ""),
            "OK");
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Fix Manos VR (Modo Controlador)
    // ────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/TITA/Fix Manos VR (Modo Controlador)")]
    static void FixHandsControllerMode()
    {
        const string leftPath  = "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/Controllers/XR Controller Left.prefab";
        const string rightPath = "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/Controllers/XR Controller Right.prefab";

        var leftPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>(leftPath);
        var rightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(rightPath);

        if (leftPrefab == null || rightPrefab == null)
        {
            EditorUtility.DisplayDialog("Fix Manos VR",
                "No se encontró el prefab de controlador en:\n" + leftPath +
                "\n\nVerifica que el sample 'Starter Assets' de XRI 3.4.1 está importado.", "OK");
            return;
        }

        var log = new StringBuilder();
        bool changed = false;

        // ── Eliminar LeftHandQuestVisual / RightHandQuestVisual ──────────────
        foreach (string goName in new[] { "LeftHandQuestVisual", "RightHandQuestVisual" })
        {
            var go = GameObject.Find(goName);
            if (go != null)
            {
                Undo.DestroyObjectImmediate(go);
                log.AppendLine($"  ✓ '{goName}' eliminado");
                changed = true;
            }
            else log.AppendLine($"  — '{goName}' no encontrado (ya eliminado)");
        }

        // ── Setup cada mano ──────────────────────────────────────────────────
        changed |= SetupControllerHand("Left",  leftPrefab,  log);
        changed |= SetupControllerHand("Right", rightPrefab, log);

        if (changed)
            EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[ExplorerVRSetupTool] Fix Manos\n" + log);
        EditorUtility.DisplayDialog(
            changed ? "Fix Manos VR — Cambios aplicados" : "Fix Manos VR — Sin cambios",
            log.ToString() + (changed ? "\n\nGuarda la escena con Ctrl+S." : ""),
            "OK");
    }

    static bool SetupControllerHand(string side, GameObject controllerPrefab, StringBuilder log)
    {
        var go = FindHandGO(side);
        if (go == null)
        {
            log.AppendLine($"\n  ✗ {side}Hand_Controller no encontrado en escena");
            return false;
        }

        bool changed = false;
        log.AppendLine($"\n[{side} Hand: {go.name}]");

        // Limpiar geometría procedural de HandModelController anterior
        var hmc = go.GetComponent<HandModelController>();
        if (hmc != null)
        {
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = go.transform.GetChild(i);
                if (child.name.StartsWith("Hand_"))
                {
                    Undo.DestroyObjectImmediate(child.gameObject);
                    changed = true;
                }
            }
            Undo.DestroyObjectImmediate(hmc);
            log.AppendLine("  ✓ HandModelController (procedural) eliminado");
            changed = true;
        }
        else log.AppendLine("  — HandModelController no presente");

        // TrackedPoseDriver
        string posPath = side == "Left"
            ? "<XRController>{LeftHand}/devicePosition"
            : "<XRController>{RightHand}/devicePosition";
        string rotPath = side == "Left"
            ? "<XRController>{LeftHand}/deviceRotation"
            : "<XRController>{RightHand}/deviceRotation";

        if (go.GetComponent<TrackedPoseDriver>() == null)
        {
            var tpd = Undo.AddComponent<TrackedPoseDriver>(go);
            ConfigureTPD(tpd, posPath, rotPath);
            log.AppendLine("  ✓ TrackedPoseDriver añadido");
            changed = true;
        }
        else log.AppendLine("  — TrackedPoseDriver ya presente");

        // XRDirectInteractor + SphereCollider
        if (go.GetComponent<XRDirectInteractor>() == null)
        {
            if (go.GetComponent<SphereCollider>() == null)
            {
                var sc = Undo.AddComponent<SphereCollider>(go);
                sc.isTrigger = true;
                sc.radius    = 0.05f;
            }
            Undo.AddComponent<XRDirectInteractor>(go);
            log.AppendLine("  ✓ XRDirectInteractor añadido");
            changed = true;
        }
        else log.AppendLine("  — XRDirectInteractor ya presente");

        // Visual: XR Controller Left/Right.prefab como hijo
        string visualName = controllerPrefab.name;
        if (go.transform.Find(visualName) == null)
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(controllerPrefab, go.transform);
            visual.name = visualName;
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            Undo.RegisterCreatedObjectUndo(visual, $"Fix Manos — {visualName}");
            log.AppendLine($"  ✓ '{visualName}' instanciado como hijo visual");
            changed = true;
        }
        else log.AppendLine($"  — '{visualName}' ya presente como hijo");

        EditorUtility.SetDirty(go);
        return changed;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Manos (Setup completo — llamado desde Run())
    // ────────────────────────────────────────────────────────────────────────

    static GameObject FindHandGO(string side)
    {
        string[] candidates = {
            $"{side}Hand_Controller",
            $"{side}Hand Controller",
            $"{side}Hand",
            $"{side} Controller",
            $"XRController ({side})",
        };
        foreach (var n in candidates)
        {
            var go = GameObject.Find(n);
            if (go != null) return go;
        }
        Debug.LogWarning($"[ExplorerVRSetupTool] Mano '{side}' no encontrada.");
        return null;
    }

    static bool SetupHand(GameObject go, string posPath, string rotPath,
                           HandModelController.HandSide side, StringBuilder log)
    {
        if (go == null) return false;
        bool changed = false;

        // TrackedPoseDriver
        var tpd = go.GetComponent<TrackedPoseDriver>();
        if (tpd == null)
        {
            tpd = Undo.AddComponent<TrackedPoseDriver>(go);
            ConfigureTPD(tpd, posPath, rotPath);
            log.AppendLine($"  ✓ TrackedPoseDriver añadido a '{go.name}'");
            changed = true;
        }
        else log.AppendLine($"  — TrackedPoseDriver ya presente en '{go.name}'");

        // HandModelController
        var hmc = go.GetComponent<HandModelController>();
        if (hmc == null)
        {
            hmc      = Undo.AddComponent<HandModelController>(go);
            hmc.hand = side;
            log.AppendLine($"  ✓ HandModelController añadido a '{go.name}' ({side})");
            changed = true;
        }
        else log.AppendLine($"  — HandModelController ya presente en '{go.name}'");

        // XRDirectInteractor + SphereCollider
        var interactor = go.GetComponent<XRDirectInteractor>();
        if (interactor == null)
        {
            var sc = go.GetComponent<SphereCollider>();
            if (sc == null)
            {
                sc           = Undo.AddComponent<SphereCollider>(go);
                sc.isTrigger = true;
                sc.radius    = 0.05f;
            }
            Undo.AddComponent<XRDirectInteractor>(go);
            log.AppendLine($"  ✓ XRDirectInteractor añadido a '{go.name}'");
            changed = true;
        }
        else log.AppendLine($"  — XRDirectInteractor ya presente en '{go.name}'");

        return changed;
    }

    static void ConfigureTPD(TrackedPoseDriver tpd, string posPath, string rotPath)
    {
        bool isLeft = posPath.Contains("LeftHand");

        var posAction = new InputAction("Position", InputActionType.Value, posPath,
                                        expectedControlType: "Vector3");
        posAction.AddBinding(isLeft ? "<XRHMD>/leftEyePosition" : "<XRHMD>/rightEyePosition");

        var rotAction = new InputAction("Rotation", InputActionType.Value, rotPath,
                                        expectedControlType: "Quaternion");
        rotAction.AddBinding(isLeft ? "<XRHMD>/leftEyeRotation" : "<XRHMD>/rightEyeRotation");

        tpd.positionInput = new InputActionProperty(posAction);
        tpd.rotationInput = new InputActionProperty(rotAction);
        tpd.trackingType  = TrackedPoseDriver.TrackingType.RotationAndPosition;
        tpd.updateType    = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Fix Locomotion XR Origin
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deshabilita los providers de locomoción del XR Origin (XR Rig) en la escena
    /// para que solo PlayerController maneje movimiento y snap turn.
    ///
    /// Problema: XR Origin (XR Rig) de Starter Assets incluye DynamicMoveProvider,
    /// SnapTurnProvider y ContinuousTurnProvider que leen el thumbstick en paralelo
    /// a PlayerController, causando que el movimiento se cancele o sea errático.
    /// </summary>
    [MenuItem("Tools/TITA/Fix Locomotion XR Origin")]
    static void FixLocomotionXROrigin()
    {
        var log = new StringBuilder();
        bool changed = false;

        // Deshabilitar DynamicMoveProvider / ContinuousMoveProvider
        foreach (var mp in Object.FindObjectsByType<ContinuousMoveProvider>(FindObjectsInactive.Include))
        {
            if (mp.enabled)
            {
                Undo.RecordObject(mp, "Fix Locomotion — ContinuousMoveProvider");
                mp.enabled = false;
                EditorUtility.SetDirty(mp);
                log.AppendLine($"  ✓ ContinuousMoveProvider deshabilitado en '{mp.gameObject.name}'");
                changed = true;
            }
            else log.AppendLine($"  — ContinuousMoveProvider ya deshabilitado en '{mp.gameObject.name}'");
        }

        // Deshabilitar SnapTurnProvider
        foreach (var stp in Object.FindObjectsByType<SnapTurnProvider>(FindObjectsInactive.Include))
        {
            if (stp.enabled)
            {
                Undo.RecordObject(stp, "Fix Locomotion — SnapTurnProvider");
                stp.enabled = false;
                EditorUtility.SetDirty(stp);
                log.AppendLine($"  ✓ SnapTurnProvider deshabilitado en '{stp.gameObject.name}'");
                changed = true;
            }
            else log.AppendLine($"  — SnapTurnProvider ya deshabilitado en '{stp.gameObject.name}'");
        }

        // Deshabilitar ContinuousTurnProvider
        foreach (var ctp in Object.FindObjectsByType<ContinuousTurnProvider>(FindObjectsInactive.Include))
        {
            if (ctp.enabled)
            {
                Undo.RecordObject(ctp, "Fix Locomotion — ContinuousTurnProvider");
                ctp.enabled = false;
                EditorUtility.SetDirty(ctp);
                log.AppendLine($"  ✓ ContinuousTurnProvider deshabilitado en '{ctp.gameObject.name}'");
                changed = true;
            }
            else log.AppendLine($"  — ContinuousTurnProvider ya deshabilitado en '{ctp.gameObject.name}'");
        }

        if (log.Length == 0)
            log.AppendLine("  — No se encontraron providers de locomoción en la escena.");

        if (changed)
            EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[ExplorerVRSetupTool] Fix Locomotion\n" + log);
        EditorUtility.DisplayDialog(
            changed ? "Fix Locomotion — Cambios aplicados" : "Fix Locomotion — Sin cambios",
            log.ToString() + (changed ? "\n\nGuarda la escena con Ctrl+S." : ""),
            "OK");
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Fix Hands Mode (XRInputModalityManager)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Añade XRInputModalityManager al XR Origin y conecta los GameObjects de
    /// controlador y mano para cada mano. El manager alterna automáticamente en
    /// runtime entre el visual del controlador Quest y el visual de mano rastreada.
    ///
    /// Requiere haber ejecutado "Actualizar Prefab Explorer_Player" primero para
    /// que los hijos LeftHandQuestVisual / RightHandQuestVisual existan.
    /// </summary>
    [MenuItem("Tools/TITA/Fix Hands Mode (Manos ↔ Controles)")]
    static void FixHandsMode()
    {
        var log = new StringBuilder();
        bool changed = false;

        var pc = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (pc == null || pc.xrRig == null)
        {
            EditorUtility.DisplayDialog("Fix Hands Mode",
                "No se encontró PlayerController con xrRig asignado.\n" +
                "Ejecuta 'Setup Completo VR Explorador' primero.", "OK");
            return;
        }
        var xrOriginGO = pc.xrRig.gameObject;

        // Cargar XRInputModalityManager por GUID (evita dependencia de assembly)
        const string k_ModalityGuid = "82bc72d2ecc8add47b2fe00d40318500";
        var modalityScript = AssetDatabase.LoadAssetAtPath<MonoScript>(
            AssetDatabase.GUIDToAssetPath(k_ModalityGuid));

        if (modalityScript == null)
        {
            EditorUtility.DisplayDialog("Fix Hands Mode",
                "XRInputModalityManager no encontrado.\n" +
                "Verifica que XR Interaction Toolkit 3.4.1 está instalado.", "OK");
            return;
        }

        var modalityType = modalityScript.GetClass();

        var modality = xrOriginGO.GetComponent(modalityType);
        if (modality == null)
        {
            modality = Undo.AddComponent(xrOriginGO, modalityType);
            log.AppendLine($"  ✓ XRInputModalityManager añadido a '{xrOriginGO.name}'");
            changed = true;
        }
        else log.AppendLine($"  — XRInputModalityManager ya presente en '{xrOriginGO.name}'");

        // Conectar los GOs de mano izquierda y derecha
        changed |= WireHandModality(modality, "Left",
            FindHandGO("Left"), "XR Controller Left", "LeftHandQuestVisual", log);
        changed |= WireHandModality(modality, "Right",
            FindHandGO("Right"), "XR Controller Right", "RightHandQuestVisual", log);

        if (changed)
            EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[ExplorerVRSetupTool] Fix Hands Mode\n" + log);
        EditorUtility.DisplayDialog(
            changed ? "Fix Hands Mode — Cambios aplicados" : "Fix Hands Mode — Sin cambios",
            log.ToString() + (changed ? "\n\nGuarda la escena con Ctrl+S." : ""),
            "OK");
    }

    static bool WireHandModality(Component modality, string side,
                                  GameObject handControllerGO,
                                  string controllerChildName, string handVisualChildName,
                                  StringBuilder log)
    {
        if (handControllerGO == null)
        {
            log.AppendLine($"\n  ✗ {side} hand controller no encontrado");
            return false;
        }

        bool changed = false;
        log.AppendLine($"\n[{side} hand: {handControllerGO.name}]");

        var controllerChild  = handControllerGO.transform.Find(controllerChildName);
        var handVisualChild  = handControllerGO.transform.Find(handVisualChildName);

        if (controllerChild == null)
            log.AppendLine($"  ✗ '{controllerChildName}' no encontrado — ejecuta 'Actualizar Prefab' primero");
        if (handVisualChild == null)
            log.AppendLine($"  ✗ '{handVisualChildName}' no encontrado — ejecuta 'Actualizar Prefab' primero");

        if (controllerChild == null && handVisualChild == null) return false;

        var so = new SerializedObject(modality);
        so.Update();

        string controllerProp = side == "Left" ? "m_LeftController" : "m_RightController";
        string handProp       = side == "Left" ? "m_LeftHand"       : "m_RightHand";

        if (controllerChild != null)
        {
            var prop = so.FindProperty(controllerProp);
            if (prop != null && prop.objectReferenceValue != controllerChild.gameObject)
            {
                prop.objectReferenceValue = controllerChild.gameObject;
                log.AppendLine($"  ✓ {controllerProp} → '{controllerChildName}'");
                changed = true;
            }
            else if (prop != null) log.AppendLine($"  — {controllerProp} ya asignado");
        }

        if (handVisualChild != null)
        {
            var prop = so.FindProperty(handProp);
            if (prop != null && prop.objectReferenceValue != handVisualChild.gameObject)
            {
                prop.objectReferenceValue = handVisualChild.gameObject;
                log.AppendLine($"  ✓ {handProp} → '{handVisualChildName}'");
                changed = true;
            }
            else if (prop != null) log.AppendLine($"  — {handProp} ya asignado");
        }

        if (changed) so.ApplyModifiedProperties();
        return changed;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Fix Camera Rotation XR Origin
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Añade InputActionManager al XR Origin para que las acciones de entrada
    /// del HMD (centerEyePosition / centerEyeRotation) se activen en runtime.
    ///
    /// Sin este componente, el TrackedPoseDriver de la cámara nunca recibe datos
    /// de rotación del HMD, por lo que la cabeza no gira.
    ///
    /// Basado en el setup del proyecto de referencia Titulacion:
    /// Complete XR Origin Set Up Variant.prefab tiene InputActionManager con
    /// XRI Default Input Actions asignado.
    /// </summary>
    [MenuItem("Tools/TITA/Fix Camera Rotation XR Origin")]
    static void FixCameraRotation()
    {
        var log = new StringBuilder();
        bool changed = false;

        // ── 1. Localizar XRI Default Input Actions ───────────────────────────
        InputActionAsset actionsAsset = null;
        var assetPath = AssetDatabase.GUIDToAssetPath("c348712bda248c246b8c49b3db54643f");
        if (!string.IsNullOrEmpty(assetPath))
            actionsAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);

        if (actionsAsset == null)
        {
            foreach (var g in AssetDatabase.FindAssets("XRI Default Input Actions t:InputActionAsset"))
            {
                actionsAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                    AssetDatabase.GUIDToAssetPath(g));
                if (actionsAsset != null) break;
            }
        }

        if (actionsAsset == null)
        {
            EditorUtility.DisplayDialog("Fix Camera Rotation",
                "No se encontró 'XRI Default Input Actions'.\n\n" +
                "Verifica que el sample 'Starter Assets' de XRI 3.4.1 está importado en:\n" +
                "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/", "OK");
            return;
        }

        log.AppendLine($"Input Actions: {actionsAsset.name}");

        // ── 2. XR Origin via PlayerController.xrRig ──────────────────────────
        // Evita depender de Unity.XR.CoreUtils (puede no resolverse en Editor assembly)
        var pc = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (pc == null)
        {
            EditorUtility.DisplayDialog("Fix Camera Rotation",
                "No se encontró PlayerController en la escena.\n" +
                "Asegúrate de que Explorer_Player esté en la escena Explorador.", "OK");
            return;
        }

        if (pc.xrRig == null)
        {
            EditorUtility.DisplayDialog("Fix Camera Rotation",
                "PlayerController.xrRig no está asignado.\n" +
                "Ejecuta primero 'Setup Completo VR Explorador'.", "OK");
            return;
        }

        var xrOriginGO = pc.xrRig.gameObject;
        log.AppendLine($"\n[XR Origin: {xrOriginGO.name}]");

        // ── 3. InputActionManager en la raíz del XR Origin ───────────────────
        // Cargamos el tipo por GUID para evitar dependencia de assembly
        // (InputActionManager está en Unity.InputSystem, no referenciado en Editor por defecto)
        const string k_IAMGuid = "017c5e3933235514c9520e1dace2a4b2";
        var iamScript = AssetDatabase.LoadAssetAtPath<MonoScript>(
            AssetDatabase.GUIDToAssetPath(k_IAMGuid));

        if (iamScript == null)
        {
            log.AppendLine("  ✗ InputActionManager no encontrado (GUID: " + k_IAMGuid + ")");
            log.AppendLine("    Añádelo manualmente al XR Origin desde Add Component.");
        }
        else
        {
            var iamType = iamScript.GetClass();
            var iam = xrOriginGO.GetComponent(iamType);
            if (iam == null)
            {
                iam = Undo.AddComponent(xrOriginGO, iamType);
                log.AppendLine("  ✓ InputActionManager añadido");
                changed = true;
            }
            else log.AppendLine("  — InputActionManager ya presente");

            // Asignar actionsAsset via SerializedObject (m_ActionAssets es el campo serializado)
            var so = new SerializedObject(iam);
            so.Update();
            var assetsProp = so.FindProperty("m_ActionAssets");
            if (assetsProp != null)
            {
                bool alreadyIn = false;
                for (int i = 0; i < assetsProp.arraySize; i++)
                {
                    if (assetsProp.GetArrayElementAtIndex(i).objectReferenceValue == actionsAsset)
                    { alreadyIn = true; break; }
                }
                if (!alreadyIn)
                {
                    assetsProp.arraySize++;
                    assetsProp.GetArrayElementAtIndex(assetsProp.arraySize - 1)
                              .objectReferenceValue = actionsAsset;
                    so.ApplyModifiedProperties();
                    log.AppendLine($"  ✓ '{actionsAsset.name}' añadido a actionAssets");
                    changed = true;
                }
                else log.AppendLine($"  — '{actionsAsset.name}' ya en actionAssets");
            }
            else log.AppendLine("  ✗ Propiedad 'm_ActionAssets' no encontrada — asigna manualmente");
        }

        // ── 4. TrackedPoseDriver en la cámara del XR Origin ──────────────────
        // Usa headCamera si ya está asignado, si no busca por tag/nombre
        GameObject camGO = pc.headCamera != null ? pc.headCamera.gameObject : null;
        if (camGO == null || !IsChildOf(camGO.transform, xrOriginGO.transform))
            camGO = FindChildWithTag(xrOriginGO, "MainCamera")
                 ?? FindChild(xrOriginGO, "Main Camera")
                 ?? FindChild(xrOriginGO, "Camera");

        if (camGO != null)
        {
            log.AppendLine($"\n[Camera: {camGO.name}]");

            var tpd = camGO.GetComponent<TrackedPoseDriver>();
            if (tpd == null)
            {
                tpd = Undo.AddComponent<TrackedPoseDriver>(camGO);
                ConfigureCameraTPD(tpd);
                log.AppendLine("  ✓ TrackedPoseDriver añadido (HMD centerEye)");
                changed = true;
            }
            else
            {
                log.AppendLine("  — TrackedPoseDriver ya presente");
                bool missingPos = tpd.positionInput.action == null
                               || string.IsNullOrEmpty(tpd.positionInput.action.bindings.Count > 0
                                   ? tpd.positionInput.action.bindings[0].path : "");
                bool missingRot = tpd.rotationInput.action == null
                               || string.IsNullOrEmpty(tpd.rotationInput.action.bindings.Count > 0
                                   ? tpd.rotationInput.action.bindings[0].path : "");

                if (missingPos || missingRot)
                {
                    Undo.RecordObject(tpd, "Fix Camera Rotation — TPD");
                    ConfigureCameraTPD(tpd);
                    EditorUtility.SetDirty(tpd);
                    log.AppendLine("  ✓ TrackedPoseDriver: bindings HMD reconfigurados");
                    changed = true;
                }
                else log.AppendLine("  — TrackedPoseDriver: bindings ya configurados");
            }

            // La Camera del XR Origin debe estar habilitada
            var cam = camGO.GetComponent<Camera>();
            if (cam != null && !cam.enabled)
            {
                Undo.RecordObject(cam, "Fix Camera Rotation — habilitar Camera XR");
                cam.enabled = true;
                EditorUtility.SetDirty(cam);
                log.AppendLine("  ✓ Camera habilitada en XR Origin");
                changed = true;
            }
            else if (cam != null) log.AppendLine("  — Camera ya habilitada en XR Origin");
        }
        else log.AppendLine("\n  ✗ No se encontró la cámara en el XR Origin");

        // ── Resultado ────────────────────────────────────────────────────────
        if (changed)
            EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[ExplorerVRSetupTool] Fix Camera Rotation\n" + log);
        EditorUtility.DisplayDialog(
            changed ? "Fix Camera Rotation — Cambios aplicados" : "Fix Camera Rotation — Sin cambios",
            log.ToString() + (changed ? "\n\nGuarda la escena con Ctrl+S." : ""),
            "OK");
    }

    static bool IsChildOf(Transform child, Transform parent)
    {
        var t = child.parent;
        while (t != null)
        {
            if (t == parent) return true;
            t = t.parent;
        }
        return false;
    }

    static void ConfigureCameraTPD(TrackedPoseDriver tpd)
    {
        var posAction = new InputAction("Position", InputActionType.Value,
            "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
        var rotAction = new InputAction("Rotation", InputActionType.Value,
            "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");

        tpd.positionInput = new InputActionProperty(posAction);
        tpd.rotationInput = new InputActionProperty(rotAction);
        tpd.trackingType  = TrackedPoseDriver.TrackingType.RotationAndPosition;
        tpd.updateType    = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────────────────

    static InputAction FindInputAction(string path)
    {
        var guids = AssetDatabase.FindAssets("t:InputActionAsset");
        foreach (var guid in guids)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var asset     = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);
            if (asset == null) continue;

            var action = asset.FindAction(path);
            if (action != null) return action;
        }
        return null;
    }

    static GameObject FindChild(GameObject root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t.gameObject;
        return null;
    }

    static GameObject FindChildWithTag(GameObject root, string tag)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            try { if (t.CompareTag(tag)) return t.gameObject; }
            catch { /* tag no existe */ }
        }
        return null;
    }

    // Busca el XR Origin en la escena activa por nombre (activos e inactivos)
    static GameObject FindXROriginInScene()
    {
        string[] names = { "XR Origin (XR Rig)", "XR_Origin_VR", "XR Origin" };
        foreach (var n in names)
        {
            var go = GameObject.Find(n);
            if (go != null) return go;
        }
        // Fallback: incluir inactivos
        foreach (var go in Object.FindObjectsByType<GameObject>(
                     FindObjectsInactive.Include))
        {
            if (go.name.Contains("XR Origin") || go.name == "XR_Origin_VR")
                return go;
        }
        return null;
    }
}

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Pedido explícito del usuario (2026-07-24, "Panel de Medición"): convertir el multímetro
/// portátil en un panel de medición FIJO en la pared, uno por Reto_Zone — elimina de raíz los
/// bugs de agarre del cuerpo (tamaño de collider, trackRotation, duelo de foco con Mode_Button)
/// porque el cuerpo deja de ser un XRGrabInteractable.
///
/// Los 4 Reto_Zone están a 35-90 unidades de distancia entre sí en Explorador.unity — un solo
/// panel no puede llegar con las puntas a las 4 zonas, así que se decidió (AskUserQuestion,
/// misma sesión): un panel por reto, hijo de su Reto_Zone → se activa/desactiva junto con la
/// zona vía GameManager.LoadLevel(), sin código extra (FindAnyObjectByType&lt;Multimeter&gt;()
/// ya excluye inactivos en todos los consumidores existentes).
///
/// Pasos:
///   1. Copiar Multimeter_VR_Art.prefab → Multimeter_Panel_Art.prefab (copia de archivo, preserva
///      TODAS las referencias internas: TMP texts, indicadores, MultimeterCable.anchorPoint).
///   2. Limpiar el panel: cuerpo pierde XRGrabInteractable + Rigidbody (BoxCollider queda sólido,
///      no-trigger); Socket_VmA/Socket_COM eliminados; Jack_Nub_Red/Black pierden Rigidbody +
///      Collider + XRGrabInteractable (quedan fijos, "soldados" al panel — MultimeterCable ya
///      maneja el caso 'anchorPoint sin Rigidbody en el padre' conectando el resorte al mundo,
///      no hace falta tocar ese script).
///   3. Desactivar (NO borrar — "no pierdes nada de lo que has hecho") la única instancia vieja
///      de Multimeter_VR_Art en la escena.
///   4. Instanciar 4 copias del panel nuevo, cada una hija de su GameManager.retoXZone, en una
///      posición PLACEHOLDER (encima del centro de la zona) — hay que reubicar cada una a mano
///      contra la pared real de su sala, esto no puede adivinarse desde el YAML.
///
/// Menú: Tools → TITA → Multímetro → Convertir a Panel de Pared (4 instancias)
/// </summary>
public static class MultimeterPanelConversionTool
{
    const string SourcePrefab = "Assets/Prefabs/Multimeter_VR_Art.prefab";
    const string PanelPrefab  = "Assets/Prefabs/Multimeter_Panel_Art.prefab";
    const string ScenePath    = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Multímetro/Convertir a Panel de Pared (4 instancias)")]
    public static void Convert()
    {
        if (!BuildPanelPrefab())
        {
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        int placed = PlaceInScene();

        Debug.Log($"[MultimeterPanelConversionTool] ✓ {placed}/4 paneles instanciados (uno por Reto_Zone). " +
                  "PENDIENTE: reposicionar cada 'Multimeter_Panel_RetoX' a mano en el Inspector " +
                  "contra la pared real de su sala — se colocaron en una posición PLACEHOLDER " +
                  "(centro de la zona + 1.4 m de altura) porque no hay forma de saber desde acá " +
                  "dónde está la pared visualmente.");
        if (Application.isBatchMode) EditorApplication.Exit(placed == 4 ? 0 : 1);
    }

    // ─────────────────────────────────────────────
    //  1-2. Prefab del panel
    // ─────────────────────────────────────────────
    static bool BuildPanelPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefab) == null)
        {
            if (!AssetDatabase.CopyAsset(SourcePrefab, PanelPrefab))
            {
                Debug.LogError($"[MultimeterPanelConversionTool] No se pudo copiar {SourcePrefab} → {PanelPrefab}.");
                return false;
            }
            AssetDatabase.Refresh();
            Debug.Log($"[MultimeterPanelConversionTool] Prefab copiado: {PanelPrefab}");
        }
        else
        {
            Debug.Log($"[MultimeterPanelConversionTool] {PanelPrefab} ya existía — se reutiliza y re-limpia.");
        }

        var root = PrefabUtility.LoadPrefabContents(PanelPrefab);
        if (root == null)
        {
            Debug.LogError($"[MultimeterPanelConversionTool] No se pudo cargar {PanelPrefab}.");
            return false;
        }

        // Cuerpo: ya no se agarra — fuera XRGrabInteractable + Rigidbody, el BoxCollider queda sólido.
        var bodyGrab = root.GetComponent<XRGrabInteractable>();
        if (bodyGrab != null) Object.DestroyImmediate(bodyGrab, true);
        var bodyRb = root.GetComponent<Rigidbody>();
        if (bodyRb != null) Object.DestroyImmediate(bodyRb, true);
        var bodyCol = root.GetComponent<BoxCollider>();
        if (bodyCol != null) bodyCol.isTrigger = false;

        // Sockets: ya no hace falta enchufar nada, los jacks quedan fijos.
        int socketsRemoved = 0;
        foreach (var socketName in new[] { "Socket_VmA", "Socket_COM" })
        {
            var t = FindDeep(root.transform, socketName);
            if (t == null) continue;
            Object.DestroyImmediate(t.gameObject);
            socketsRemoved++;
        }

        // Jacks: fijos al panel — pierden física y grab, quedan como mesh decorativo.
        int jacksCleaned = 0;
        foreach (var jackName in new[] { "Jack_Nub_Red", "Jack_Nub_Black" })
        {
            var t = FindDeep(root.transform, jackName);
            if (t == null)
            {
                Debug.LogWarning($"[MultimeterPanelConversionTool] No se encontró '{jackName}' en el panel.");
                continue;
            }
            var grab = t.GetComponent<XRGrabInteractable>();
            if (grab != null) Object.DestroyImmediate(grab, true);
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null) Object.DestroyImmediate(rb, true);
            var col = t.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col, true);
            jacksCleaned++;
        }

        root.name = "Multimeter_Panel_Art";
        PrefabUtility.SaveAsPrefabAsset(root, PanelPrefab);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.Refresh();

        Debug.Log($"[MultimeterPanelConversionTool] Panel limpio: cuerpo sin grab/Rigidbody, " +
                  $"{socketsRemoved}/2 sockets eliminados, {jacksCleaned}/2 jacks fijados.");
        return socketsRemoved == 2 && jacksCleaned == 2;
    }

    // ─────────────────────────────────────────────
    //  3-4. Escena: desactivar la vieja, instanciar 4 nuevas
    // ─────────────────────────────────────────────
    static int PlaceInScene()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>();
        if (gm == null)
        {
            Debug.LogError("[MultimeterPanelConversionTool] No hay GameManager en la escena — no se puede ubicar por zona.");
            return 0;
        }

        // No borrar el trabajo previo — solo desactivar para que FindAnyObjectByType<Multimeter>()
        // (usado por NodeInteractable, GameManager, etc.) ya no la elija.
        var oldInstance = Object.FindAnyObjectByType<Multimeter>(FindObjectsInactive.Include);
        if (oldInstance != null && oldInstance.gameObject.activeSelf)
        {
            oldInstance.gameObject.SetActive(false);
            Debug.Log($"[MultimeterPanelConversionTool] Instancia portátil vieja '{oldInstance.name}' desactivada (no borrada).");
        }

        var panelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefab);
        if (panelAsset == null)
        {
            Debug.LogError($"[MultimeterPanelConversionTool] No se pudo cargar el asset {PanelPrefab}.");
            return 0;
        }

        var zonas = new (string label, GameObject zone)[]
        {
            ("Reto1", gm.reto1Zone),
            ("Reto2", gm.reto2Zone),
            ("Reto3", gm.reto3Zone),
            ("Reto4", gm.reto4Zone),
        };

        int placed = 0;
        foreach (var (label, zone) in zonas)
        {
            if (zone == null)
            {
                Debug.LogWarning($"[MultimeterPanelConversionTool] GameManager.{label.ToLowerInvariant()}Zone no está asignado — se omite.");
                continue;
            }

            var existing = zone.transform.Find($"Multimeter_Panel_{label}");
            if (existing != null)
            {
                Debug.Log($"[MultimeterPanelConversionTool] Ya existe 'Multimeter_Panel_{label}' en {label} — se omite.");
                placed++;
                continue;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(panelAsset);
            instance.name = $"Multimeter_Panel_{label}";

            // PLACEHOLDER: centro de la zona + altura de panel de pared típica. Reubicar a mano.
            Vector3 worldPos = zone.transform.position + Vector3.up * 1.4f;
            instance.transform.SetPositionAndRotation(worldPos, Quaternion.identity);
            instance.transform.SetParent(zone.transform, worldPositionStays: true);

            Undo.RegisterCreatedObjectUndo(instance, "Crear panel de multímetro");
            placed++;
            Debug.Log($"[MultimeterPanelConversionTool] '{instance.name}' creado como hijo de '{zone.name}' (posición PLACEHOLDER).");
        }

        var activeScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);

        return placed;
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
#endif

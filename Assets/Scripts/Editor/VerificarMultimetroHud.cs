#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verificación de solo lectura (no modifica nada) de MultimeterHudConversionTool, usando la API
/// real de Unity en vez de grep sobre el YAML — un componente heredado de un PrefabInstance (como
/// MultimeterModeButton en Mode_Button) no re-emite su guid de script en la escena, solo aparece
/// como override por propertyPath, así que grep del guid da falsos negativos.
///
/// Menú: Tools → TITA → Multímetro → Verificar HUD
/// </summary>
public static class VerificarMultimetroHud
{
    [MenuItem("Tools/TITA/Multímetro/Verificar HUD")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Explorador.unity", OpenSceneMode.Single);

        bool ok = true;

        var multimeters = Object.FindObjectsByType<Multimeter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int activeCount = 0;
        Debug.Log($"[VerificarHud] Multimeter en escena (incluye inactivos): {multimeters.Length}");
        foreach (var m in multimeters)
        {
            Debug.Log($"[VerificarHud]   '{GetPath(m.transform)}' activo={m.gameObject.activeInHierarchy}");
            if (m.gameObject.activeInHierarchy) activeCount++;
        }
        Debug.Log($"[VerificarHud] Multimeter ACTIVOS: {activeCount} (esperado: 1 — FindAnyObjectByType<Multimeter>() debe ser inambiguo)");
        if (activeCount != 1) ok = false;

        var ui = Object.FindAnyObjectByType<MultimeterUI>(FindObjectsInactive.Include);
        if (ui == null)
        {
            Debug.LogError("[VerificarHud] No hay MultimeterUI en la escena.");
            ok = false;
        }
        else
        {
            bool refsOk = ui.multimeter != null && ui.txtModo != null && ui.txtVoltaje != null &&
                          ui.txtCorriente != null && ui.txtEstado != null &&
                          ui.txtProbeRoja != null && ui.txtProbeNegra != null;
            Debug.Log($"[VerificarHud] MultimeterUI '{GetPath(ui.transform)}' activo={ui.gameObject.activeInHierarchy} " +
                      $"multimeter={(ui.multimeter != null ? "OK" : "NULL")} " +
                      $"txtModo={(ui.txtModo != null ? "OK" : "NULL")} txtVoltaje={(ui.txtVoltaje != null ? "OK" : "NULL")} " +
                      $"txtCorriente={(ui.txtCorriente != null ? "OK" : "NULL")} txtEstado={(ui.txtEstado != null ? "OK" : "NULL")} " +
                      $"txtProbeRoja={(ui.txtProbeRoja != null ? "OK" : "NULL")} txtProbeNegra={(ui.txtProbeNegra != null ? "OK" : "NULL")}");
            if (!refsOk || !ui.gameObject.activeInHierarchy) ok = false;
        }

        Debug.Log("[VerificarHud] --- Búsqueda global de todo lo llamado 'Mode_Button' ---");
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "Mode_Button")
                    Debug.Log($"[VerificarHud]   encontrado en '{GetPath(t)}' activoSelf={t.gameObject.activeSelf} activoEnJerarquia={t.gameObject.activeInHierarchy}");

        var gm = Object.FindAnyObjectByType<GameManager>();
        var zonas = new (string label, GameObject zone)[]
        {
            ("Reto1", gm.reto1Zone), ("Reto2", gm.reto2Zone),
            ("Reto3", gm.reto3Zone), ("Reto4", gm.reto4Zone),
        };
        var hudMultimeter = ui != null ? ui.multimeter : null;
        foreach (var (label, zone) in zonas)
        {
            var panelGO = FindInScene($"Multimeter_Panel_{label}");
            if (panelGO == null)
            {
                Debug.LogError($"[VerificarHud] '{label}': no se encontró 'Multimeter_Panel_{label}'.");
                ok = false;
                continue;
            }

            var btnT = panelGO.transform.Find("Mode_Button");
            if (btnT == null)
            {
                Debug.LogError($"[VerificarHud] '{label}': Mode_Button NO es hijo directo de Multimeter_Panel_{label}.");
                ok = false;
                continue;
            }

            int otherActiveSiblings = 0;
            foreach (Transform child in panelGO.transform)
                if (child != btnT && child.gameObject.activeSelf) otherActiveSiblings++;
            var leftoverComponents = panelGO.GetComponents<Component>().Length; // esperado: 1 (solo Transform)

            var mb = btnT.GetComponent<MultimeterModeButton>();
            var xrsi = btnT.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            var col = btnT.GetComponent<Collider>();
            bool wired = mb != null && mb.multimeter != null && hudMultimeter != null && mb.multimeter == hudMultimeter;
            if (mb != null)
                Debug.Log($"[VerificarHud]     diag '{label}': mb.multimeter={(mb.multimeter == null ? "NULL" : GetPath(mb.multimeter.transform) + " id=" + mb.multimeter.GetInstanceID())} " +
                          $"hudMultimeter={(hudMultimeter == null ? "NULL" : GetPath(hudMultimeter.transform) + " id=" + hudMultimeter.GetInstanceID())}");
            bool buttonActive = btnT.gameObject.activeInHierarchy;
            Debug.Log($"[VerificarHud] '{label}': panelActivo={panelGO.activeSelf} componentesEnRaiz={leftoverComponents} (esperado 1) " +
                      $"hermanosActivosDeMode_Button={otherActiveSiblings} (esperado 0) Mode_Button activoEnJerarquia={buttonActive} " +
                      $"MultimeterModeButton={(mb != null ? "OK" : "FALTA")} apuntaAlMultimeterDelHUD={wired} " +
                      $"XRSimpleInteractable={(xrsi != null ? "OK" : "FALTA")} Collider={(col != null ? "OK" : "FALTA")}");
            if (!panelGO.activeSelf || leftoverComponents != 1 || otherActiveSiblings != 0 || !buttonActive ||
                mb == null || !wired || xrsi == null || col == null)
                ok = false;
        }

        var vrArt = FindInScene("Multimeter_VR_Art");
        if (vrArt != null)
        {
            Debug.Log($"[VerificarHud] Multimeter_VR_Art activo={vrArt.activeSelf} (esperado False)");
            if (vrArt.activeSelf) ok = false;
        }

        Debug.Log(ok ? "[VerificarHud] ✓ TODO OK." : "[VerificarHud] ✗ HAY PROBLEMAS — ver arriba.");
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    static GameObject FindInScene(string name)
    {
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
        return null;
    }

    static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
#endif

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Linq;

/// <summary>
/// Añade un botón "Encender" (CircuitSwitch) al Reto 2 (paralelo), clonando el switch del Reto 1
/// ("Switch_Series") para reutilizar su mesh/material/XRSimpleInteractable/haptics ya configurados.
///
/// Lo deja como HIJO de Reto2_Zone (así AutoDetectComponents lo mete en el CircuitManager del reto),
/// APAGADO (isOn = false), y sin nodos (el gate del paralelo en CircuitManager.SimulateParallel solo
/// lee sw.isOn; los nodos se auto-crean inofensivos). Idempotente: si ya existe, solo lo reconfigura.
///
/// Ejecutar: Tools → TITA → Reto 2 → Añadir botón Encender  (o -executeMethod Reto2PowerSwitchSetup.SetupBatch)
/// </summary>
public static class Reto2PowerSwitchSetup
{
    const string ScenePath    = "Assets/Scenes/Explorador.unity";
    const string SwitchName   = "Switch_Encender_Reto2";
    const string ZoneName     = "Reto2_Zone";
    const string TemplateName = "Switch_Series";

    // Posición local dentro de Reto2_Zone: entre los dos LEDs (LED_RamaA z≈+0.28, LED_RamaB z≈-0.29,
    // ambos x≈0.65), un poco elevado y hacia el frente para que sea alcanzable en VR.
    static readonly Vector3 LocalPos   = new Vector3(0.65f, 0.06f, 0f);
    static readonly Vector3 LocalScale = new Vector3(0.13f, 0.10f, 0.13f);

    [MenuItem("Tools/TITA/Reto 2/Añadir botón Encender")]
    public static void SetupMenu()
    {
        int n = Setup();
        EditorUtility.DisplayDialog("Reto 2 — Botón Encender",
            n > 0 ? "Botón 'Encender' listo en Reto2_Zone (apagado). Reposiciónalo en el Editor si hace falta."
                  : "No se pudo (¿falta Switch_Series o Reto2_Zone en la escena?). Revisa la consola.",
            "OK");
    }

    public static void SetupBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int n = Setup();
        if (n > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Reto2PowerSwitchSetup] Guardado. Botón Encender añadido/actualizado.");
        }
        else Debug.LogError("[Reto2PowerSwitchSetup] FALLÓ: no se encontró Switch_Series o Reto2_Zone.");
    }

    [MenuItem("Tools/TITA/Reto 2/Quitar botón Encender (auto-energizar)")]
    public static void RemoveMenu()
    {
        int n = Remove();
        EditorUtility.DisplayDialog("Reto 2 — Botón Encender",
            n > 0 ? "Botón 'Encender' eliminado. El circuito queda auto-energizado."
                  : "No había botón que quitar.", "OK");
    }

    public static void RemoveBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int n = Remove();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Reto2PowerSwitchSetup] Quitados {n} botón(es) Encender. Guardado (auto-energizado).");
    }

    static int Remove()
    {
        int n = 0;
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t))
            .Where(t => t.name == SwitchName)
            .ToArray();
        foreach (var t in all) { Object.DestroyImmediate(t.gameObject); n++; }
        return n;
    }

    static int Setup()
    {
        // Buscar incluyendo objetos INACTIVOS (las zonas de reto arrancan desactivadas).
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t))
            .ToArray();

        Transform zone     = all.FirstOrDefault(t => t.name == ZoneName);
        Transform template = all.FirstOrDefault(t => t.name == TemplateName);

        if (zone == null)     { Debug.LogError("[Reto2PowerSwitchSetup] No encontré Reto2_Zone."); return 0; }
        if (template == null) { Debug.LogError("[Reto2PowerSwitchSetup] No encontré Switch_Series (plantilla)."); return 0; }

        // ¿Ya existe? → reconfigurar en vez de duplicar.
        Transform existing = all.FirstOrDefault(t => t.name == SwitchName);
        GameObject go = existing != null
            ? existing.gameObject
            : Object.Instantiate(template.gameObject);

        go.name = SwitchName;
        go.SetActive(true);
        Undo.RegisterCreatedObjectUndo(go, "Añadir botón Encender Reto 2");

        var tr = go.transform;
        tr.SetParent(zone, worldPositionStays: false);
        tr.localPosition = LocalPos;
        tr.localScale    = LocalScale;
        tr.localRotation = Quaternion.Euler(0f, 0f, 0f);

        // CircuitSwitch: apagado y sin heredar los nodos del Reto 1 (el gate del paralelo solo usa isOn).
        var sw = go.GetComponent<CircuitSwitch>();
        if (sw != null)
        {
            sw.isOn   = false;
            sw.nodeA  = null;
            sw.nodeB  = null;
        }
        else Debug.LogWarning("[Reto2PowerSwitchSetup] El clon no tiene CircuitSwitch (revisar plantilla).");

        Debug.Log($"[Reto2PowerSwitchSetup] '{SwitchName}' bajo '{ZoneName}' en local {LocalPos}, OFF.");
        return 1;
    }
}

using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Reto2Led2Debug
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Debug Reto2 LED2")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var gm = Object.FindAnyObjectByType<GameManager>();
        var m = typeof(GameManager).GetMethod("LoadLevel", BindingFlags.NonPublic | BindingFlags.Instance);
        m.Invoke(gm, new object[] { 1 });

        var proto0 = Object.FindObjectsByType<ProtoboardSimulator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(p => p.gameObject.name == "Protoboard_Reto2");
        if (proto0 != null)
        {
            var rails = proto0.todosLosSlots.Where(s => s != null).Select(s => s.railId).Distinct().OrderBy(x => x);
            Debug.Log($"##RAILS_REALES## [{string.Join(", ", rails)}]");
        }

        foreach (var c0 in Object.FindObjectsByType<ProtoboardConnector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            c0.SendMessage("OnEnable", SendMessageOptions.DontRequireReceiver);
        proto0?.ForzarValidacion();

        if (proto0 != null)
        {
            var nA = proto0.NodeForRail("COL_1C");
            var nB = proto0.NodeForRail("GND");
            Debug.Log($"##NODEFORRAIL_DIRECTO## COL_1C={(nA != null ? nA.name : "null")} GND={(nB != null ? nB.name : "null")}");
        }

        var led2 = Object.FindObjectsByType<LED>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(l => l.name == "Circuit_LED2");
        if (led2 == null) { Debug.LogError("[Debug] Circuit_LED2 no existe en la escena."); return; }

        Debug.Log($"##LED2## path=\"{GetPath(led2.transform)}\" activeInHierarchy={led2.gameObject.activeInHierarchy} " +
                  $"polarityInverted={led2.polarityInverted} nodeA={(led2.nodeA != null ? led2.nodeA.name : "null")} " +
                  $"nodeB={(led2.nodeB != null ? led2.nodeB.name : "null")}");

        var conn = led2.GetComponent<ProtoboardConnector>();
        Debug.Log($"##LED2_CONN## tieneProtoboardConnector={conn != null}");
        if (conn != null)
            Debug.Log($"   lockNodes={conn.lockNodes} lockRailA=\"{conn.lockRailA}\" lockRailB=\"{conn.lockRailB}\" " +
                      $"leadA={(conn.leadA != null ? conn.leadA.name : "null")} leadB={(conn.leadB != null ? conn.leadB.name : "null")}");

        var proto = Object.FindObjectsByType<ProtoboardSimulator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(p => p.gameObject.name == "Protoboard_Reto2");
        Debug.Log($"##PROTO2## encontrado={proto != null} activeInHierarchy={proto?.gameObject.activeInHierarchy} " +
                  $"todosLosSlots.Count={proto?.todosLosSlots?.Count}");

        Debug.Log($"##CONN_ACTIVE_STATIC## count={ProtoboardConnector.Active.Count} " +
                  $"contieneLed2={ProtoboardConnector.Active.Contains(conn)}");
        foreach (var c in ProtoboardConnector.Active)
            Debug.Log($"   active-conn: \"{c?.name}\" lockNodes={c?.lockNodes} rails={c?.lockRailA}->{c?.lockRailB}");
    }

    static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}

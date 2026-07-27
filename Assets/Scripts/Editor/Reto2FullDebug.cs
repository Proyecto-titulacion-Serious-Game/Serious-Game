using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Diagnóstico completo del Reto 2: estado de TODAS sus piezas tras aplicar la misma
/// cadena de fixes que FullGameTwoConfigsTest, para ver exactamente dónde se corta el voltaje.</summary>
public static class Reto2FullDebug
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Debug Reto2 completo")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var gm = Object.FindAnyObjectByType<GameManager>();
        var loadLevel = typeof(GameManager).GetMethod("LoadLevel", BindingFlags.NonPublic | BindingFlags.Instance);
        loadLevel.Invoke(gm, new object[] { 1 });

        // Activar la batería (como en el test principal)
        var fuente = Object.FindObjectsByType<VoltageSource>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(v => v.gameObject.name == "Fuente_9V");
        if (fuente != null) fuente.gameObject.SetActive(true);

        // Forzar Awake+OnEnable de TODOS los ProtoboardConnector
        var conns = Object.FindObjectsByType<ProtoboardConnector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[Debug] {conns.Length} ProtoboardConnector en la escena.");
        foreach (var c in conns)
        {
            c.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
            c.SendMessage("OnEnable", SendMessageOptions.DontRequireReceiver);
        }
        Debug.Log($"[Debug] ProtoboardConnector.Active.Count={ProtoboardConnector.Active.Count}");
        foreach (var c in ProtoboardConnector.Active)
            Debug.Log($"   active: \"{GetPath(c.transform)}\" lockNodes={c.lockNodes} rails={c.lockRailA}->{c.lockRailB}");

        var proto = Object.FindObjectsByType<ProtoboardSimulator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(p => p.gameObject.name == "Protoboard_Reto2");
        proto?.ForzarValidacion();
        Debug.Log($"[Debug] proto encontrado={proto != null}");

        if (proto != null && fuente != null)
        {
            var vccNode = proto.NodeForRail("VCC");
            var gndNode = proto.NodeForRail("GND");
            Debug.Log($"[Debug] vccNode={(vccNode != null ? vccNode.name : "null")} gndNode={(gndNode != null ? gndNode.name : "null")}");
            var jVcc = new GameObject("TestJumper_VCC").AddComponent<Jumper>();
            jVcc.transform.SetParent(proto.transform, false);
            jVcc.nodeA = fuente.nodeA; jVcc.nodeB = vccNode;
            var jGnd = new GameObject("TestJumper_GND").AddComponent<Jumper>();
            jGnd.transform.SetParent(proto.transform, false);
            jGnd.nodeA = fuente.nodeB; jGnd.nodeB = gndNode;
            proto.ForzarValidacion();
            Debug.Log($"##JUMPER_VCC## nodeA={(jVcc.nodeA != null ? jVcc.nodeA.name : "null")} nodeB={(jVcc.nodeB != null ? jVcc.nodeB.name : "null")} current={jVcc.current} Va={(jVcc.nodeA != null ? jVcc.nodeA.voltage : -999)} Vb={(jVcc.nodeB != null ? jVcc.nodeB.voltage : -999)}");
            Debug.Log($"##JUMPER_GND## nodeA={(jGnd.nodeA != null ? jGnd.nodeA.name : "null")} nodeB={(jGnd.nodeB != null ? jGnd.nodeB.name : "null")} current={jGnd.current} Va={(jGnd.nodeA != null ? jGnd.nodeA.voltage : -999)} Vb={(jGnd.nodeB != null ? jGnd.nodeB.voltage : -999)}");
        }

        // Dump de TODOS los componentes eléctricos bajo Protoboard_Reto2
        if (proto != null)
        {
            foreach (var comp in proto.GetComponentsInChildren<ElectricalComponent>(true))
            {
                var res = comp as Resistor;
                var led = comp as LED;
                var vs  = comp as VoltageSource;
                string extra = res != null ? $"resistance={res.resistance} hasFault={res.hasFault} current={res.current}"
                              : led != null ? $"isOn={led.isOn} state={led.state} current={led.current} polInv={led.polarityInverted}"
                              : vs  != null ? $"voltage={vs.voltage} activeInHierarchy={vs.gameObject.activeInHierarchy}"
                              : "";
                var conn = comp.GetComponent<ProtoboardConnector>();
                string connInfo = conn != null ? $"lockNodes={conn.lockNodes} rails={conn.lockRailA}->{conn.lockRailB}" : "sin ProtoboardConnector";
                Debug.Log($"##COMP## \"{comp.name}\" tipo={comp.GetType().Name} " +
                          $"nodeA={(comp.nodeA != null ? comp.nodeA.name : "null")} nodeB={(comp.nodeB != null ? comp.nodeB.name : "null")} " +
                          $"Va={(comp.nodeA != null ? comp.nodeA.voltage.ToString("F3") : "-")} " +
                          $"Vb={(comp.nodeB != null ? comp.nodeB.voltage.ToString("F3") : "-")} " +
                          $"{extra} | {connInfo}");
            }
        }
    }

    static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}

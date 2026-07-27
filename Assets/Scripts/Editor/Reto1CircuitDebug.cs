using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Diagnóstico puntual: por qué Resistor_Faulty (Reto 1) mide 0V tras la reparación.</summary>
public static class Reto1CircuitDebug
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Debug Reto1 CircuitManager")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var gm = Object.FindAnyObjectByType<GameManager>();
        var m = typeof(GameManager).GetMethod("LoadLevel", BindingFlags.NonPublic | BindingFlags.Instance);
        m.Invoke(gm, new object[] { 0 });

        var cms = Object.FindObjectsByType<CircuitManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var cm1 = cms.FirstOrDefault(c => GetPath(c.transform) == "GameZones/Reto1_Zone");
        Debug.Log($"[Debug] cm1 encontrado={cm1 != null}");
        if (cm1 == null) return;

        cm1.AutoDetectComponents();
        Debug.Log($"##CM_AFTER_DETECT## components.Count={cm1.components.Count}");
        foreach (var c in cm1.components)
        {
            var res = c as Resistor;
            var led = c as LED;
            var vs  = c as VoltageSource;
            string extra = res != null ? $"resistance={res.resistance} hasFault={res.hasFault}"
                          : led != null ? $"resistance={led.resistance} isOn={led.isOn} state={led.state} polInv={led.polarityInverted}"
                          : vs  != null ? $"voltage={vs.voltage} GetEffectiveVoltage={vs.GetEffectiveVoltage()}"
                          : "";
            Debug.Log($"   comp: {c?.GetType().Name} name=\"{c?.name}\" nodeA={(c.nodeA != null ? c.nodeA.name : "null")} " +
                      $"nodeB={(c.nodeB != null ? c.nodeB.name : "null")} {extra}");
        }

        cm1.ForceSimulate();
        Debug.Log($"##CM_AFTER_FORCESIM## sourceVoltage={cm1.sourceVoltage} totalCurrent={cm1.totalCurrent}");
        foreach (var c in cm1.components)
        {
            var res = c as Resistor;
            var led = c as LED;
            string extra = res != null ? $"resistance={res.resistance} hasFault={res.hasFault} current={res.current} voltageDrop={res.voltageDrop}"
                          : led != null ? $"isOn={led.isOn} state={led.state} current={led.current} voltageDrop={led.voltageDrop}"
                          : "";
            float va = c.nodeA != null ? c.nodeA.voltage : float.NaN;
            float vb = c.nodeB != null ? c.nodeB.voltage : float.NaN;
            Debug.Log($"   post-sim: {c?.GetType().Name} \"{c?.name}\" Va={va} Vb={vb} {extra}");
        }

        // Reparar el resistor y volver a simular, como hace la ruta real.
        var delivery = Object.FindAnyObjectByType<ComponentDeliverySystem>();
        bool ok = delivery.DebugSimularEntregaEInstalacion(ComponentType.Resistor, 850f);
        Debug.Log($"##ENTREGA## ok={ok}");
        cm1.ForceSimulate();
        Debug.Log($"##CM_AFTER_REPAIR## sourceVoltage={cm1.sourceVoltage} totalCurrent={cm1.totalCurrent}");
        foreach (var c in cm1.components)
        {
            var res = c as Resistor;
            var led = c as LED;
            string extra = res != null ? $"resistance={res.resistance} hasFault={res.hasFault} current={res.current}"
                          : led != null ? $"isOn={led.isOn} state={led.state} current={led.current}"
                          : "";
            float va = c.nodeA != null ? c.nodeA.voltage : float.NaN;
            float vb = c.nodeB != null ? c.nodeB.voltage : float.NaN;
            Debug.Log($"   post-repair: {c?.GetType().Name} \"{c?.name}\" Va={va} Vb={vb} {extra}");
        }
    }

    static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}

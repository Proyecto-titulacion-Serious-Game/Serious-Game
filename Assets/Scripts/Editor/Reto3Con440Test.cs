using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Reproduce EXACTO el reporte del usuario: Reto 3 con resistencia 440Ω (dentro de tolerancia de
/// 470Ω correcto), LED polaridad correcta, Capacitor polaridad correcta — ¿completa?
/// Diagnóstico: si NO completa, imprime el estado real de cada componente para ver cuál falla.
/// </summary>
public static class Reto3Con440Test
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Pruebas/Reto 3 con 440 Ohm (repro bug)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>();
        var delivery = Object.FindAnyObjectByType<ComponentDeliverySystem>();

        foreach (var cm in Object.FindObjectsByType<CircuitManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            cm.AutoDetectComponents();
        gm.SendMessage("Start", SendMessageOptions.DontRequireReceiver);

        typeof(GameManager).GetMethod("LoadLevel", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(gm, new object[] { 2 }); // Reto 3

        foreach (var led in Object.FindObjectsByType<LED>(FindObjectsInactive.Exclude))
            led.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);

        bool r3a = delivery.DebugSimularEntregaEInstalacion(ComponentType.Resistor, 440f);
        bool r3b = delivery.DebugSimularEntregaEInstalacion(ComponentType.LED, 1f);
        bool r3c = delivery.DebugSimularEntregaEInstalacion(ComponentType.Capacitor, 1f);

        var victoria = (bool)typeof(GameManager).GetMethod("CumpleVictoriaRetos123", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(gm, null);

        var r = Object.FindObjectsByType<Resistor>(FindObjectsInactive.Exclude)
            .Where(x => x.nodeA != null && x.nodeB != null).ToArray();
        var l = Object.FindObjectsByType<LED>(FindObjectsInactive.Exclude)
            .Where(x => x.nodeA != null && x.nodeB != null).ToArray();
        var c = Object.FindObjectsByType<Capacitor>(FindObjectsInactive.Exclude)
            .Where(x => x.nodeA != null && x.nodeB != null).ToArray();

        Debug.Log("===== RETO3 CON 440 OHM =====");
        Debug.Log($"entregas: R={r3a} LED={r3b} CAP={r3c}  VICTORIA={victoria}");
        foreach (var x in r) Debug.Log($"  R '{x.name}': valor={x.resistance} hasFault={x.hasFault} correctResistance={x.correctResistance} tolerancePercent={x.tolerancePercent}");
        foreach (var x in l) Debug.Log($"  LED '{x.name}': isOn={x.isOn} state={x.state} polInv={x.polarityInverted} I={x.current*1000f:F2}mA");
        foreach (var x in c) Debug.Log($"  CAP '{x.name}': polInv={x.polarityInverted}");

        Debug.Log(victoria ? "##RESULT## OK — completó con 440 Ohm" : "##RESULT## FALLO — NO completó con 440 Ohm");
        if (Application.isBatchMode) EditorApplication.Exit(victoria ? 0 : 1);
    }
}

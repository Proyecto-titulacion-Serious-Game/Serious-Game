using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Diagnóstico de solo lectura: jerarquía y componentes de 'Reto3_Cables' y los 2
/// 'Battery_9V' de la escena, para saber si el circuito del Reto 3 depende de cables físicos
/// jugables (CableElectricalBridge / PhysicCableCon) o son decorativos (solo VRCableRenderer visual)
/// y la fuente ya está pre-cableada a los nodos por código/inspector.</summary>
public static class Reto3CablesDiag
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 3/Diagnosticar cables y bateria (solo lectura)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();

        var cablesRoot = all.FirstOrDefault(t => t.name == "Reto3_Cables");
        if (cablesRoot != null)
        {
            Debug.Log($"[Reto3CablesDiag] ── 'Reto3_Cables' path={Path(cablesRoot)} activeSelf={cablesRoot.gameObject.activeSelf} hijos={cablesRoot.childCount} ──");
            DumpRecursive(cablesRoot, 0);
        }
        else Debug.LogWarning("[Reto3CablesDiag] No until 'Reto3_Cables'.");

        var baterias = all.Where(t => t.name == "Battery_9V").ToList();
        Debug.Log($"[Reto3CablesDiag] {baterias.Count} GameObject(s) 'Battery_9V' en la escena.");
        foreach (var b in baterias)
        {
            Debug.Log($"[Reto3CablesDiag] ── Battery_9V path={Path(b)} activeSelf={b.gameObject.activeSelf} ──");
            var vs = b.GetComponentInChildren<VoltageSource>(true);
            if (vs != null)
                Debug.Log($"[Reto3CablesDiag]   VoltageSource: voltage={vs.voltage} nodeA={(vs.nodeA!=null?vs.nodeA.name:"NULL")} nodeB={(vs.nodeB!=null?vs.nodeB.name:"NULL")} hasFault={vs.hasFault}");
            else
                Debug.Log("[Reto3CablesDiag]   Sin VoltageSource en hijos.");
            DumpRecursive(b, 0);
        }

        // ¿Cuál Battery_9V pertenece a la zona del Reto 3?
        var gm = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);

        // Activar el Reto 3 de verdad (LoadLevel), igual que hizo Reto3SlotConnectionRealTest —
        // así este diagnóstico refleja el estado REAL de juego, no el estado crudo de la escena
        // antes de que el jugador llegue a la zona.
        var lvl = typeof(GameManager).GetMethod("LoadLevel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        lvl.Invoke(gm, new object[] { 2 });
        Debug.Log("[Reto3CablesDiag] ── Tras LoadLevel(2) (Reto 3 activado de verdad) ──");

        if (gm != null && gm.reto3Zone != null)
        {
            Debug.Log($"[Reto3CablesDiag] reto3Zone path={Path(gm.reto3Zone.transform)}");
            var vsEnZona = gm.reto3Zone.GetComponentsInChildren<VoltageSource>(true);
            Debug.Log($"[Reto3CablesDiag] VoltageSource dentro de reto3Zone: {vsEnZona.Length}");
            foreach (var vs in vsEnZona)
                Debug.Log($"[Reto3CablesDiag]   '{vs.name}' voltage={vs.voltage} nodeA={(vs.nodeA!=null?vs.nodeA.name:"NULL")} nodeB={(vs.nodeB!=null?vs.nodeB.name:"NULL")} enabled={vs.enabled} activeInHierarchy={vs.gameObject.activeInHierarchy}");

            var cablesEnZona = gm.reto3Zone.GetComponentsInChildren<CableElectricalBridge>(true);
            Debug.Log($"[Reto3CablesDiag] CableElectricalBridge dentro de reto3Zone: {cablesEnZona.Length}");
            foreach (var ceb in cablesEnZona)
                Debug.Log($"[Reto3CablesDiag]   '{ceb.name}' path={Path(ceb.transform)}");

            var vrCablesEnZona = gm.reto3Zone.GetComponentsInChildren<VRCableRenderer>(true);
            Debug.Log($"[Reto3CablesDiag] VRCableRenderer dentro de reto3Zone: {vrCablesEnZona.Length}");
        }
    }

    static void DumpRecursive(Transform t, int depth)
    {
        var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name);
        Debug.Log($"[Reto3CablesDiag] {new string(' ', depth * 2)}{t.name}  [{string.Join(",", comps)}]  active={t.gameObject.activeSelf}");
        for (int i = 0; i < t.childCount; i++)
            DumpRecursive(t.GetChild(i), depth + 1);
    }

    static string Path(Transform t)
    {
        string s = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }
}

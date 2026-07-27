using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Diagnóstico de SOLO LECTURA: compara las posiciones/rotaciones MUNDO del ArduinoCore, sus nodos
/// de pin (Nodo_D2..D13), el ProtoboardSimulator del Reto 4 y sus ProtoboardSlots, para detectar si
/// el pivote de inclinación (-32°/-90°, agregado 2026-07-16) dejó a alguno de estos fuera de la
/// jerarquía tiltada — lo que desalinearía el "imán" de CableProbePlug (que usa posición MUNDO)
/// respecto al mesh visible.
///
/// Ejecutar: Tools → TITA → Reto 4 → Diagnosticar geometría de cables (solo lectura)
/// </summary>
public static class Reto4CableGeometryDiag
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 4/Diagnosticar geometria de cables (solo lectura)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var sims = Object.FindObjectsByType<ProtoboardSimulator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[Reto4CableGeom] {sims.Length} ProtoboardSimulator(es) en la escena (incluyendo inactivos).");

        var bridge = Object.FindAnyObjectByType<ArduinoNetworkBridge>(FindObjectsInactive.Include);
        Debug.Log(bridge != null
            ? $"[Reto4CableGeom] ArduinoNetworkBridge SÍ existe en '{Path(bridge.transform)}' active={bridge.gameObject.activeSelf}"
            : "[Reto4CableGeom] ArduinoNetworkBridge NO existe en ningún lado de la escena.");

        var arduino = Object.FindAnyObjectByType<ArduinoCore>(FindObjectsInactive.Include);
        if (arduino == null)
        {
            Debug.LogError("[Reto4CableGeom] No hay ArduinoCore en la escena (ni activo ni inactivo). Volcando jerarquía de Reto4_Zone / Reto4_TiltGroup para ubicar el modelo real:");
            DumpHierarchyByName("Reto4_Zone");
            DumpHierarchyByName("Reto4_TiltGroup");
            Finish(1);
            return;
        }

        Debug.Log($"[Reto4CableGeom] ArduinoCore path={Path(arduino.transform)} worldPos={arduino.transform.position} worldRot={arduino.transform.rotation.eulerAngles} lossyScale={arduino.transform.lossyScale}");

        // Cadena de padres del ArduinoCore, con rotación local de cada uno (para ver dónde vive el pivote de tilt).
        Debug.Log("[Reto4CableGeom] Cadena de padres del ArduinoCore:");
        for (Transform t = arduino.transform; t != null; t = t.parent)
            Debug.Log($"    {t.name}  localPos={t.localPosition}  localRot={t.localRotation.eulerAngles}  localScale={t.localScale}");

        Debug.Log($"[Reto4CableGeom] pinNodeMap.Count={arduino.pinNodeMap.Count}");
        foreach (var m in arduino.pinNodeMap)
        {
            if (m.node == null) { Debug.LogWarning($"    Pin {m.pin}: node=NULL"); continue; }
            Debug.Log($"    Pin {m.pin}: node='{m.node.gameObject.name}' path={Path(m.node.transform)} worldPos={m.node.transform.position}");
        }
        if (arduino.nodoGND != null)
            Debug.Log($"[Reto4CableGeom] nodoGND path={Path(arduino.nodoGND.transform)} worldPos={arduino.nodoGND.transform.position}");
        else
            Debug.LogWarning("[Reto4CableGeom] arduino.nodoGND es NULL.");

        // Identificar cuál sim es el del Reto 4: el que NO se llama "Protoboard_Reto2" ni cuelga de ese nombre.
        foreach (var sim in sims)
        {
            bool esReto2 = sim.gameObject.name.Contains("Reto2") || Path(sim.transform).Contains("Reto2");
            var slots = sim.todosLosSlots?.Where(s => s != null).ToList() ?? new System.Collections.Generic.List<ProtoboardSlot>();
            Debug.Log($"[Reto4CableGeom] ##SIM## {(esReto2 ? "(Reto2)" : "(candidato Reto4)")} path={Path(sim.transform)} name={sim.gameObject.name} slots={slots.Count} worldPos={sim.transform.position} worldRot={sim.transform.rotation.eulerAngles}");

            var runSim = typeof(ProtoboardSimulator).GetMethod("RunSimulation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            runSim?.Invoke(sim, null);
            Debug.Log($"    ConnectionPoints tras simular = {sim.ConnectionPoints.Count} (slots propios={slots.Count}; " +
                      (esReto2 ? "esperado = slots propios EXACTO, sin contaminación del Arduino del Reto 4)"
                               : "esperado = slots+14 pines D+3 GND+6 pines A = slots+23)"));

            if (esReto2 || slots.Count == 0) continue;

            var s0 = slots[0];
            Debug.Log($"    Slot ejemplo '{s0.name}' railId={s0.railId} path={Path(s0.transform)} worldPos={s0.transform.position}");
            var gnd = slots.FirstOrDefault(s => s.railId == "GND");
            if (gnd != null) Debug.Log($"    Slot GND '{gnd.name}' worldPos={gnd.transform.position}");

            // Distancia entre el nodo del pin Arduino y el slot de protoboard más cercano — si el
            // usuario dice "no conecta", una distancia grande (> plugRadius=0.03) explicaría por qué
            // el imán de CableProbePlug nunca encuentra el hueco al soltar cerca del mesh visible.
            if (arduino.pinNodeMap.Count > 0 && arduino.pinNodeMap[0].node != null)
            {
                float dist = Vector3.Distance(arduino.pinNodeMap[0].node.transform.position, s0.transform.position);
                Debug.Log($"    Distancia Pin[{arduino.pinNodeMap[0].pin}] <-> Slot ejemplo = {dist:F3} m (referencia, no implica error por sí sola)");
            }
        }

        // Cables físicos ya presentes en la escena (CableProbePlug) — ver si sus puntas están MUY lejos
        // de cualquier ConnectionPoint conocido (indicaría que el jumper vive fuera del pivote inclinado).
        var probes = Object.FindObjectsByType<CableProbePlug>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[Reto4CableGeom] CableProbePlug en escena: {probes.Length}");
        foreach (var p in probes)
            Debug.Log($"    Probe '{p.gameObject.name}' path={Path(p.transform)} worldPos={p.transform.position}");

        // ── CableBoxSpawner del Reto 4: inspeccionar su cablePrefab asignado ──
        var boxes = Object.FindObjectsByType<CableBoxSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[Reto4CableGeom] CableBoxSpawner en escena: {boxes.Length}");
        foreach (var box in boxes)
        {
            Debug.Log($"[Reto4CableGeom] ##BOX## path={Path(box.transform)} cablePrefab={(box.cablePrefab != null ? box.cablePrefab.name : "NULL")}");
            if (box.cablePrefab == null) continue;
            Debug.Log("    Jerarquía del cablePrefab:");
            DumpRecursivePrefab(box.cablePrefab.transform, 0);
        }

        // ── GameManager.protoSim: ¿apunta al ProtoboardSimulator correcto del Reto 4? ──
        var gmCheck = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (gmCheck != null)
        {
            var f = typeof(GameManager).GetField("protoSim", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                 ?? typeof(GameManager).GetField("protoSim", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var val = f?.GetValue(gmCheck) as ProtoboardSimulator;
            Debug.Log($"[Reto4CableGeom] GameManager.protoSim = {(val != null ? Path(val.transform) : "NULL")}");
        }

        Finish(0);
    }

    static void DumpHierarchyByName(string rootName)
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        var root = all.FirstOrDefault(t => t.name == rootName);
        if (root == null) { Debug.LogWarning($"[Reto4CableGeom] No encontré '{rootName}' en la escena."); return; }
        Debug.Log($"[Reto4CableGeom] ── Jerarquía bajo '{rootName}' ──");
        DumpRecursive(root, 0);
    }

    static void DumpRecursivePrefab(Transform t, int depth)
    {
        var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name);
        Debug.Log($"[Reto4CableGeom] {new string(' ', depth * 2)}{t.name}  [{string.Join(",", comps)}]");
        for (int i = 0; i < t.childCount; i++)
            DumpRecursivePrefab(t.GetChild(i), depth + 1);
    }

    static void DumpRecursive(Transform t, int depth)
    {
        var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name);
        Debug.Log($"[Reto4CableGeom] {new string(' ', depth * 2)}{t.name}  [{string.Join(",", comps)}]  active={t.gameObject.activeSelf} localPos={t.localPosition}");
        for (int i = 0; i < t.childCount; i++)
            DumpRecursive(t.GetChild(i), depth + 1);
    }

    static string Path(Transform t)
    {
        if (t == null) return "(null)";
        string s = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }

    static void Finish(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }
}

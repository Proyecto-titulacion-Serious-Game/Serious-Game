using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// FIX QUIRÚRGICO: el GameObject "Arduino" (bajo Reto4_TiltGroup) tiene el mesh y TODOS los nodos
/// físicos (Nodo_D0..D12, Nodo_P13, Nodo_A0..A5, Nodo_GND ×3) ya colocados y con collider — pero
/// no tiene el componente <see cref="ArduinoCore"/>. Sin ArduinoCore, ProtoboardSimulator.
/// GatherConnectionPoints() nunca encuentra estos nodos (los busca vía _arduino.pinNodeMap), así
/// que el imán de CableProbePlug NUNCA los ofrece como huecos enchufables — los cables jamás
/// "conectan" a los pines del Arduino aunque el modelo se vea bien.
///
/// Este tool NO crea geometría nueva: reutiliza los Nodo_* ya existentes (evita duplicados) y
/// simplemente los registra en un ArduinoCore.pinNodeMap nuevo.
///
/// Ejecutar: Tools → TITA → Reto 4 → Restaurar ArduinoCore (reutiliza nodos existentes)
/// </summary>
public static class Reto4RestoreArduinoCore
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";
    const string TiltGroupName = "Reto4_TiltGroup";

    [MenuItem("Tools/TITA/Reto 4/Restaurar ArduinoCore (reutiliza nodos existentes)")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var (ok, msg) = DoFix();
        if (ok) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
        Debug.Log("[Reto4RestoreArduinoCore] " + msg);
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    static (bool, string) DoFix()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();

        Transform tiltGroup = all.FirstOrDefault(t => t.name == TiltGroupName);
        if (tiltGroup == null) return (false, $"No encontré '{TiltGroupName}'.");

        Transform arduinoRoot = tiltGroup.Find("Arduino");
        if (arduinoRoot == null) return (false, $"No encontré 'Arduino' bajo '{TiltGroupName}'.");

        var core = arduinoRoot.GetComponent<ArduinoCore>();
        bool creado = core == null;
        if (core == null) core = Undo.AddComponent<ArduinoCore>(arduinoRoot.gameObject);

        Undo.RecordObject(core, "Restaurar pinNodeMap");
        core.pinNodeMap.Clear();

        int pinesRegistrados = 0;
        for (int pin = 0; pin <= 13; pin++)
        {
            string nombre = pin == 13 ? "Nodo_P13" : $"Nodo_D{pin}";
            Transform nodoT = FindDescendant(arduinoRoot, nombre);
            if (nodoT == null) continue;

            var node = nodoT.GetComponent<ElectricalNode>();
            if (node == null) continue;

            core.RegisterPinNode(pin, node);
            if (pin == 13) core.nodoP13 = node;
            pinesRegistrados++;
        }

        // GND: puede haber varias "Nodo_GND"/"Nodo_GND (1)"/"Nodo_GND (2)" — todas deben representar
        // el mismo nodo eléctrico de retorno. Usamos la primera como nodoGND del core; si BuildNodeMap
        // del sandbox las trata como nodos independientes eso es un problema de topología aparte
        // (no de este fix), documentado abajo en el resumen.
        Transform gndT = FindDescendant(arduinoRoot, "Nodo_GND");
        if (gndT != null)
        {
            var gndNode = gndT.GetComponent<ElectricalNode>();
            if (gndNode != null) core.nodoGND = gndNode;
        }

        Transform a0T = FindDescendant(arduinoRoot, "Nodo_A0");
        if (a0T != null)
        {
            var a0Node = a0T.GetComponent<ElectricalNode>();
            if (a0Node != null) core.nodoA0 = a0Node;
        }

        int gndCount = arduinoRoot.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Nodo_GND"));

        EditorUtility.SetDirty(core);

        string resumen = $"ArduinoCore {(creado ? "CREADO" : "ya existía")} en '{arduinoRoot.name}'. " +
            $"{pinesRegistrados}/14 pines (D0-D13) reenlazados a sus nodos físicos existentes. " +
            $"nodoGND={(core.nodoGND != null ? core.nodoGND.name : "NULL")} nodoA0={(core.nodoA0 != null ? core.nodoA0.name : "NULL")}. " +
            (gndCount > 1 ? $"AVISO: hay {gndCount} 'Nodo_GND*' distintos en el modelo — solo el primero quedó como core.nodoGND; " +
                            "si el sandbox los trata como nodos eléctricos separados (no unidos por un riel GND real), " +
                            "conectar a Nodo_GND (1) o (2) no cerraría el circuito. Revisar con Tools→TITA→Reto4→Inspeccionar slots." : "") +
            " NOTA: ArduinoNetworkBridge (recepción del sketch subido por el Técnico) tampoco existe en la escena — " +
            "no se tocó en este fix porque es un NetworkBehaviour de Fusion y requiere verificar su Spawn/NetworkObject " +
            "antes de tocarlo; puede ser un problema aparte si el sketch nunca le llega al Explorador.";

        return (true, resumen);
    }

    static Transform FindDescendant(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}

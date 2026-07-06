using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Rellena en el Inspector de ArduinoCore las referencias de nodo (pinNodeMap D2..D13 + nodoGND
/// + nodoP13 + nodoA0) buscando los ElectricalNode hijos por nombre, SIN mover ningún nodo.
/// Equivale al auto-enlace runtime (ArduinoCore.AutoDiscoverPinNodes) pero PERSISTIDO en la escena.
///
/// Menú: Tools → TITA → Reto 4 → Vincular pinNodeMap (sin mover nodos)
/// </summary>
public static class ArduinoPinNodeLinker
{
    [MenuItem("Tools/TITA/Reto 4/Vincular pinNodeMap (sin mover nodos)")]
    public static void Link()
    {
        var core = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<ArduinoCore>()
            : null;
        if (core == null) core = Object.FindAnyObjectByType<ArduinoCore>();
        if (core == null)
        {
            EditorUtility.DisplayDialog("Vincular pinNodeMap", "No hay ningún ArduinoCore en la escena.", "OK");
            return;
        }

        var nodos = core.GetComponentsInChildren<ElectricalNode>(true);

        Undo.RecordObject(core, "Vincular pinNodeMap");

        // ── Pines D2..D13 ──
        int pines = 0;
        foreach (var n in nodos)
        {
            int pin = ParsePin(n.name);
            if (pin < 2 || pin > 13) continue;
            core.RegisterPinNode(pin, n);
            pines++;
        }

        // ── GND / P13 / A0 (por nombre, el más cercano al Arduino) ──
        if (core.nodoGND == null) core.nodoGND = NearestNamed(core, nodos, "GND");
        if (core.nodoP13 == null) core.nodoP13 = core.PinToNode(13);
        if (core.nodoA0  == null) core.nodoA0  = NearestNamed(core, nodos, "A0");

        EditorUtility.SetDirty(core);
        EditorSceneManager.MarkSceneDirty(core.gameObject.scene);
        Selection.activeGameObject = core.gameObject;

        int validos = 0;
        foreach (var m in core.pinNodeMap) if (m.node != null) validos++;

        Debug.Log($"[Linker] pinNodeMap vinculado: {validos}/{core.pinNodeMap.Count} pines con nodo, " +
                  $"GND={(core.nodoGND != null)}, P13={(core.nodoP13 != null)}, A0={(core.nodoA0 != null)}. " +
                  "Posiciones SIN tocar.", core);
        EditorUtility.DisplayDialog("Vincular pinNodeMap",
            $"Listo. {validos}/{core.pinNodeMap.Count} pines vinculados " +
            $"(GND={(core.nodoGND != null)}, P13={(core.nodoP13 != null)}, A0={(core.nodoA0 != null)}).\n\n" +
            "No se movió ningún nodo. Guardá la escena (Ctrl+S).", "OK");
    }

    static int ParsePin(string name)
    {
        var m = Regex.Match(name, @"(?:Nodo_[DP]|Pin_?)(\d{1,2})\b", RegexOptions.IgnoreCase);
        return (m.Success && int.TryParse(m.Groups[1].Value, out int p)) ? p : -1;
    }

    static ElectricalNode NearestNamed(ArduinoCore core, ElectricalNode[] nodos, string contains)
    {
        ElectricalNode best = null; float bestD = float.MaxValue;
        foreach (var n in nodos)
        {
            if (n.name.IndexOf(contains, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            float d = (n.transform.position - core.transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = n; }
        }
        return best;
    }
}

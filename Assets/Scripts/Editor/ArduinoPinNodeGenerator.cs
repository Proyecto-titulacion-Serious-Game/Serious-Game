using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Genera un <see cref="ElectricalNode"/> físico por cada pin digital D2–D13 del Arduino
/// y llena <see cref="ArduinoCore.pinNodeMap"/>, de modo que el número de pin que programa
/// el Técnico sea físicamente significativo (modo creativo): conectar al pin 7 ≠ conectar
/// al pin 13. Sin esto, todos los pines caían en nodoP13 (número de pin cosmético).
///
/// Cada nodo recibe: ElectricalNode + SphereCollider (no-trigger, para que la punta del
/// multímetro y los cables lo detecten) + NodeInteractable (para medir directo sobre el pin).
///
/// Menú: Tools → TITA → Reto 4 → Generar Nodos de Pines (D2–D13)
/// </summary>
public static class ArduinoPinNodeGenerator
{
    // Dimensiones físicas Arduino Uno (metros) — coinciden con ArduinoModelCreator
    const float PCB_W = 0.0686f;
    const float PIN_S = 0.00254f;
    const float HDR_H = 0.0085f;
    const float PCB_H = 0.0016f;

    [MenuItem("Tools/TITA/Reto 4/Generar Nodos de Pines (D2-D13)")]
    public static void Generate()
    {
        var core = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<ArduinoCore>()
            : null;
        if (core == null) core = Object.FindAnyObjectByType<ArduinoCore>();

        if (core == null)
        {
            EditorUtility.DisplayDialog("ArduinoCore no encontrado",
                "No hay ningún GameObject con ArduinoCore en la escena.\n" +
                "Ejecuta primero el Reto4SetupWizard / Auto-Setup para crear el Arduino.", "OK");
            return;
        }

        Transform parent = core.transform;
        float y    = HDR_H + PCB_H;
        float z    = -0.0025f;
        float x13  = PCB_W - PIN_S * 0.5f;

        // Las constantes están en METROS reales, pero la posición de los nodos es LOCAL al
        // GO del Arduino. Si ese GO tiene escala ≠ 1 (en escena está a 0.2), un valor local
        // metido tal cual se reduce por la escala del padre y los pines quedan amontonados
        // fuera de los headers. Compensamos dividiendo por la escala mundial del padre, así
        // los nodos caen a tamaño/posición real en MUNDO sea cual sea la escala del GO.
        Vector3 ls = parent.lossyScale;
        Vector3 inv = new Vector3(
            Mathf.Approximately(ls.x, 0f) ? 1f : 1f / ls.x,
            Mathf.Approximately(ls.y, 0f) ? 1f : 1f / ls.y,
            Mathf.Approximately(ls.z, 0f) ? 1f : 1f / ls.z);
        float radius = 0.0016f * Mathf.Max(inv.x, inv.y, inv.z);  // ~1.6 mm en MUNDO

        Undo.RecordObject(core, "Llenar pinNodeMap");
        core.pinNodeMap.Clear();

        ElectricalNode node13 = null;
        int creados = 0, reusados = 0;

        for (int pin = 2; pin <= 13; pin++)
        {
            int   stepsFrom13 = 13 - pin;
            float gap         = pin <= 7 ? PIN_S : 0f;   // hueco real entre D7 y D8
            var   pos         = Vector3.Scale(new Vector3(x13 - stepsFrom13 * PIN_S - gap, y, z), inv);

            bool existed;
            var node = FindOrCreatePinNode(parent, pin, pos, radius, out existed);
            if (existed) reusados++; else creados++;

            core.RegisterPinNode(pin, node);
            if (pin == 13) node13 = node;
        }

        if (node13 != null) core.nodoP13 = node13;   // mantener alias legacy

        EditorUtility.SetDirty(core);
        EditorSceneManager.MarkSceneDirty(core.gameObject.scene);
        Selection.activeGameObject = core.gameObject;

        EditorUtility.DisplayDialog("Nodos de pin generados",
            $"pinNodeMap poblado con 12 pines (D2–D13).\n" +
            $"  • Creados: {creados}\n  • Reutilizados: {reusados}\n\n" +
            "Ahora cada pin tiene su propio ElectricalNode → el número de pin importa.\n" +
            "El Explorador debe conectar su circuito al header del pin que dictó el Técnico.\n\n" +
            "Siguiente: posiciona los nodos sobre los headers reales si usas el modelo 3D, " +
            "y cablea el multímetro con Tools → TITA → Multímetro → Cablear Multímetro ART.", "OK");
    }

    static ElectricalNode FindOrCreatePinNode(Transform parent, int pin, Vector3 pos, float radius, out bool existed)
    {
        // Nombres aceptados (reutiliza Nodo_P13 legacy para el pin 13)
        string primary = $"Nodo_D{pin}";
        string[] aliases = pin == 13
            ? new[] { "Nodo_P13", "Nodo_D13" }
            : new[] { $"Nodo_D{pin}", $"Nodo_P{pin}" };

        Transform t = null;
        foreach (var nm in aliases) { t = parent.Find(nm); if (t != null) break; }

        GameObject go;
        if (t != null) { go = t.gameObject; existed = true; }
        else
        {
            go = new GameObject(primary);
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "Crear nodo pin");
            existed = false;
        }

        go.transform.localPosition = pos;

        // NOTA: usar TryGetComponent + null-check explícito, NO el operador `??`.
        // `??` no respeta el `==` sobrecargado de UnityEngine.Object y puede devolver
        // un "fake-null" (causa del MissingComponentException en col.isTrigger).
        if (!go.TryGetComponent<ElectricalNode>(out var node))
            node = Undo.AddComponent<ElectricalNode>(go);

        if (!go.TryGetComponent<SphereCollider>(out var col))
            col = Undo.AddComponent<SphereCollider>(go);
        col.isTrigger = false;   // no-trigger: detectable por SphereCast del multímetro y cables
        col.radius    = radius;  // compensado por la escala del padre → ~1.6 mm en MUNDO

        // NodeInteractable para poder medir directamente sobre el header del pin.
        // (RequireComponent(Collider) ya satisfecho por el SphereCollider de arriba.)
        if (!go.TryGetComponent<NodeInteractable>(out var ni))
            ni = Undo.AddComponent<NodeInteractable>(go);
        ni.nodeTarget = node;

        return node;
    }
}

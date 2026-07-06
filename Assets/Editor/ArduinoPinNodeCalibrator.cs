using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Calibra la fila de nodos de pin (Nodo_D2..D13) sobre los headers del modelo 3D del Arduino
/// SIN depender de constantes de Arduino Uno genérico (que no calzan con un modelo custom).
///
/// Uso:
///   1. En el Editor, mové A MANO 'Nodo_D2' y 'Nodo_D13' (alias 'Nodo_P13') para que queden
///      exactamente sobre sus headers visibles del modelo.
///   2. Ejecutá este menú: distribuye Nodo_D3..D12 en línea recta entre ambos, con el pitch
///      correcto y respetando el hueco real entre D7 y D8.
///
/// Solo mueve D3..D12; D2 y D13 quedan donde vos los pusiste (son las referencias).
/// Menú: Tools → TITA → Reto 4 → Calibrar Nodos de Pines (D2↔D13)
/// </summary>
public static class ArduinoPinNodeCalibrator
{
    // Hueco entre D7 y D8 expresado en "anchos de pin" (Arduino real ≈ 1 pin extra).
    const float GAP_PINS = 1f;

    [MenuItem("Tools/TITA/Reto 4/Calibrar Nodos de Pines (D2↔D13)")]
    public static void Calibrate()
    {
        var core = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<ArduinoCore>()
            : null;
        if (core == null) core = Object.FindAnyObjectByType<ArduinoCore>();
        if (core == null)
        {
            EditorUtility.DisplayDialog("Calibrar nodos", "No hay ningún ArduinoCore en la escena.", "OK");
            return;
        }

        Transform parent = core.transform;
        Transform d2  = FindNode(parent, 2);
        Transform d13 = FindNode(parent, 13);

        if (d2 == null || d13 == null)
        {
            EditorUtility.DisplayDialog("Calibrar nodos",
                "No encontré 'Nodo_D2' y/o 'Nodo_D13' (o 'Nodo_P13') como hijos del Arduino.\n\n" +
                "Asegurate de que existan y colocá ambos sobre sus headers antes de calibrar.", "OK");
            return;
        }

        Vector3 a = d2.localPosition;   // referencia: header de D2 (lo pusiste vos)
        Vector3 b = d13.localPosition;  // referencia: header de D13

        float uMax = U(13);             // posición paramétrica del último pin
        int movidos = 0;

        for (int pin = 3; pin <= 12; pin++)
        {
            var t = FindNode(parent, pin);
            if (t == null) continue;     // si falta un nodo, lo salteamos (el auto-enlace ya lo crea si hace falta)
            float f = U(pin) / uMax;      // 0 en D2, 1 en D13
            Undo.RecordObject(t, "Calibrar nodo pin");
            t.localPosition = Vector3.Lerp(a, b, f);
            EditorUtility.SetDirty(t);
            movidos++;
        }

        EditorSceneManager.MarkSceneDirty(core.gameObject.scene);

        Debug.Log($"[Calibrar] Nodos D3..D12 distribuidos entre D2 {a} y D13 {b} (gap D7/D8 = {GAP_PINS} pin). " +
                  $"Movidos: {movidos}.", core);
        EditorUtility.DisplayDialog("Calibrar nodos",
            $"Listo. {movidos} nodos (D3–D12) distribuidos entre D2 y D13.\n\n" +
            "Verificá en el Editor que cada nodo cae sobre su header. Guardá (Ctrl+S) y probá en VR: " +
            "el cable a D2 ahora debería enganchar 'Nodo_D2'.", "OK");
    }

    /// <summary>Posición paramétrica del pin en la fila, con hueco entre D7 y D8.</summary>
    static float U(int pin) => (pin - 2) + (pin >= 8 ? GAP_PINS : 0f);

    static Transform FindNode(Transform parent, int pin)
    {
        string[] aliases = pin == 13
            ? new[] { "Nodo_D13", "Nodo_P13" }
            : new[] { $"Nodo_D{pin}", $"Nodo_P{pin}" };
        foreach (var nm in aliases)
        {
            var t = parent.Find(nm);
            if (t != null) return t;
        }
        return null;
    }
}

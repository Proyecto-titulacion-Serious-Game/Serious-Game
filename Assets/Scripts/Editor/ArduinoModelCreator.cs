#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Aplica el modelo 3D real del Arduino Uno (OBJ importado desde Meshy AI) al GO
/// que tiene ArduinoCore y reposiciona los nodos eléctricos a sus pines físicos.
///
/// El modelo OBJ se encuentra en:
///   Assets/Art/Arduino/model 2/source/Arduino_uno_r3_v2.obj
///
/// Tools > TITA > Aplicar Modelo 3D Arduino Uno
/// </summary>
public static class ArduinoModelCreator
{
    const string MODEL_PATH =
        "Assets/Art/Arduino/model 2/source/Arduino_uno_r3_v2.obj";

    // Si la fila de pines digitales queda en el borde LARGO opuesto (modelo espejado
    // por la conversión de handedness del OBJ), pon esto en true para girar la malla 180°.
    const bool FLIP_180_Y = false;

    // Dimensiones reales Arduino Uno para calcular posiciones de nodos (metros).
    // El OBJ "Arduino_uno_r3_v2" mide 9.04 x 7.04 unidades (ratio 1.284 ≈ Uno real 1.285),
    // así que estas constantes ya encajan: solo hay que escalarlo y alinear su esquina.
    const float PCB_W = 0.0686f;
    const float PCB_D = 0.0534f;
    const float PCB_H = 0.0016f;
    const float PIN_S = 0.00254f;
    const float HDR_H = 0.0085f;

    [MenuItem("Tools/TITA/Aplicar Modelo 3D Arduino Uno")]
    static void Crear()
    {
        // ── Cargar el modelo importado ────────────────────────────────────
        var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MODEL_PATH);
        if (modelPrefab == null)
        {
            EditorUtility.DisplayDialog("Modelo no encontrado",
                $"No se encontró el modelo en:\n{MODEL_PATH}\n\n" +
                "Asegúrate de que el archivo .obj esté importado en esa ruta.",
                "OK");
            return;
        }

        // ── Buscar GO del Arduino en la escena ────────────────────────────
        GameObject arduinoGO = null;

        if (Selection.activeGameObject != null &&
            Selection.activeGameObject.GetComponent<ArduinoCore>() != null)
            arduinoGO = Selection.activeGameObject;
        else
            arduinoGO = Object.FindAnyObjectByType<ArduinoCore>()?.gameObject;

        if (arduinoGO == null)
        {
            bool crear = EditorUtility.DisplayDialog("Arduino GO no encontrado",
                "No se encontró ningún GO con ArduinoCore en la escena.\n\n" +
                "¿Crear un nuevo GO 'Arduino_Uno' como raíz?",
                "Crear", "Cancelar");
            if (!crear) return;

            arduinoGO = new GameObject("Arduino_Uno");
            Undo.RegisterCreatedObjectUndo(arduinoGO, "Crear Arduino_Uno");
        }

        Undo.RecordObject(arduinoGO.transform, "Aplicar modelo Arduino");

        // ── Eliminar modelo anterior si existe ────────────────────────────
        var oldModel = arduinoGO.transform.Find("[Arduino_Model]");
        if (oldModel != null) Undo.DestroyObjectImmediate(oldModel.gameObject);

        // ── Instanciar el modelo OBJ como hijo ────────────────────────────
        var modelGO = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, arduinoGO.transform);
        Undo.RegisterCreatedObjectUndo(modelGO, "Modelo Arduino OBJ");
        modelGO.name = "[Arduino_Model]";
        modelGO.transform.localPosition = Vector3.zero;
        modelGO.transform.localRotation = FLIP_180_Y ? Quaternion.Euler(0f, 180f, 0f)
                                                     : Quaternion.identity;

        // ── Escalar a tamaño real por la PROFUNDIDAD (eje Z) ──────────────
        // A diferencia del ancho (X), la profundidad del Uno no tiene voladizos
        // (el USB y el jack salen por el lado X), así que Z→PCB_D da la escala
        // correcta aunque el OBJ traiga conectores que sobresalen. Para este modelo:
        // Zspan 7.04 u → PCB_D 0.0534 m  ⇒  escala ≈ 0.00759 (PCB queda 0.0686 x 0.0534).
        var renderers = modelGO.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds wb = renderers[0].bounds;
            foreach (var r in renderers) wb.Encapsulate(r.bounds);
            if (wb.size.z > 1e-5f)
                modelGO.transform.localScale = Vector3.one * (PCB_D / wb.size.z);

            // ── Alinear la esquina de la PCB al origen del ArduinoCore ────
            // Convención de los nodos: X∈[0,PCB_W], Z∈[-PCB_D,0], Y desde la base.
            // Este OBJ tiene el pivote CENTRADO, así que sin este corrimiento la
            // malla quedaría medio tablero desfasada de los nodos. Se recalculan los
            // bounds ya escalados y se lleva la esquina (minX, maxZ, minY) al origen.
            wb = renderers[0].bounds;
            foreach (var r in renderers) wb.Encapsulate(r.bounds);
            Vector3 lMin = arduinoGO.transform.InverseTransformPoint(wb.min);
            Vector3 lMax = arduinoGO.transform.InverseTransformPoint(wb.max);
            Vector3 p = modelGO.transform.localPosition;
            modelGO.transform.localPosition = new Vector3(p.x - lMin.x, p.y - lMin.y, p.z - lMax.z);
        }

        // ── Reubicar Nodo_P13, Nodo_GND, Nodo_A0 a pines físicos reales ──
        // Mismas constantes en METROS que ArduinoPinNodeGenerator; se dividen por la escala
        // mundial del GO para que caigan a tamaño real aunque el Arduino esté escalado (0.2).
        Vector3 ls = arduinoGO.transform.lossyScale;
        Vector3 inv = new Vector3(
            Mathf.Approximately(ls.x, 0f) ? 1f : 1f / ls.x,
            Mathf.Approximately(ls.y, 0f) ? 1f : 1f / ls.y,
            Mathf.Approximately(ls.z, 0f) ? 1f : 1f / ls.z);

        float pinTopZ = -0.0025f;
        float pinBotZ = -PCB_D - 0.0025f;

        Vector3 posP13 = Vector3.Scale(new Vector3(PCB_W - PIN_S * 0.5f,           HDR_H + PCB_H, pinTopZ), inv);
        Vector3 posGND = Vector3.Scale(new Vector3(PCB_W - PIN_S * 0.5f - PIN_S*7, HDR_H + PCB_H, pinTopZ), inv);
        Vector3 posA0  = Vector3.Scale(new Vector3(PCB_W * 0.90f,                   HDR_H + PCB_H, pinBotZ), inv);

        int reubicados = 0;
        foreach (Transform child in arduinoGO.transform)
        {
            if (child.name == "Nodo_P13") { child.localPosition = posP13; reubicados++; }
            if (child.name == "Nodo_GND") { child.localPosition = posGND; reubicados++; }
            if (child.name == "Nodo_A0")  { child.localPosition = posA0;  reubicados++; }
        }

        // ── Finalizar ─────────────────────────────────────────────────────
        EditorUtility.SetDirty(arduinoGO);
        EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Selection.activeGameObject = arduinoGO;

        string msg = $"Modelo Arduino Uno aplicado en '{arduinoGO.name}'.\n\n" +
                     $"Modelo: {MODEL_PATH}\n\n";
        msg += reubicados > 0
            ? $"{reubicados}/3 nodos eléctricos reposicionados a pines físicos reales.\n\n"
            : "No se encontraron Nodo_P13/GND/A0 como hijos directos.\n" +
              "  Ejecuta primero el Wizard para crearlos y vuelve a correr este tool.\n\n";
        msg += "Ajusta la posición del GO padre sobre la mesa del Explorador.";

        EditorUtility.DisplayDialog("Arduino Uno aplicado", msg, "OK");
    }
}
#endif

#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Rediseña Reto3_Zone para que se vea como un circuito real sobre protoboard:
/// - Reordena los 4 componentes fijos + 3 nodos de medición siguiendo la topología eléctrica REAL
///   (Batería → Resistor en serie → nodo de unión → [LED || Capacitor] en paralelo → GND → Batería).
/// - Oculta la base vieja (PCB_Board, textura de pizarra genérica) sin borrarla.
/// - Instancia una copia del protoboard de Reto 4 (Bareboard.prefab) ajustada al tamaño real del layout.
/// - Dibuja 8 cables (VRCableRenderer, el mismo renderer de arco parabólico de Reto 4, en modo estático
///   sin XRGrabInteractable/Rigidbody) uniendo cada tramo real del circuito.
///
/// Solo cambia posiciones X/Z de componentes existentes y agrega objetos nuevos (protoboard+cables);
/// NO toca nodeA/nodeB de ningún componente (la simulación eléctrica de CircuitManager no depende de
/// la posición visual) ni la posición/rotación de Reto3_Zone (el ancla del cuarto).
///
/// Menú: Tools → TITA → Reto 3 → Rediseñar como Protoboard (layout + cables + board)
/// Batch: Reto3ProtoboardLayoutTool.RedesignBatch()
/// </summary>
public static class Reto3ProtoboardLayoutTool
{
    const string ExploradorScenePath = "Assets/Scenes/Explorador.unity";
    const string BareboardGuid = "3febd7ce15b3448fbb19b0fd912112b5";

    const float MarginXZ = 0.6f;
    const float BoardThickness = 0.1f;
    const float BoardYClearance = 0.04f;
    const float WireWidth = 0.007f;

    static readonly Color WireRed = new Color(0.85f, 0.15f, 0.15f);
    static readonly Color WireOrange = new Color(0.95f, 0.6f, 0.1f);
    static readonly Color WireBlack = new Color(0.08f, 0.08f, 0.08f);

    [MenuItem("Tools/TITA/Reto 3/Rediseñar como Protoboard (layout + cables + board)")]
    public static void RedesignMenu()
    {
        RedesignBatch();
        EditorUtility.DisplayDialog("Rediseño Reto 3",
            "Listo. Revisá en el Editor: que el protoboard nuevo quede a la altura correcta bajo los " +
            "componentes, y que los 8 cables (curvas amarillas/rojas/negras) sigan el circuito real " +
            "sin atravesar piezas. La escena ya quedó guardada.", "OK");
    }

    public static void RedesignBatch()
    {
        var scene = EditorSceneManager.OpenScene(ExploradorScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError($"[Reto3Protoboard] No se pudo abrir {ExploradorScenePath}.");
            return;
        }

        var zoneGO = FindInScene(scene, "Reto3_Zone");
        if (zoneGO == null) { Debug.LogError("[Reto3Protoboard] Reto3_Zone no encontrado."); return; }
        var zone = zoneGO.transform;
        Vector3 anchorBefore = zone.localPosition;

        Transform battery = zone.Find("Battery_9V");
        Transform resistor = zone.Find("Resistor_Serie_Faulty");
        Transform led = zone.Find("LED_Paralelo");
        Transform cap = zone.Find("Capacitor_Invertido");
        Transform nodeVcc = zone.Find("Node_R3_VCC");
        Transform nodeAfterR = zone.Find("Node_R3_AfterR");
        Transform nodeGnd = zone.Find("Node_R3_GND");
        Transform pcbBoard = zone.Find("PCB_Board");

        if (battery == null || resistor == null || led == null || cap == null ||
            nodeVcc == null || nodeAfterR == null || nodeGnd == null)
        {
            Debug.LogError("[Reto3Protoboard] Faltan uno o más hijos esperados de Reto3_Zone por nombre. Abortando.");
            return;
        }

        // 1) Reordenar siguiendo la topología real (ver nodeA/nodeB verificados):
        //    Batería(+) -> Node_VCC -> Resistor -> Node_AfterR -> {LED, Capacitor en paralelo} -> Node_GND -> Batería(-)
        //    Solo X/Z; la altura (Y) de cada pieza sobre la mesa/board no se toca.
        SetXZ(battery, -0.85f, 0f);
        SetXZ(nodeVcc, -0.68f, 0f);
        SetXZ(resistor, -0.45f, 0f);
        SetXZ(nodeAfterR, -0.15f, 0f);
        SetXZ(led, 0.15f, 0.35f);
        SetXZ(cap, 0.15f, -0.35f);
        SetXZ(nodeGnd, 0.55f, 0f);

        // 2) Ocultar la base vieja (textura de pizarra genérica, no de circuito) sin borrarla — reversible.
        if (pcbBoard != null)
        {
            Undo.RecordObject(pcbBoard.gameObject, "Reto3 Redesign Hide PCB_Board");
            pcbBoard.gameObject.SetActive(false);
            EditorUtility.SetDirty(pcbBoard.gameObject);
        }

        // 3) Protoboard nueva (copia del Bareboard de Reto 4), medida por bounding box de las 7 piezas
        //    para no adivinar la escala del mesh — se calcula a partir de sus bounds locales reales.
        var items = new[] { battery, nodeVcc, resistor, nodeAfterR, led, cap, nodeGnd };
        Vector3 min = items[0].localPosition;
        Vector3 max = items[0].localPosition;
        foreach (var t in items)
        {
            min = Vector3.Min(min, t.localPosition);
            max = Vector3.Max(max, t.localPosition);
        }
        Vector3 center = (min + max) * 0.5f;
        float targetWidth = (max.x - min.x) + MarginXZ;
        float targetDepth = (max.z - min.z) + MarginXZ;

        var board = InstantiateBareboard(zone, "Reto3_Protoboard");
        if (board != null)
        {
            var mf = board.GetComponentInChildren<MeshFilter>();
            Bounds lb = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);

            Vector3 scale = new Vector3(
                lb.size.x > 0.0001f ? targetWidth / lb.size.x : 1f,
                lb.size.y > 0.0001f ? BoardThickness / lb.size.y : 1f,
                lb.size.z > 0.0001f ? targetDepth / lb.size.z : 1f);
            board.transform.localScale = scale;

            Vector3 pivotOffset = Vector3.Scale(lb.center, scale);
            float boardY = min.y - BoardYClearance;
            board.transform.localPosition = new Vector3(center.x, boardY, center.z) - pivotOffset;

            EditorUtility.SetDirty(board);
            Debug.Log($"[Reto3Protoboard] Protoboard '{board.name}' creado: escala={scale}, centro local=({center.x},{boardY},{center.z}).");
        }
        else
        {
            Debug.LogWarning("[Reto3Protoboard] No se pudo crear el protoboard nuevo (¿falta el prefab Bareboard?). Se continúa solo con el layout+cables.");
        }

        // 4) Cables (arco parabólico estático, mismo VRCableRenderer de Reto 4, sin grab/física).
        Material lineMat = GetOrCreateLineMaterial();
        var wireParentGO = new GameObject("Reto3_Cables");
        Undo.RegisterCreatedObjectUndo(wireParentGO, "Reto3 Wires Parent");
        var oldWireParent = zone.Find("Reto3_Cables");
        if (oldWireParent != null) Undo.DestroyObjectImmediate(oldWireParent.gameObject);
        wireParentGO.transform.SetParent(zone, false);

        int wires = 0;
        wires += CreateWire(wireParentGO.transform, "Wire_Bateria_VCC", battery, nodeVcc, WireRed, lineMat) ? 1 : 0;
        wires += CreateWire(wireParentGO.transform, "Wire_VCC_Resistor", nodeVcc, resistor, WireRed, lineMat) ? 1 : 0;
        wires += CreateWire(wireParentGO.transform, "Wire_Resistor_Union", resistor, nodeAfterR, WireOrange, lineMat) ? 1 : 0;
        wires += CreateWire(wireParentGO.transform, "Wire_Union_LED", nodeAfterR, led, WireOrange, lineMat) ? 1 : 0;
        wires += CreateWire(wireParentGO.transform, "Wire_Union_Capacitor", nodeAfterR, cap, WireOrange, lineMat) ? 1 : 0;
        wires += CreateWire(wireParentGO.transform, "Wire_LED_GND", led, nodeGnd, WireBlack, lineMat) ? 1 : 0;
        wires += CreateWire(wireParentGO.transform, "Wire_Capacitor_GND", cap, nodeGnd, WireBlack, lineMat) ? 1 : 0;
        wires += CreateWire(wireParentGO.transform, "Wire_GND_Bateria", nodeGnd, battery, WireBlack, lineMat) ? 1 : 0;

        Debug.Log($"[Reto3Protoboard] {wires}/8 cables creados. Reto3_Zone.localPosition antes={anchorBefore} después={zone.localPosition} (debe ser igual).");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Reto3Protoboard] Completado y escena guardada.");
    }

    static GameObject FindInScene(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
            if (t != null) return t.gameObject;
        }
        return null;
    }

    static void SetXZ(Transform t, float x, float z)
    {
        Undo.RecordObject(t, "Reto3 Redesign Layout");
        Vector3 p = t.localPosition;
        t.localPosition = new Vector3(x, p.y, z);
        EditorUtility.SetDirty(t);
    }

    static GameObject InstantiateBareboard(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        string path = AssetDatabase.GUIDToAssetPath(BareboardGuid);
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("[Reto3Protoboard] GUID de Bareboard.prefab no resuelve a ningún asset.");
            return null;
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogError($"[Reto3Protoboard] No se pudo cargar el prefab en '{path}'.");
            return null;
        }

        var instanceObj = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instanceObj == null)
        {
            Debug.LogError("[Reto3Protoboard] InstantiatePrefab devolvió null para Bareboard.");
            return null;
        }
        instanceObj.transform.SetParent(parent, false);
        instanceObj.name = name;
        Undo.RegisterCreatedObjectUndo(instanceObj, "Reto3 Protoboard");
        return instanceObj;
    }

    static bool CreateWire(Transform parent, string name, Transform a, Transform b, Color color, Material sharedLineMat)
    {
        if (a == null || b == null)
        {
            Debug.LogWarning($"[Reto3Protoboard] Cable '{name}': falta un extremo, salteado.");
            return false;
        }

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Reto3 Wire");
        go.transform.SetParent(parent, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = sharedLineMat;
        lr.startWidth = lr.endWidth = WireWidth;
        lr.numCapVertices = 4;
        lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", color);
        lr.SetPropertyBlock(mpb);

        var cable = go.AddComponent<VRCableRenderer>();
        cable.origin = a;
        cable.target = b;
        cable.segments = 16;
        cable.arcPerMeter = 0.5f;
        cable.minArc = 0.02f;
        cable.maxArc = 0.15f;
        cable.arcUpward = true;

        EditorUtility.SetDirty(go);
        return true;
    }

    static Material _lineMat;
    static Material GetOrCreateLineMaterial()
    {
        if (_lineMat != null) return _lineMat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit")
              ?? Shader.Find("Universal Render Pipeline/Lit")
              ?? Shader.Find("Sprites/Default");
        _lineMat = new Material(sh) { name = "Reto3_WireLine_Unlit" };
        return _lineMat;
    }
}
#endif

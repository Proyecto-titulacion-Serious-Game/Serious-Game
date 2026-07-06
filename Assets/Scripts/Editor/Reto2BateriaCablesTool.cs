using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ETAPA 3 (setup) del sistema de cables físicos: cablea la parte ELÉCTRICA del Reto 2.
///  1. Pone 2 hembras en la Fuente_9V: Bateria_Mas (+) y Bateria_Menos (−), cada una con su
///     ElectricalNode propio + ContactNode(explicitNode). Kinematic + allowConnectDifrentCollor.
///  2. DESACOPLA la fuente de los rieles: VoltageSource.nodeA = borne+, nodeB = borne−
///     (antes iban directo a VCC/GND → siempre energizado). Ahora hace falta cablear.
///  3. Añade ContactNode (sin nodo explícito → se resuelve por el ProtoboardSlot) a cada SlotFemale.
///  4. Añade CableElectricalBridge a cada CableFisico (crea el Jumper al enchufar 2 hembras).
///
/// Ejecutar: Tools → TITA → Reto 2 → Cables físicos - Eléctrico (batería + puentes)
///           (headless: -executeMethod Reto2BateriaCablesTool.SetupBatch)
/// </summary>
public static class Reto2BateriaCablesTool
{
    const string ScenePath    = "Assets/Scenes/Explorador.unity";
    const string BoardName    = "Protoboard_Reto2";
    const string SourceName   = "Fuente_9V";
    const string FemalePrefab = "Assets/Scripts/cables/PhysicCalbes/Prefabs/CableContactFemale.prefab";
    const string ContainerName = "CablesFisicos_Reto2";

    [MenuItem("Tools/TITA/Reto 2/Cables físicos - Eléctrico (batería + puentes)")]
    public static void SetupMenu()
    {
        var (ok, msg) = Setup();
        EditorUtility.DisplayDialog("Reto 2 — Eléctrico de cables", msg, "OK");
    }

    public static void SetupBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var (ok, msg) = Setup();
        if (ok) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
        Debug.Log("[Reto2BateriaCables] " + msg);
    }

    static (bool, string) Setup()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();

        Transform board = all.FirstOrDefault(t => t.name == BoardName);
        if (board == null) return (false, "No encontré Protoboard_Reto2.");

        Transform fuente = all.FirstOrDefault(t => t.name == SourceName)
                           ?? board.Find(SourceName);
        if (fuente == null) return (false, "No encontré Fuente_9V.");

        var vs = fuente.GetComponent<VoltageSource>();
        if (vs == null) return (false, "Fuente_9V no tiene VoltageSource.");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FemalePrefab);
        if (prefab == null) return (false, $"No cargué {FemalePrefab}.");

        // 1 + 2. Bornes de la batería + desacople de la fuente.
        ElectricalNode masNode   = CrearBorne(fuente, prefab, "Bateria_Mas",   new Vector3( 0.03f, 0f, 0f), 1); // rojo
        ElectricalNode menosNode = CrearBorne(fuente, prefab, "Bateria_Menos", new Vector3(-0.03f, 0f, 0f), 0); // blanco/negro
        vs.nodeA = masNode;
        vs.nodeB = menosNode;
        EditorUtility.SetDirty(vs);

        // 3. ContactNode en cada SlotFemale (nodo por slot).
        int femalesMarcadas = 0;
        foreach (var slot in board.GetComponentsInChildren<ProtoboardSlot>(true))
        {
            var female = slot != null ? slot.transform.Find("SlotFemale") : null;
            if (female == null) continue;
            if (female.GetComponent<ContactNode>() == null)
                female.gameObject.AddComponent<ContactNode>();   // explicitNode null → usa slot.assignedNode
            femalesMarcadas++;
        }

        // 4. CableElectricalBridge en cada CableFisico.
        // Re-consultar la escena: CrearBorne hizo DestroyImmediate de bornes viejos, que siguen en el
        // array 'all' materializado arriba → leer t.name sobre uno destruido lanzaba MissingReference.
        int cablesPuente = 0;
        Transform cont = Resources.FindObjectsOfTypeAll<Transform>()
            .FirstOrDefault(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)
                                 && t.name == ContainerName);
        if (cont != null)
            foreach (Transform cable in cont)
            {
                if (cable.GetComponent<CableElectricalBridge>() == null)
                    cable.gameObject.AddComponent<CableElectricalBridge>();
                cablesPuente++;
            }

        return (true, $"Batería con 2 bornes (+/−) y fuente desacoplada. {femalesMarcadas} SlotFemale marcadas, " +
                      $"{cablesPuente} cables con puente eléctrico. Ahora hay que cablear batería(+)→VCC y (−)→GND para energizar.");
    }

    /// <summary>Crea una hembra-borne en la batería con su ElectricalNode + ContactNode. Devuelve el nodo.</summary>
    static ElectricalNode CrearBorne(Transform fuente, GameObject prefab, string name, Vector3 localPos, int colorIndex)
    {
        var prev = fuente.Find(name);
        if (prev != null) Object.DestroyImmediate(prev.gameObject);

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, fuente);
        go.name = name;
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;

        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
            { rb.isKinematic = true; rb.useGravity = false; }

        // Connector: permitir cualquier color de cable.
        foreach (var comp in go.GetComponentsInChildren<Component>(true))
        {
            if (comp == null || comp.GetType().Name != "Connector") continue;
            var so = new SerializedObject(comp);
            var pAllow = so.FindProperty("allowConnectDifrentCollor");
            if (pAllow != null) { pAllow.boolValue = true; }
            var pCol = so.FindProperty("<ConnectionColor>k__BackingField");
            if (pCol != null) pCol.enumValueIndex = colorIndex;
            so.ApplyModifiedProperties();
        }

        var node = go.GetComponent<ElectricalNode>() ?? go.AddComponent<ElectricalNode>();
        var cn   = go.GetComponent<ContactNode>()    ?? go.AddComponent<ContactNode>();
        cn.explicitNode = node;
        return node;
    }
}

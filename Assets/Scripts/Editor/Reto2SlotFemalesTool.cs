using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ETAPA 2 (parte 1) del sistema de cables físicos: pone un CableContactFemale (Connector HEMBRA)
/// en CADA ProtoboardSlot del Reto 2, para que los cables (macho-macho) se puedan enchufar en
/// cualquier slot. Deja la hembra kinematic (fija) y con allowConnectDifrentCollor=true (para que
/// enchufe cualquier color de cable).
///
/// (El puente XR-grab y la transmisión eléctrica son las partes siguientes.)
///
/// Ejecutar: Tools → TITA → Reto 2 → Cables físicos - Hembras en slots
///           (headless: -executeMethod Reto2SlotFemalesTool.SetupBatch)
/// </summary>
public static class Reto2SlotFemalesTool
{
    const string ScenePath    = "Assets/Scenes/Explorador.unity";
    const string BoardName    = "Protoboard_Reto2";
    const string FemalePrefab = "Assets/Scripts/cables/PhysicCalbes/Prefabs/CableContactFemale.prefab";
    const string FemaleName   = "SlotFemale";

    [MenuItem("Tools/TITA/Reto 2/Cables físicos - Hembras en slots")]
    public static void SetupMenu()
    {
        int n = Setup();
        EditorUtility.DisplayDialog("Reto 2 — Hembras en slots",
            n > 0 ? $"{n} conectores hembra (CableContactFemale) puestos en los slots. Los cables macho ya pueden enchufarse. " +
                    "(Falta el puente XR y la transmisión eléctrica.)"
                  : "No se pudo (¿falta Protoboard_Reto2 o el prefab CableContactFemale?). Revisa la consola.", "OK");
    }

    public static void SetupBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int n = Setup();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(n > 0 ? $"[Reto2SlotFemales] {n} hembras puestas en slots y guardado."
                        : "[Reto2SlotFemales] FALLÓ.");
    }

    static int Setup()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        Transform board = all.FirstOrDefault(t => t.name == BoardName);
        if (board == null) { Debug.LogError("[Reto2SlotFemales] No encontré Protoboard_Reto2."); return 0; }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FemalePrefab);
        if (prefab == null) { Debug.LogError($"[Reto2SlotFemales] No cargué {FemalePrefab}."); return 0; }

        var slots = board.GetComponentsInChildren<ProtoboardSlot>(true);
        int n = 0;
        foreach (var slot in slots)
        {
            if (slot == null) continue;

            // Idempotente: borrar una hembra previa de este slot.
            var prev = slot.transform.Find(FemaleName);
            if (prev != null) Object.DestroyImmediate(prev.gameObject);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slot.transform);
            go.name = FemaleName;
            go.transform.localPosition = Vector3.zero;   // sobre el hueco del slot
            go.transform.localRotation = Quaternion.identity;

            // Rigidbody kinematic (la hembra no se mueve).
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
                { rb.isKinematic = true; rb.useGravity = false; }

            // Connector: permitir enchufar cualquier color (allowConnectDifrentCollor).
            foreach (var comp in go.GetComponentsInChildren<Component>(true))
            {
                if (comp == null || comp.GetType().Name != "Connector") continue;
                var so = new SerializedObject(comp);
                var pAllow = so.FindProperty("allowConnectDifrentCollor");
                if (pAllow != null) { pAllow.boolValue = true; so.ApplyModifiedProperties(); }
            }
            n++;
        }

        Debug.Log($"[Reto2SlotFemales] {n} hembras '{FemaleName}' puestas en los slots de Protoboard_Reto2.");
        return n;
    }
}

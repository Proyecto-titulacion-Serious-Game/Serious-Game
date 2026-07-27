#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// BUG REAL 2026-07-25 (reportado por el usuario: "el multímetro no responde al conectar cables a
/// los slots"): la gran mayoría de los <see cref="ProtoboardSlot"/> de la escena NO tienen NINGÚN
/// Collider. Verificado a mano contra Explorador.unity: de 52 ProtoboardSlot, solo 8 (Slot_GND_C_0..3
/// y Slot_GND_D_0..3) tienen un MeshCollider no-trigger; los otros 44 —TODO Protoboard_Reto2 y el
/// grueso de la matriz de Reto 4— son instancias "peladas" del modelo Cube.fbx (Samples XR
/// Interaction Toolkit) con el script ProtoboardSlot agregado encima (m_AddedComponents) pero SIN
/// Collider propio, o primitivas creadas a mano sin collider.
///
/// Esto importa porque <see cref="MultimeterProbe"/> (la punta física roja/negra del multímetro,
/// activa en los 4 Reto_Zone vía el prefab Multimeter_Panel_Art) YA SABE leer
/// <see cref="ProtoboardSlot.assignedNode"/> correctamente en su OnTriggerEnter — el camino de
/// medición por contacto físico existe y está bien escrito. Pero sin Collider en el slot, Physics
/// nunca genera el OnTriggerEnter contra la punta (trigger) del multímetro, así que el multímetro
/// se queda "sin contacto" sin importar qué tan bien esté cableado el circuito. No es un problema
/// de NodeInteractable ni de lógica de asignación de nodos — es, literalmente, que no hay geometría
/// de colisión que tocar.
///
/// Este fix replica EXACTAMENTE la receta que ya usan los 8 slots que sí funcionan (MeshCollider
/// no-trigger, no-convexo, usando el mismo mesh del MeshFilter existente) en todos los
/// ProtoboardSlot que aún no tengan Collider — sin tocar NodeInteractable ni el resto del sistema.
///
/// Tools → TITA → Reto 4 → Agregar colliders a ProtoboardSlots sin collider
/// </summary>
public static class ProtoboardSlotColliderFix
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 4/Agregar colliders a ProtoboardSlots sin collider")]
    public static void Run()
    {
        int added = Aplicar();
        int total = Object.FindObjectsByType<ProtoboardSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        Debug.Log($"[ProtoboardSlotColliderFix] Colliders agregados: {added} (de {total} ProtoboardSlot totales en la escena abierta). " +
                  "Guarda la escena (Ctrl+S) para persistir el cambio.");
    }

    /// <summary>Versión headless: abre Explorador.unity, aplica el fix, GUARDA y sale.
    /// Uso: Unity.exe -batchmode -quit -projectPath . -executeMethod ProtoboardSlotColliderFix.RunBatch</summary>
    public static void RunBatch()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int added = Aplicar();
        int total = Object.FindObjectsByType<ProtoboardSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        Debug.Log($"[ProtoboardSlotColliderFix] (batch) Colliders agregados: {added} (de {total} ProtoboardSlot totales).");
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[ProtoboardSlotColliderFix] (batch) Escena guardada.");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static int Aplicar()
    {
        int count = 0;
        var slots = Object.FindObjectsByType<ProtoboardSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var slot in slots)
        {
            if (slot == null) continue;
            if (slot.GetComponent<Collider>() != null) continue; // ya tiene uno (p.ej. los 8 GND_C/GND_D)

            var mf = slot.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
            {
                Debug.LogWarning($"[ProtoboardSlotColliderFix] '{slot.name}' no tiene MeshFilter/mesh — " +
                                  "no se puede agregar un MeshCollider automático. Revisar a mano.", slot);
                continue;
            }

            var col = Undo.AddComponent<MeshCollider>(slot.gameObject);
            col.sharedMesh = mf.sharedMesh;
            col.convex     = false;   // estático, no-trigger: no necesita ser convexo (igual que los 8 de referencia)
            col.isTrigger  = false;   // MultimeterProbeContact/MultimeterProbe exigen NO-trigger del lado del slot
            EditorUtility.SetDirty(slot.gameObject);
            count++;
        }
        return count;
    }
}
#endif

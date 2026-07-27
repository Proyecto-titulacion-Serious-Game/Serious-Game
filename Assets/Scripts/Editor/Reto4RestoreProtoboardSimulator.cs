using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// BUG REAL encontrado 2026-07-24 (reportado: "el Reto 4 no se completó porque no funcionaban los
/// slots (GND) del protoboard" en una sesión real de 2 máquinas): el GameObject "Bareboard" del
/// Reto 4 (GameZones/Reto4_Zone/Reto4_TiltGroup/Bareboard) tiene sus 32 ProtoboardSlot intactos
/// (incluidos los 8 de GND) bajo "[ProtoboardSlots]", pero el componente ProtoboardSimulator que
/// los simula/agrupa por railId NO ESTÁ — se perdió en algún punto (probablemente el mismo refactor
/// incompleto que rompió ArduinoCore.cs). Sin ProtoboardSimulator, NINGÚN slot de este board hace
/// nada eléctricamente: ni GND, ni VCC, ni las columnas — coincide exactamente con el síntoma
/// reportado ("ningún GND servía", no solo alguno puntual).
///
/// Confirmado por Reto4SlotsGndAudit.cs, que YA esperaba encontrar el ProtoboardSimulator en esa
/// ruta exacta (prueba de que existía cuando se escribió esa auditoría).
///
/// Este fix: agrega el componente a "Bareboard" y lo rellena con todos los ProtoboardSlot
/// encontrados debajo — igual que la config real del Reto 2 (Bareboard/Protoboard_Reto2).
///
/// Menú: Tools → TITA → Reto 4 → Fix — restaurar ProtoboardSimulator del Bareboard (headless-safe)
/// </summary>
public static class Reto4RestoreProtoboardSimulator
{
    const string ScenePath     = "Assets/Scenes/Explorador.unity";
    const string BareboardPath = "GameZones/Reto4_Zone/Reto4_TiltGroup/Bareboard";

    [MenuItem("Tools/TITA/Reto 4/Fix — restaurar ProtoboardSimulator del Bareboard (headless-safe)")]
    public static void Apply()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform bareboard = null;
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t.name == "Bareboard" && t.gameObject.scene.IsValid() && GetPath(t) == BareboardPath)
            { bareboard = t; break; }
        }

        if (bareboard == null)
        {
            Debug.LogError($"[Reto4RestoreProtoboardSimulator] No encontré '{BareboardPath}'.");
            Finish(1); return;
        }

        var sim = bareboard.GetComponent<ProtoboardSimulator>();
        bool yaExistia = sim != null;
        if (!yaExistia) sim = Undo.AddComponent<ProtoboardSimulator>(bareboard.gameObject);

        var slots = bareboard.GetComponentsInChildren<ProtoboardSlot>(true).ToList();
        sim.todosLosSlots = slots;

        int gndCount = slots.Count(s => s.railId == "GND");
        Debug.Log($"[Reto4RestoreProtoboardSimulator] {(yaExistia ? "Ya existía" : "AGREGADO")} ProtoboardSimulator en '{BareboardPath}'. " +
                  $"Slots asignados: {slots.Count} (GND: {gndCount}).");

        // Reconectar GameManager.protoSim si apunta a otro/ninguno — mismo patrón que Reto4AutoSetup.
        var gm = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (gm != null)
        {
            var so   = new SerializedObject(gm);
            var prop = so.FindProperty("protoSim");
            if (prop != null && prop.objectReferenceValue != (Object)sim)
            {
                prop.objectReferenceValue = sim;
                so.ApplyModifiedProperties();
                Debug.Log("[Reto4RestoreProtoboardSimulator] GameManager.protoSim reconectado al Bareboard restaurado.");
            }
        }

        EditorUtility.SetDirty(bareboard.gameObject);
        EditorSceneManager.MarkSceneDirty(bareboard.gameObject.scene);
        EditorSceneManager.SaveScene(bareboard.gameObject.scene);

        Debug.Log($"[Reto4RestoreProtoboardSimulator] ✓ Listo. slots={slots.Count} gnd={gndCount}");
        Finish(slots.Count > 0 && gndCount > 0 ? 0 : 1);
    }

    static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }

    static void Finish(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }
}

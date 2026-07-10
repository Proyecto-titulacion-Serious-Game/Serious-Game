using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Coloca el NPC tutorial (RobotKyle + TutorialNPC con globo de texto) en la escena del Técnico.
///
/// Uso normal: abrir Tecnico.unity → Tools → TITA → Técnico → Setup NPC Tutorial (RobotKyle).
/// Batch (Unity cerrado):
///   Unity.exe -quit -batchmode -projectPath ... -executeMethod TutorialNPCSetupTool.SetupBatch
///
/// El NPC se coloca cerca de la cámara del Técnico (a ~2 m al frente, mirándola). Muévelo
/// libremente después — el setup no lo re-posiciona si ya existe 'NPC_Tutorial'.
/// La HISTORIA y las guías por reto se editan en el Inspector del componente TutorialNPC.
/// </summary>
public static class TutorialNPCSetupTool
{
    const string ScenePath  = "Assets/Scenes/Tecnico/Tecnico.unity";
    const string PrefabPath = "Assets/UnityTechnologies/SpaceRobotKyle/Prefabs/RobotKyle.prefab";

    [MenuItem("Tools/TITA/Tecnico/Setup NPC Tutorial (RobotKyle)")]
    public static void SetupMenu()
    {
        if (Setup())
            EditorSceneManager.MarkAllScenesDirty();
    }

    /// <summary>Versión headless: abre la escena del Técnico, coloca el NPC y guarda.</summary>
    public static void SetupBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (Setup())
            EditorSceneManager.SaveScene(scene);
    }

    static bool Setup()
    {
        // Idempotente: si ya existe, solo garantizar el componente (respetando posición del autor).
        var existente = GameObject.Find("NPC_Tutorial");
        if (existente != null)
        {
            if (existente.GetComponent<TutorialNPC>() == null)
                existente.AddComponent<TutorialNPC>();
            Debug.Log("[TutorialNPCSetup] 'NPC_Tutorial' ya existe — componente verificado, posición intacta.");
            return true;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[TutorialNPCSetup] No se encontró el prefab en {PrefabPath}.");
            return false;
        }

        var npc = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        npc.name = "NPC_Tutorial";

        // Posición: ~2 m frente a la cámara del Técnico, al nivel del suelo, mirándola.
        var cam = EncontrarCamara();
        if (cam != null)
        {
            Vector3 fwd = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 pos = cam.transform.position + fwd * 2.2f;
            pos.y = cam.transform.position.y - 1.6f;   // altura de ojos → suelo aprox.
            npc.transform.position = pos;
            npc.transform.rotation = Quaternion.LookRotation(-fwd);
        }
        else
        {
            npc.transform.position = Vector3.zero;
            Debug.LogWarning("[TutorialNPCSetup] Sin cámara en la escena — NPC en el origen; muévelo a mano.");
        }

        npc.AddComponent<TutorialNPC>();
        Debug.Log($"[TutorialNPCSetup] ✓ NPC tutorial creado en {npc.transform.position}. " +
                  "Edita la HISTORIA y las guías en el Inspector (TutorialNPC). Teclas: N=página, H=mostrar/ocultar.");
        return true;
    }

    static Camera EncontrarCamara()
    {
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (c.isActiveAndEnabled) return c;
        return null;
    }
}

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Agrega CableSelfCollisionOff (ya usado en el Reto 2 para el mismo problema) a la raíz del
/// multímetro: los colliders del cuerpo y de las 2 puntas viven en Rigidbodies SEPARADOS dentro de
/// la MISMA jerarquía — Unity no los ignora entre sí solo por estar emparentados. Al agarrar y mover
/// el cuerpo (kinematic, sigue la mano rápido), arrastra a las puntas con él y sus colliders chocan
/// en cada frame → empujón violento al jugador (bug reportado tras el fix de posición).
///
/// El SpringJoint ya tenía enableCollision=false, pero eso solo cubre el PAR punta↔cuerpo conectado
/// por el joint — no cubre punta-roja↔punta-negra ni punta↔botón de modo. CableSelfCollisionOff
/// ignora TODOS los pares dentro de la jerarquía, de una vez.
/// </summary>
public static class MultimeterSelfCollisionFix
{
    const string PREFAB_PATH = "Assets/Prefabs/Multimeter_VR_Art.prefab";

    [MenuItem("Tools/TITA/Multímetro/Fix — apagar auto-colisión cuerpo/puntas (headless-safe)")]
    public static void Apply()
    {
        var go = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
        if (go == null) { Debug.LogError($"[MultimeterSelfCollisionFix] No se pudo cargar {PREFAB_PATH}"); return; }

        var existing = go.GetComponent<CableSelfCollisionOff>();
        if (existing == null)
        {
            go.AddComponent<CableSelfCollisionOff>();
            Debug.Log("[MultimeterSelfCollisionFix] CableSelfCollisionOff agregado a la raíz del multímetro.");
        }
        else
        {
            Debug.Log("[MultimeterSelfCollisionFix] Ya estaba agregado — sin cambios.");
        }

        PrefabUtility.SaveAsPrefabAsset(go, PREFAB_PATH);
        PrefabUtility.UnloadPrefabContents(go);
        AssetDatabase.Refresh();

        Debug.Log("[MultimeterSelfCollisionFix] ✓ Listo.");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
#endif

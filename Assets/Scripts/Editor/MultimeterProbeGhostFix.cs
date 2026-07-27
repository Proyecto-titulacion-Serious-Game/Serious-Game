#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Convierte el ÚNICO collider no-trigger de cada punta (el de "agarre") en trigger también.
/// Antes de esto, la punta tenía un SphereCollider físico real (isTrigger=false) — con Rigidbody
/// kinematic, al moverse rápido junto al cuerpo (VR arrastra la mano rápido) ese collider físico
/// puede empujar CUALQUIER otra cosa que toque (el propio jugador, su CharacterController, etc.),
/// sin importar qué pares se hayan marcado a ignorar entre sí (CableSelfCollisionOff solo cubre la
/// jerarquía propia del multímetro, no cubre choques contra el jugador).
///
/// XRGrabInteractable NO necesita un collider físico para detectar el agarre — sus queries de
/// proximidad/agarre (Physics.OverlapSphere/Box) incluyen triggers igual. Con AMBOS colliders de la
/// punta en trigger, la punta queda completamente "fantasma": nunca empuja ni es empujada por nada.
/// </summary>
public static class MultimeterProbeGhostFix
{
    const string PREFAB_PATH = "Assets/Prefabs/Multimeter_VR_Art.prefab";

    [MenuItem("Tools/TITA/Multímetro/Fix — puntas 100% trigger, sin física de empuje (headless-safe)")]
    public static void Apply()
    {
        var go = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
        if (go == null) { Debug.LogError($"[MultimeterProbeGhostFix] No se pudo cargar {PREFAB_PATH}"); return; }

        // El cuerpo del multímetro (BoxCollider raíz) también: es kinematic + lo agarra la mano
        // directamente — sostenido cerca del jugador (para mirar la pantalla) puede chocar contra su
        // propio CharacterController igual que la punta. Mismo fix, misma razón.
        var bodyCol = go.GetComponent<BoxCollider>();
        if (bodyCol != null && !bodyCol.isTrigger)
        {
            bodyCol.isTrigger = true;
            Debug.Log("[MultimeterProbeGhostFix] Cuerpo del multímetro: collider convertido a trigger.");
        }

        int hechas = 0;

        foreach (var colorName in new[] { "Red", "Black" })
        {
            var cableT = go.transform.Find($"Cable_{colorName}");
            var probeT = cableT != null ? cableT.Find($"Probe_{colorName}_Tip") : null;
            if (probeT == null)
            {
                Debug.LogWarning($"[MultimeterProbeGhostFix] No se encontró Probe_{colorName}_Tip.");
                continue;
            }

            int convertidos = 0;
            foreach (var col in probeT.GetComponents<SphereCollider>())
            {
                if (!col.isTrigger)
                {
                    col.isTrigger = true;
                    convertidos++;
                }
            }

            hechas++;
            Debug.Log($"[MultimeterProbeGhostFix] '{colorName}': {convertidos} collider(es) convertidos a trigger.");
        }

        PrefabUtility.SaveAsPrefabAsset(go, PREFAB_PATH);
        PrefabUtility.UnloadPrefabContents(go);
        AssetDatabase.Refresh();

        Debug.Log($"[MultimeterProbeGhostFix] ✓ Listo. Puntas procesadas={hechas}");
        if (Application.isBatchMode) EditorApplication.Exit(hechas == 2 ? 0 : 1);
    }
}
#endif

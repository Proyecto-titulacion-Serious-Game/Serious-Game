#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Revierte el fix "todo trigger" (rompió el agarre de las puntas con XRGrabInteractable en este
/// proyecto) y en su lugar agrega MultimeterIgnorePlayerCollision — ignora la colisión puntual
/// contra el CharacterController del jugador sin tocar capas de física ni el tipo de collider.
/// </summary>
public static class MultimeterUngrabbableFix
{
    const string PREFAB_PATH = "Assets/Prefabs/Multimeter_VR_Art.prefab";

    [MenuItem("Tools/TITA/Multímetro/Fix — revertir triggers, ignorar colisión contra jugador (headless-safe)")]
    public static void Apply()
    {
        var go = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
        if (go == null) { Debug.LogError($"[MultimeterUngrabbableFix] No se pudo cargar {PREFAB_PATH}"); return; }

        var bodyCol = go.GetComponent<BoxCollider>();
        if (bodyCol != null && bodyCol.isTrigger)
        {
            bodyCol.isTrigger = false;
            Debug.Log("[MultimeterUngrabbableFix] Cuerpo: collider revertido a físico (no-trigger).");
        }

        int revertidas = 0;
        foreach (var colorName in new[] { "Red", "Black" })
        {
            var cableT = go.transform.Find($"Cable_{colorName}");
            var probeT = cableT != null ? cableT.Find($"Probe_{colorName}_Tip") : null;
            if (probeT == null) continue;

            foreach (var col in probeT.GetComponents<SphereCollider>())
            {
                // El de agarre es el de radio chico (0.9); el de detección/contacto es el de radio
                // grande (1.1+). Revertimos SOLO el de agarre — el de contacto sigue trigger (correcto).
                if (col.isTrigger && col.radius < 1f)
                {
                    col.isTrigger = false;
                    revertidas++;
                }
            }
        }

        if (go.GetComponent<MultimeterIgnorePlayerCollision>() == null)
        {
            go.AddComponent<MultimeterIgnorePlayerCollision>();
            Debug.Log("[MultimeterUngrabbableFix] MultimeterIgnorePlayerCollision agregado a la raíz.");
        }

        PrefabUtility.SaveAsPrefabAsset(go, PREFAB_PATH);
        PrefabUtility.UnloadPrefabContents(go);
        AssetDatabase.Refresh();

        Debug.Log($"[MultimeterUngrabbableFix] ✓ Listo. Colliders de agarre revertidos={revertidas}");
        if (Application.isBatchMode) EditorApplication.Exit(revertidas == 2 ? 0 : 1);
    }
}
#endif

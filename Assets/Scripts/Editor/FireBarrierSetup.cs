using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Coloca BARRERAS DE FUEGO que bloquean la entrada a las zonas de reto hasta completar el
/// reto anterior. Reto 2 y Reto 3 (el Reto 4 NO se bloquea).
///
///   Tools → TITA → Explorador → Setup Barreras de Fuego   (crea/actualiza)
///   Tools → TITA → Explorador → Quitar Barreras de Fuego   (limpia)
///
/// Usa la pared de fuego de CartoonFX (CFXR2 Firewall A) + un BoxCollider sólido. Tras
/// crearlas, REPOSICIONÁ cada 'FireBarrier_*' en la entrada/pasillo real de su zona (el tool
/// las deja sobre el centro de la zona como punto de partida). Lo maneja en runtime
/// <see cref="RetoFireBarrier"/>.
/// </summary>
public static class FireBarrierSetup
{
    const string FirewallGuid = "2af5562ce69fc2d448481a1587a352a5"; // CFXR2 Firewall A

    [MenuItem("Tools/TITA/Explorador/Setup Barreras de Fuego")]
    static void Setup()
    {
        var firewall = AssetDatabase.LoadAssetAtPath<GameObject>(
            AssetDatabase.GUIDToAssetPath(FirewallGuid));
        if (firewall == null)
        {
            EditorUtility.DisplayDialog("Barreras de Fuego",
                "No encontré la prefab CFXR2 Firewall A (CartoonFX). ¿Está el paquete importado?", "Cerrar");
            return;
        }

        int n = 0;
        n += CrearBarrera("Reto2_Zone", LevelType.OhmLaw,   firewall) ? 1 : 0;
        n += CrearBarrera("Reto3_Zone", LevelType.Parallel, firewall) ? 1 : 0;

        if (n > 0)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("Barreras de Fuego",
                $"{n} barrera(s) creada(s)/actualizada(s) en Reto 2 y Reto 3.\n\n" +
                "IMPORTANTE: reposicioná cada 'FireBarrier_*' en la entrada real de su zona " +
                "(quedaron sobre el centro de la zona). Guardá la escena.", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Barreras de Fuego",
                "No encontré Reto2_Zone / Reto3_Zone en la escena abierta. " +
                "Abrí Explorador.unity y reintentá.", "Cerrar");
        }
    }

    [MenuItem("Tools/TITA/Explorador/Quitar Barreras de Fuego")]
    static void Quitar()
    {
        int n = 0;
        foreach (var b in Object.FindObjectsByType<RetoFireBarrier>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(b.gameObject);
            n++;
        }
        if (n > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Barreras de Fuego", $"{n} barrera(s) eliminada(s).", "OK");
    }

    static bool CrearBarrera(string zonaName, LevelType retoPrevio, GameObject firewall)
    {
        Transform zona = FindByName(zonaName);
        if (zona == null) return false;

        string barreraName = $"FireBarrier_{zonaName}";

        // Reemplazar si ya existe (re-ejecución idempotente).
        var prev = FindByName(barreraName);
        if (prev != null) Object.DestroyImmediate(prev.gameObject);

        var go = new GameObject(barreraName);
        Undo.RegisterCreatedObjectUndo(go, "Crear barrera de fuego");
        go.transform.SetParent(zona.parent != null ? zona.parent : null, false);
        go.transform.position = zona.position;

        // Collider sólido que bloquea el paso (no trigger).
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.size      = new Vector3(4f, 3f, 0.6f);   // pared ~4m ancho × 3m alto
        box.center    = new Vector3(0f, 1.5f, 0f);

        // VFX de fuego como hijo.
        var vfx = (GameObject)PrefabUtility.InstantiatePrefab(firewall);
        vfx.transform.SetParent(go.transform, false);
        vfx.transform.localPosition = Vector3.zero;

        // Componente que la apaga al completar el reto previo.
        var barrier = go.AddComponent<RetoFireBarrier>();
        barrier.retoPrevioACompletar = retoPrevio;
        barrier.fuegoVFX        = vfx;
        barrier.barreraCollider = box;

        Debug.Log($"[FireBarrierSetup] '{barreraName}' creada en {zona.position} " +
                  $"(se apaga al completar {retoPrevio}). Reposicionar en la entrada real.");
        return true;
    }

    static Transform FindByName(string name)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name) return t;
        return null;
    }
}

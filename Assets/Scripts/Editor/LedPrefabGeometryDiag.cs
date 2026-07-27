using UnityEditor;
using UnityEngine;

/// <summary>
/// DIAGNÓSTICO: compara la geometría runtime de los 3 prefabs Delivered_LED_* (bounds del mesh,
/// posición de las patas auto-creadas por ProtoboardConnector, colliders). Si el mesh de un color
/// tiene otro pivote/tamaño, sus patas no alcanzan los slots en el snap físico del Reto 4 y ese
/// color "no funciona" aunque toda la lógica sea agnóstica al color.
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod LedPrefabGeometryDiag.Run -logFile
/// </summary>
public static class LedPrefabGeometryDiag
{
    static readonly string[] Paths =
    {
        "Assets/Prefabs/Delivered/Delivered_LED.prefab",
        "Assets/Prefabs/Delivered/Delivered_LED_Red.prefab",
        "Assets/Prefabs/Delivered/Delivered_LED_Green.prefab",
        "Assets/Prefabs/Delivered/Delivered_LED_Yellow.prefab",
    };

    [MenuItem("Tools/TITA/Diag/Geometria prefabs LED (headless)")]
    public static void Run()
    {
        foreach (var path in Paths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogError($"##GEO## {path} NO EXISTE"); continue; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;

            var rend = go.GetComponentInChildren<Renderer>(true);
            var mf   = go.GetComponentInChildren<MeshFilter>(true);
            var col  = go.GetComponentInChildren<Collider>(true);

            string meshInfo = mf != null && mf.sharedMesh != null
                ? $"mesh='{mf.sharedMesh.name}' bounds.center={mf.sharedMesh.bounds.center:F4} bounds.size={mf.sharedMesh.bounds.size:F4}"
                : "SIN MESH";
            string rendInfo = rend != null
                ? $"rend.bounds.center={rend.bounds.center:F4} rend.bounds.size={rend.bounds.size:F4} rend.enabled={rend.enabled}"
                : "SIN RENDERER";
            string colInfo = col is BoxCollider bc
                ? $"box.center={bc.center:F4} box.size={bc.size:F4}"
                : (col != null ? col.GetType().Name : "SIN COLLIDER");

            // Forzar la creación de patas real (misma ruta runtime: EnsureOn + Awake→EnsureLeads)
            var conn = ProtoboardConnector.EnsureOn(go);
            string leads = "SIN CONNECTOR";
            if (conn != null)
            {
                conn.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
                leads = $"leadA={(conn.leadA != null ? conn.leadA.position.ToString("F4") : "null")} " +
                        $"leadB={(conn.leadB != null ? conn.leadB.position.ToString("F4") : "null")}";
            }

            var led = go.GetComponentInChildren<LED>(true);
            string ledInfo = led != null
                ? $"R={led.resistance} Vf={led.forwardVoltage} inv={led.polarityInverted} maxSafe={led.maxSafeCurrent} overload={led.overloadCurrent}"
                : "SIN LED";

            Debug.Log($"##GEO## {System.IO.Path.GetFileNameWithoutExtension(path)}\n" +
                      $"  escala={go.transform.localScale:F4}\n  {meshInfo}\n  {rendInfo}\n  {colInfo}\n  {leads}\n  {ledInfo}");

            Object.DestroyImmediate(go);
        }

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}

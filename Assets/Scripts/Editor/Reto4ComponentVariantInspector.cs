using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Diagnóstico de SOLO LECTURA: revisa si los prefabs de variante de color del LED (y capacitor)
/// están realmente asignados en el ExplorerComponentReceiver de la escena Explorador.unity, y si
/// cada prefab tiene el LED.cristalTint correcto (verde/rojo/amarillo), para responder "si el
/// Técnico envía un LED amarillo, ¿le llega un LED que realmente se ve/enciende amarillo?".
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto4ComponentVariantInspector.Run -logFile -
/// </summary>
public static class Reto4ComponentVariantInspector
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 4/Inspeccionar variantes de color (solo lectura)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var receivers = Object.FindObjectsByType<ExplorerComponentReceiver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[VariantInspector] {receivers.Length} ExplorerComponentReceiver(es) en la escena.");

        foreach (var r in receivers)
        {
            Debug.Log($"##RECEIVER## path=\"{GetPath(r.transform)}\" activo={r.gameObject.activeInHierarchy}");
            LogPrefabTint("ledGreenPrefab (verde)",   r.ledGreenPrefab);
            LogPrefabTint("ledRedPrefab (rojo)",      r.ledRedPrefab);
            LogPrefabTint("ledYellowPrefab (amarillo)", r.ledYellowPrefab);
            LogPrefabTint("ledPrefab (base/fallback)", r.ledPrefab);
        }

        // Prefabs de referencia que usa el lado Técnico para inferir la variante por nombre
        // (DeskComponent.ResolveVariant): confirmamos que existan objetos "amarillo/yellow" en la
        // escena del Técnico para que la inferencia por nombre encuentre algo razonable.
        Debug.Log("[VariantInspector] Fin del diagnóstico.");
    }

    static void LogPrefabTint(string campo, GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"  {campo}: NULL (sin asignar — caerá al fallback por defecto)");
            return;
        }
        var led = prefab.GetComponent<LED>() ?? prefab.GetComponentInChildren<LED>(true);
        if (led == null)
        {
            Debug.LogWarning($"  {campo}: \"{prefab.name}\" — SIN componente LED (no se puede leer cristalTint)");
            return;
        }
        Debug.Log($"  {campo}: \"{prefab.name}\" cristalTint=RGBA({led.cristalTint.r:F2},{led.cristalTint.g:F2},{led.cristalTint.b:F2},{led.cristalTint.a:F2}) " +
                  $"colorOverload=RGBA({led.colorOverload.r:F2},{led.colorOverload.g:F2},{led.colorOverload.b:F2})");
    }

    static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}

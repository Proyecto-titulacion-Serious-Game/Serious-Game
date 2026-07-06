using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Crea un material de CRISTAL transparente (URP Lit) y lo asigna a todos los LED (prefabs + escena),
/// para que se vean como un LED real de plástico claro: transparente/glossy apagado, y brillando de
/// color cuando encienden (por emisión, que la controla <see cref="LED"/> con MaterialPropertyBlock).
///
/// Ejecutar: Tools → TITA → LED Cristal - Aplicar material transparente
/// </summary>
public static class LEDCristalSetup
{
    // En Resources para que LED.cs lo cargue en runtime (Resources.Load<Material>("LED_Cristal")).
    const string MatPath = "Assets/Resources/LED_Cristal.mat";

    [MenuItem("Tools/TITA/LED Cristal - Aplicar material transparente")]
    public static void AplicarCristal()
    {
        var mat = CrearMaterialCristal();
        if (mat == null) { EditorUtility.DisplayDialog("LED Cristal", "No pude crear el material (¿falta URP/Lit?).", "OK"); return; }

        // 1. Prefabs con LED (buscamos por nombre "LED" bajo Assets/Prefabs para no cargar todo).
        int prefabs = 0, rends = 0;
        foreach (var guid in AssetDatabase.FindAssets("LED t:Prefab", new[] { "Assets/Prefabs" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var go = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;
            Color tint = TintDeNombre(System.IO.Path.GetFileNameWithoutExtension(path));
            foreach (var led in go.GetComponentsInChildren<LED>(true))
            {
                led.cristalTint = tint;   // color de cristal por variante (verde/rojo/amarillo)
                foreach (var r in led.GetComponentsInChildren<Renderer>(true))
                    if (r != null && !(r is ParticleSystemRenderer)) { r.sharedMaterial = mat; rends++; changed = true; }
            }
            if (changed) { PrefabUtility.SaveAsPrefabAsset(go, path); prefabs++; }
            PrefabUtility.UnloadPrefabContents(go);
        }

        // 2. Instancias de LED en la escena abierta.
        int scn = 0;
        foreach (var led in Object.FindObjectsByType<LED>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            led.cristalTint = TintDeNombre(led.name);
            EditorUtility.SetDirty(led);
            foreach (var r in led.GetComponentsInChildren<Renderer>(true))
                if (r != null && !(r is ParticleSystemRenderer)) { r.sharedMaterial = mat; EditorUtility.SetDirty(r); scn++; }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[LEDCristal] Material cristal aplicado: {prefabs} prefabs ({rends} renderers) + {scn} renderers en escena.");
        EditorUtility.DisplayDialog("LED Cristal",
            $"Material transparente aplicado a {prefabs} prefabs de LED + {scn} en la escena.\n" +
            "Apagado = cristal claro · Encendido = brillo de color (emisión).", "OK");
    }

    /// <summary>Color de cristal según el nombre del LED/variante (green/red/yellow/blue). Neutro si no hay color.</summary>
    static Color TintDeNombre(string n)
    {
        n = (n ?? "").ToLowerInvariant();
        if (n.Contains("green")  || n.Contains("verde"))  return new Color(0.30f, 1.00f, 0.40f, 1f);
        if (n.Contains("red")    || n.Contains("roj"))    return new Color(1.00f, 0.28f, 0.24f, 1f);
        if (n.Contains("yellow") || n.Contains("amaril")) return new Color(1.00f, 0.88f, 0.30f, 1f);
        if (n.Contains("blue")   || n.Contains("azul"))   return new Color(0.35f, 0.60f, 1.00f, 1f);
        return new Color(0.30f, 1.00f, 0.40f, 1f);   // verde por defecto (LED típico / led_ok)
    }

    static Material CrearMaterialCristal()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (existing != null) return existing;

        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) return null;

        var m = new Material(sh) { name = "LED_Cristal" };
        // Superficie TRANSPARENTE (alpha blend, sin ZWrite).
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        // Plástico translúcido glossy (como masterTex, _Glossiness 0.5). El color lo pone cada LED.
        m.SetColor("_BaseColor", new Color(0.6f, 0.8f, 0.6f, 0.7f));
        m.SetFloat("_Smoothness", 0.6f);
        m.SetFloat("_Metallic", 0f);
        // Emisión activa (el color/brillo lo pone cada LED por MaterialPropertyBlock).
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetColor("_EmissionColor", Color.black);

        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        AssetDatabase.CreateAsset(m, MatPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[LEDCristal] Material creado en " + MatPath);
        return m;
    }
}

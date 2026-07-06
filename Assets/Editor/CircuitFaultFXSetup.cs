using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Configura el feedback de falla: añade CircuitFaultFX (rayo del asset Lightning Bolt) y asigna
/// un sonido PLACEHOLDER existente a los slots vacíos del CircuitAudioManager.
/// Menú: Tools → TITA → Reto 4 → Setup FX de Falla (rayo + sonido)
/// </summary>
public static class CircuitFaultFXSetup
{
    const string LIGHTNING_GUID = "a9892f6a83fd8d4468ce0c8a3d09d3cd"; // SimpleLightningBoltPrefab
    const string PLACEHOLDER_AUDIO_GUID = "16fba6d30ed741d4a9fdd6e79ee2f3ac"; // Button Pop.wav

    [MenuItem("Tools/TITA/Reto 4/Setup FX de Falla (rayo + sonido)")]
    public static void Setup()
    {
        var prefab = Load<GameObject>(LIGHTNING_GUID);
        var placeholder = Load<AudioClip>(PLACEHOLDER_AUDIO_GUID);

        // ── Manager de audio: asignar placeholder a slots vacíos ──
        var audio = Object.FindAnyObjectByType<CircuitAudioManager>();
        if (audio != null && placeholder != null)
        {
            Undo.RecordObject(audio, "Placeholder FX audio");
            if (audio.sfxShortCircuit == null) audio.sfxShortCircuit = placeholder;
            if (audio.sfxFault        == null) audio.sfxFault        = placeholder;
            if (audio.sfxLedBlown     == null) audio.sfxLedBlown     = placeholder;
            EditorUtility.SetDirty(audio);
        }

        // ── CircuitFaultFX (rayo): lo ponemos en el GO del audio manager o uno nuevo ──
        GameObject host = audio != null ? audio.gameObject : GameObject.Find("CircuitFX");
        if (host == null)
        {
            host = new GameObject("CircuitFX");
            Undo.RegisterCreatedObjectUndo(host, "Crear CircuitFX");
        }
        var fx = host.GetComponent<CircuitFaultFX>();
        if (fx == null) fx = Undo.AddComponent<CircuitFaultFX>(host);
        if (fx.lightningPrefab == null) fx.lightningPrefab = prefab;
        EditorUtility.SetDirty(fx);

        EditorSceneManager.MarkSceneDirty(host.scene);
        Selection.activeGameObject = host;
        EditorGUIUtility.PingObject(host);

        EditorUtility.DisplayDialog("Setup FX de Falla",
            $"Listo.\n\n" +
            $"• Rayo Lightning Bolt: {(prefab != null ? "asignado" : "PREFAB NO ENCONTRADO")} en '{host.name}'.\n" +
            $"• Sonido placeholder ({(placeholder != null ? placeholder.name : "no encontrado")}) " +
            $"asignado a los slots vacíos de CircuitAudioManager.\n\n" +
            "Dónde poner el sonido bueno: seleccioná el GameObject con CircuitAudioManager → " +
            "en el Inspector arrastrá tus .wav a 'Sfx Short Circuit', 'Sfx Fault' y 'Sfx Led Blown'.\n\n" +
            "GUARDÁ la escena (Ctrl+S).", "OK");

        Debug.Log($"[CircuitFaultFXSetup] FX listo. Rayo={(prefab!=null)}, audioMgr={(audio!=null)}, " +
                  $"placeholder={(placeholder!=null)}.", host);
    }

    static T Load<T>(string guid) where T : Object
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
    }
}

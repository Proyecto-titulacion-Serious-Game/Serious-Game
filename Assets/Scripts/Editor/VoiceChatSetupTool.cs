using UnityEngine;
using UnityEditor;
using Fusion;
using Photon.Voice.Unity;
#if FUSION_WEAVER
using Photon.Voice.Fusion;
#endif

/// <summary>
/// Genera los prefabs de chat de voz (Photon Voice 2 + Fusion) usados por ConnectionManager:
/// - Assets/Resources/VoiceSpeaker.prefab   (Speaker + AudioSource, reproduce la voz remota)
/// - Assets/Resources/FusionRunnerVoice.prefab (NetworkRunner + Recorder + FusionVoiceClient)
///
/// Recorder queda en modo Voice Detection (transmite solo al detectar voz, sin botón), para
/// Técnico y Explorador por igual — ConnectionManager es el mismo script en ambas escenas.
///
/// Falta pegar el App Id de Voice (dashboard.photonengine.com, app tipo "Voice") en el campo
/// "App Id Voice" del asset Assets/Photon/Fusion/Resources/PhotonAppSettings.asset.
/// </summary>
public static class VoiceChatSetupTool
{
    const string SPEAKER_PREFAB_PATH = "Assets/Resources/VoiceSpeaker.prefab";
    const string RUNNER_PREFAB_PATH = "Assets/Resources/FusionRunnerVoice.prefab";

    [MenuItem("Tools/TITA/Red/Configurar Chat de Voz")]
    public static void Setup()
    {
#if !FUSION_WEAVER
        const string msg = "FUSION_WEAVER no está definido — Photon Voice no encuentra Fusion 2 en este proyecto.";
        if (Application.isBatchMode) Debug.LogError("[VoiceChatSetupTool] " + msg);
        else EditorUtility.DisplayDialog("Chat de Voz", msg, "OK");
#else
        var speakerPrefab = BuildSpeakerPrefab();
        BuildRunnerPrefab(speakerPrefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var appSettings = AssetDatabase.LoadAssetAtPath<Object>(
            "Assets/Photon/Fusion/Resources/PhotonAppSettings.asset");

        string msg = "Prefabs creados:\n" + RUNNER_PREFAB_PATH + "\n" + SPEAKER_PREFAB_PATH +
            "\n\nFalta pegar el App Id de Voice en el campo 'App Id Voice' de PhotonAppSettings.asset antes de poder conectar.";

        if (Application.isBatchMode)
        {
            Debug.Log("[VoiceChatSetupTool] " + msg);
        }
        else
        {
            Selection.activeObject = appSettings;
            EditorUtility.DisplayDialog("Chat de Voz", msg, "OK");
        }
#endif
    }

#if FUSION_WEAVER
    static GameObject BuildSpeakerPrefab()
    {
        var go = new GameObject("VoiceSpeaker");
        go.AddComponent<AudioSource>();
        go.AddComponent<Speaker>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, SPEAKER_PREFAB_PATH);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static void BuildRunnerPrefab(GameObject speakerPrefab)
    {
        var go = new GameObject("[FusionRunner]");
        go.AddComponent<NetworkRunner>();

        var recorder = go.AddComponent<Recorder>();
        var recorderSO = new SerializedObject(recorder);
        // TRANSMISIÓN CONTINUA (voiceDetection=false): el VAD gateaba al Técnico — con un mic de
        // laptop bajo el umbral (0.01) no transmitía o se cortaba a mitad de frase. Para 2 jugadores
        // el ancho de banda continuo (~30 kbps) es trivial y elimina esa causa de fallo por completo.
        recorderSO.FindProperty("voiceDetection").boolValue = false;
        recorderSO.ApplyModifiedPropertiesWithoutUndo();

        var voiceClient = go.AddComponent<FusionVoiceClient>();
        voiceClient.PrimaryRecorder = recorder;
        voiceClient.SpeakerPrefab = speakerPrefab;
        var voiceClientSO = new SerializedObject(voiceClient);
        voiceClientSO.FindProperty("usePrimaryRecorder").boolValue = true;
        voiceClientSO.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(go, RUNNER_PREFAB_PATH);
        Object.DestroyImmediate(go);
    }
#endif
}

using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using Bhaptics.SDK2;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Centraliza la retroalimentación háptica del Explorador:
/// - Controladores Meta Quest (vibración de manos)
/// - Chaleco háptico bHaptics (usando evento "asdasd")
///
/// Uso desde otros scripts:
///   haptics.PlayLight();    → toque suave (conectar punta multímetro)
///   haptics.PlayMedium();   → pulso (agarrar componente)
///   haptics.PlayStrong();   → vibración fuerte (reparación exitosa)
///   haptics.PlayError();    → patrón de error (componente incorrecto)
///   haptics.PlayCurrent(0.5f); → vibración proporcional a corriente
/// </summary>
public class HapticFeedback : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Meta Quest — controladores")]
    [Range(0f, 1f)] public float lightIntensity  = 0.3f;
    [Range(0f, 1f)] public float mediumIntensity = 0.6f;
    [Range(0f, 1f)] public float strongIntensity = 1.0f;

    [Header("Chaleco háptico")]
    [Tooltip("True para que el chaleco vibre junto con los mandos.")]
    public bool chalecoEnabled = true;

    [Header("Proporcional a corriente")]
    [Tooltip("Corriente máxima (A) que equivale a vibración máxima.")]
    public float maxCurrentForHaptic = 0.1f;

    [Header("Pruebas desde Inspector")]
    [Tooltip("Valor de corriente simulado para probar PlayCurrent desde el Inspector.")]
    public float testCurrentValue = 0.05f;

    // ─────────────────────────────────────────────
    //  Dispositivos XR
    // ─────────────────────────────────────────────
    private InputDevice _leftController;
    private InputDevice _rightController;

    void Start()
    {
        RefreshDevices();
    }

    void RefreshDevices()
    {
        var leftDevices  = new System.Collections.Generic.List<InputDevice>();
        var rightDevices = new System.Collections.Generic.List<InputDevice>();

        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Left  | InputDeviceCharacteristics.Controller, leftDevices);
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, rightDevices);

        if (leftDevices.Count  > 0) _leftController  = leftDevices[0];
        if (rightDevices.Count > 0) _rightController = rightDevices[0];
    }

    // ─────────────────────────────────────────────
    //  API Pública y Pruebas
    // ─────────────────────────────────────────────

    public void PlayLight() 
    {
        Vibrate(lightIntensity, 0.1f);
        if (chalecoEnabled) SendToVest(lightIntensity);
    }

    public void PlayMedium() 
    {
        Vibrate(mediumIntensity, 0.2f);
        if (chalecoEnabled) SendToVest(mediumIntensity);
    }

    public void PlayStrong() 
    {
        Vibrate(strongIntensity, 0.4f);
        if (chalecoEnabled) SendToVest(strongIntensity);
    }

    public void PlayError()
    {
        StartCoroutine(ErrorPattern());
        if (chalecoEnabled) StartCoroutine(VestErrorPattern());
    }

    /// <summary>Doble pulso corto en el chaleco: el ERROR se siente distinto al éxito (pulso único largo).</summary>
    IEnumerator VestErrorPattern()
    {
        SendToVest(strongIntensity, 0.18f);
        yield return new WaitForSeconds(0.28f);
        SendToVest(strongIntensity, 0.18f);
    }

    public void PlayCurrent(float current)
    {
        float normalizedIntensity = Mathf.Clamp01(current / maxCurrentForHaptic);
        Vibrate(normalizedIntensity, 0.1f);

        if (chalecoEnabled) SendToVest(normalizedIntensity);
    }

    /// <summary>Función específica para probar solo el chaleco desde el botón.</summary>
    public void TestVestOnly()
    {
        if (!chalecoEnabled)
        {
            Debug.LogWarning("[HapticFeedback] Activa 'Chaleco Enabled' para probar la vibración del chaleco.");
            return;
        }
        
        float normalizedIntensity = Mathf.Clamp01(testCurrentValue / maxCurrentForHaptic);
        SendToVest(normalizedIntensity);
    }

    // ─────────────────────────────────────────────
    //  Internos — Controladores Meta Quest
    // ─────────────────────────────────────────────

    void Vibrate(float amplitude, float duration)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Ruta Meta SDK — mayor precisión en Quest
        try
        {
            OVRInput.SetControllerVibration(amplitude, amplitude, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(amplitude, amplitude, OVRInput.Controller.RTouch);
            StartCoroutine(StopOVRHaptics(duration));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Haptics] OVRInput fallo: {e.Message}");
            VibrateXRI(amplitude, duration); // fallback
        }
#else
        VibrateXRI(amplitude, duration);
#endif
    }

    void VibrateXRI(float amplitude, float duration)
    {
        if (!_leftController.isValid || !_rightController.isValid)
            RefreshDevices();

        try
        {
            if (_leftController.isValid)
                _leftController.SendHapticImpulse(0, amplitude, duration);
            if (_rightController.isValid)
                _rightController.SendHapticImpulse(0, amplitude, duration);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Haptics] XRI fallo: {e.Message}");
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    System.Collections.IEnumerator StopOVRHaptics(float delay)
    {
        yield return new WaitForSeconds(delay);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
    }
#endif

    IEnumerator ErrorPattern()
    {
        // Patrón intermitente para los mandos
        Vibrate(strongIntensity, 0.1f);
        yield return new WaitForSeconds(0.15f);
        Vibrate(strongIntensity, 0.1f);
        yield return new WaitForSeconds(0.15f);
        Vibrate(mediumIntensity, 0.1f);
    }

    // ─────────────────────────────────────────────
    //  Internos — Chaleco háptico (bHaptics)
    // ─────────────────────────────────────────────

    void SendToVest(float intensity, float duration = 1.0f)
    {
        Debug.Log($"[HapticFeedback] Chaleco: intensidad {intensity:F2}, duración {duration:F2}s");

        try
        {
            // Usamos tu evento "asdasd" registrado en el portal de bHaptics
            BhapticsLibrary.PlayParam("asdasd", intensity, duration: duration);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Haptics] Error con bHaptics: {e.Message}");
        }
    }
}

// ─────────────────────────────────────────────
//  Custom Editor: Dibuja los botones visuales
// ─────────────────────────────────────────────
#if UNITY_EDITOR
[CustomEditor(typeof(HapticFeedback))]
public class HapticFeedbackEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        HapticFeedback haptics = (HapticFeedback)target;

        GUILayout.Space(15);
        EditorGUILayout.LabelField("Controles de Prueba Rápida", EditorStyles.boldLabel);

        if (GUILayout.Button("► Enviar señal al Chaleco (Usa testCurrentValue)", GUILayout.Height(30)))
        {
            if (Application.isPlaying) haptics.TestVestOnly();
            else Debug.LogWarning("Entra al Play Mode (▶) para probar el chaleco.");
        }

        GUILayout.Space(5);

        // Al presionar estos botones, vibrarán tanto el chaleco como los mandos
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Suave")) { if (Application.isPlaying) haptics.PlayLight(); }
        if (GUILayout.Button("Medio")) { if (Application.isPlaying) haptics.PlayMedium(); }
        if (GUILayout.Button("Fuerte")) { if (Application.isPlaying) haptics.PlayStrong(); }
        if (GUILayout.Button("Error")) { if (Application.isPlaying) haptics.PlayError(); }
        GUILayout.EndHorizontal();
    }
}
#endif
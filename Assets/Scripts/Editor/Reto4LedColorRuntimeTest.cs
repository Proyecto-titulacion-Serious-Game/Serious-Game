using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Verificación de EXTREMO A EXTREMO (no solo el campo cristalTint): instancia cada prefab
/// Delivered_LED_* REAL, lo enciende con una corriente segura vía LED.Calculate() (misma ruta que
/// usa el motor eléctrico real), y lee el color de EMISIÓN que quedó aplicado de verdad en el
/// material de la instancia (_matInst/_EmissionColor) — el que realmente maneja el "efecto luz"
/// (brillo/glow) del LED. Confirma que el dato (cristalTint) y el efecto visual (emisión del
/// material) coinciden, no solo que el campo tenga el valor correcto.
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto4LedColorRuntimeTest.Run -logFile -
/// </summary>
public static class Reto4LedColorRuntimeTest
{
    static readonly (string path, string colorEsperado)[] Prefabs =
    {
        ("Assets/Prefabs/Delivered/Delivered_LED_Green.prefab",  "verde"),
        ("Assets/Prefabs/Delivered/Delivered_LED_Red.prefab",    "rojo"),
        ("Assets/Prefabs/Delivered/Delivered_LED_Yellow.prefab", "amarillo"),
    };

    [MenuItem("Tools/TITA/Reto 4/Test color real del LED al encender (headless)")]
    public static void Run()
    {
        Debug.Log("===== TEST: color de EMISIÓN real al encender cada LED entregable =====");
        bool allOk = true;

        foreach (var (path, nombreColor) in Prefabs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogError($"[Test] No encontré {path}"); allOk = false; continue; }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var led = instance.GetComponentInChildren<LED>(true);
            if (led == null) { Debug.LogError($"[Test] {path} no tiene componente LED."); allOk = false; continue; }

            // Activar el renderer (Delivered_LED lo trae desactivado hasta colocarse) y forzar Awake
            // (crea _matInst desde el material LED_Cristal de Resources — la ruta REAL de runtime).
            var rendAny = instance.GetComponentInChildren<Renderer>(true);
            if (rendAny != null) rendAny.enabled = true;
            led.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);

            // Circuito mínimo: nodo pin a un voltaje que da ~10 mA con la resistencia INTERNA del
            // LED (50 Ω por defecto) → dentro de rango seguro (min 5 mA, max 20 mA) = LEDState.Correct.
            var nodeA = new GameObject("Pin").AddComponent<ElectricalNode>();
            var nodeB = new GameObject("Gnd").AddComponent<ElectricalNode>();
            nodeA.voltage = 0.5f;   // 0.5V / 50Ω = 10 mA
            nodeB.voltage = 0f;
            led.nodeA = nodeA;
            led.nodeB = nodeB;
            led.polarityInverted = false;

            led.Calculate();   // misma ruta que corre el motor eléctrico real cada tick

            var matInst = GetPrivateField<Material>(led, "_matInst");
            int emissionID = GetPrivateStaticField<int>(typeof(LED), "_emissionID");

            if (matInst == null)
            {
                Debug.LogError($"[Test] {nombreColor}: _matInst es null tras Awake+Calculate (¿falta LED_Cristal en Resources?).");
                allOk = false;
                continue;
            }

            Color emision = matInst.GetColor(emissionID);
            Color tintEsperado = led.cristalTint;

            // La emisión real = cristalTint * intensidad (boostI*pulse*BoostVictoria en Update, pero
            // ApplyColor ya la fija en Calculate()/SetState() a cristalTint*1 antes del primer pulso).
            // Comparamos el HUE/dirección de color (normalizado), no el brillo absoluto.
            bool hueCoincide = ColorHueCoincide(emision, tintEsperado);

            Debug.Log($"##LEDCOLOR## variante={nombreColor} state={led.state} isOn={led.isOn} currentMa={led.current*1000f:F2} " +
                      $"cristalTint=RGBA({tintEsperado.r:F2},{tintEsperado.g:F2},{tintEsperado.b:F2}) " +
                      $"emisionMaterialReal=RGBA({emision.r:F2},{emision.g:F2},{emision.b:F2}) " +
                      $"hueCoincide={hueCoincide}");

            if (led.state != LEDState.Correct || !hueCoincide) allOk = false;

            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(nodeA.gameObject);
            Object.DestroyImmediate(nodeB.gameObject);
        }

        Debug.Log(allOk
            ? "===== RESULTADO: los 3 LEDs encienden con el color de emisión correcto (verde/rojo/amarillo). ====="
            : "===== RESULTADO: ✗ Al menos un LED no encendió con el color esperado. =====");

        if (Application.isBatchMode) EditorApplication.Exit(allOk ? 0 : 1);
    }

    // Compara la dirección del color (normalizado por magnitud) para no depender del multiplicador
    // de intensidad exacto que use ApplyColor/Update en este instante del pulso.
    static bool ColorHueCoincide(Color a, Color b)
    {
        Vector3 va = new Vector3(a.r, a.g, a.b);
        Vector3 vb = new Vector3(b.r, b.g, b.b);
        if (va.magnitude < 1e-4f || vb.magnitude < 1e-4f) return false;
        float dot = Vector3.Dot(va.normalized, vb.normalized);
        return dot > 0.98f;   // prácticamente el mismo tono
    }

    static T GetPrivateField<T>(object obj, string name)
    {
        var f = obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return (T)f.GetValue(obj);
    }

    static T GetPrivateStaticField<T>(System.Type t, string name)
    {
        var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        return (T)f.GetValue(null);
    }
}

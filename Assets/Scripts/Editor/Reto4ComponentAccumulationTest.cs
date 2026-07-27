using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Reproduce el bug reportado: "el Técnico envía un LED verde, luego un LED amarillo, y el verde
/// que ya tenía el Explorador desaparece". Usa las clases REALES (ExplorerComponentReceiver,
/// GameManager) y el mismo evento privado que dispara GameSession.OnComponenteRecibido, para ver
/// qué pasa de verdad en vez de adivinar leyendo el código.
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto4ComponentAccumulationTest.Run -logFile -
/// </summary>
public static class Reto4ComponentAccumulationTest
{
    [MenuItem("Tools/TITA/Reto 4/Test acumulación de componentes (headless)")]
    public static void Run()
    {
        bool okArduino = RunScenario("A) GameManager.currentLevel = Arduino (caso correcto)",
            configurarGM: true, nivel: LevelType.Arduino);
        bool okSinNivel = RunScenario("B) GameManager presente pero currentLevel != Arduino (regresión sospechada)",
            configurarGM: true, nivel: LevelType.OhmLaw);
        bool okSinGM = RunScenario("C) SIN GameManager en la escena (FindAnyObjectByType devuelve null)",
            configurarGM: false, nivel: LevelType.Arduino);

        Debug.Log($"===== RESUMEN: A(Arduino)={(okArduino ? "OK acumula" : "BUG: reemplaza")} · " +
                  $"B(nivel incorrecto)={(okSinNivel ? "OK acumula" : "BUG: reemplaza")} · " +
                  $"C(sin GameManager)={(okSinGM ? "OK acumula" : "BUG: reemplaza")} =====");

        if (Application.isBatchMode) EditorApplication.Exit(okArduino ? 0 : 1);
    }

    static bool RunScenario(string nombre, bool configurarGM, LevelType nivel)
    {
        Debug.Log($"===== TEST: {nombre} =====");

        var root = new GameObject("AccumTestRoot_" + nombre.Substring(0, 1));

        if (configurarGM)
        {
            var gmGO = new GameObject("GM");
            gmGO.transform.SetParent(root.transform);
            var gm = gmGO.AddComponent<GameManager>();
            SetPrivateField(gm, "_currentLevel", nivel);
            Debug.Log($"[Test] GameManager.currentLevel = {gm.currentLevel}");
        }
        else
        {
            Debug.Log("[Test] Sin GameManager en la escena.");
        }

        // ── Plantillas de LED por color (simples, sin geometría real, solo para identificar cuál llegó) ──
        GameObject MakeLedTemplate(string nombreLed, float tint)
        {
            var go = new GameObject(nombreLed);
            go.transform.SetParent(root.transform);
            var led = go.AddComponent<LED>();
            led.forwardVoltage = tint; // solo para diferenciar instancias en el log, no es el color real
            return go;
        }
        var green  = MakeLedTemplate("Template_LED_Verde",   2.0f);
        var yellow = MakeLedTemplate("Template_LED_Amarillo", 2.1f);

        // ── Receiver ──
        var recvGO = new GameObject("Receiver");
        recvGO.transform.SetParent(root.transform);
        var recv = recvGO.AddComponent<ExplorerComponentReceiver>();

        var slot = new GameObject("SlotLED").transform;
        slot.SetParent(root.transform);

        recv.slotLED       = slot;
        recv.puntoDeEntrega = slot;
        recv.ledGreenPrefab  = green;
        recv.ledYellowPrefab = yellow;
        recv.modoBandejaHibrida = false;   // evitar depender de XRGrabInteractable/física en el test

        // Forzar Awake/OnEnable si no corrieron todavía (AddComponent ya los dispara en Editor, pero
        // por robustez llamamos SendMessage para asegurar el orden si algo quedó pendiente).
        recv.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);

        var handleMethod = typeof(ExplorerComponentReceiver).GetMethod(
            "HandleComponenteRecibido", BindingFlags.NonPublic | BindingFlags.Instance);

        // ── 1) Enviar LED verde ──
        handleMethod.Invoke(recv, new object[] { ComponentType.LED, 1f, (int)ComponentVariant.LedGreen });
        var recibidos1 = GetPrivateField<System.Collections.Generic.List<GameObject>>(recv, "_componentesRecibidos");
        var ledVerdeInstancia = recibidos1.FirstOrDefault();
        Debug.Log($"##ACCUM## tras enviar VERDE: count={recibidos1.Count} " +
                  $"instancia=\"{(ledVerdeInstancia != null ? ledVerdeInstancia.name : "NULL")}\"");

        // ── 2) Enviar LED amarillo ──
        handleMethod.Invoke(recv, new object[] { ComponentType.LED, 1f, (int)ComponentVariant.LedYellow });
        var recibidos2 = GetPrivateField<System.Collections.Generic.List<GameObject>>(recv, "_componentesRecibidos");

        bool verdeSigueVivo   = ledVerdeInstancia != null;               // el objeto en sí no fue destruido
        bool verdeSigueEnLista = recibidos2.Contains(ledVerdeInstancia);  // sigue registrado

        Debug.Log($"##ACCUM## tras enviar AMARILLO: count={recibidos2.Count} " +
                  $"nombres=[{string.Join(", ", recibidos2.Select(g => g != null ? g.name : "NULL"))}] " +
                  $"verdeSigueVivo={verdeSigueVivo} verdeSigueEnLista={verdeSigueEnLista}");

        bool ok = recibidos2.Count == 2 && verdeSigueVivo && verdeSigueEnLista;
        Debug.Log(ok
            ? "===== RESULTADO: los 2 LEDs coexisten (verde + amarillo). El bug NO se reproduce con este flujo. ====="
            : "===== RESULTADO: BUG REPRODUCIDO — el LED verde desapareció al enviar el amarillo. =====");

        Object.DestroyImmediate(root);
        return ok;
    }

    static void SetPrivateField(object obj, string name, object value)
    {
        var f = obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        f.SetValue(obj, value);
    }

    static T GetPrivateField<T>(object obj, string name)
    {
        var f = obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return (T)f.GetValue(obj);
    }
}

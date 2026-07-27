using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Confirma que, instalando los 3 componentes correctos por el imán real del slot (mismo camino que
/// Reto3SlotConnectionRealTest), GameManager.OnLevelCompleted REALMENTE dispara — el evento que
/// PlayerFeedbackUI escucha para mostrar "¡FELICIDADES!". Verifica el reporte "no aparece el HUD de
/// felicitación al completar Reto 3".
///
/// Ejecutar: Tools → TITA → Reto 3 → Test celebracion (headless)
/// </summary>
public static class Reto3CelebracionTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 3/Test celebracion (headless)")]
    public static void Run()
    {
        int fails = 0;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        var tGm = typeof(GameManager);
        InvokePrivate(tGm, gm, "LoadLevel", new object[] { 2 }); // Reto 3

        var delivery = Object.FindAnyObjectByType<ComponentDeliverySystem>(FindObjectsInactive.Include);
        var cmWarmup = gm.reto3Zone.GetComponent<CircuitManager>();
        cmWarmup.AutoDetectComponents();

        Resistor resistorFaulty = Object.FindObjectsByType<Resistor>(FindObjectsInactive.Exclude).First(r => r.hasFault);
        LED ledInvertido = Object.FindObjectsByType<LED>(FindObjectsInactive.Exclude).First(l => l.polarityInverted);
        Capacitor capInvertido = Object.FindObjectsByType<Capacitor>(FindObjectsInactive.Exclude).First(c => c.polarityInverted);

        var slots = gm.reto3Zone.GetComponentsInChildren<ComponentSlot>(true);
        var slotResistor = slots.First(s => s.acceptedType == ComponentSlotType.Resistor);
        var slotLed = slots.First(s => s.acceptedType == ComponentSlotType.LED);
        var slotCap = slots.First(s => s.acceptedType == ComponentSlotType.Capacitor);
        foreach (var s in new[] { slotResistor, slotLed, slotCap })
            if (s.delivery == null) s.delivery = delivery;

        var tDelivery = typeof(ComponentDeliverySystem);
        var onTriggerStay = typeof(ComponentSlot).GetMethod("OnTriggerStay", BindingFlags.NonPublic | BindingFlags.Instance);

        void InstalarCorrecto(ComponentSlot slot, ComponentType tipo, float valor)
        {
            var go = new GameObject($"Test_{tipo}_correcto");
            go.transform.position = slot.transform.position;
            go.AddComponent<GrabbableComponent>();
            switch (tipo)
            {
                case ComponentType.Resistor: go.AddComponent<Resistor>().resistance = valor; break;
                case ComponentType.LED: go.AddComponent<LED>().polarityInverted = false; break;
                case ComponentType.Capacitor: go.AddComponent<Capacitor>().polarityInverted = false; break;
            }
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;

            SetPrivateOn(tDelivery, delivery, "_pendingType", tipo);
            SetPrivateOn(tDelivery, delivery, "_pendingValue", valor);
            SetPrivateOn(tDelivery, delivery, "_waitingForInstall", true);
            onTriggerStay.Invoke(slot, new object[] { col });
        }

        InstalarCorrecto(slotResistor, ComponentType.Resistor, resistorFaulty.correctResistance);
        InstalarCorrecto(slotLed, ComponentType.LED, 1f);
        InstalarCorrecto(slotCap, ComponentType.Capacitor, 1f);

        Debug.Log($"[Reto3Celebracion] Instalados: slotResistor={slotResistor.InstalledObject?.name} slotLed={slotLed.InstalledObject?.name} slotCap={slotCap.InstalledObject?.name}");

        // ── Suscribirse al evento REAL que dispara la celebración ──
        bool completedFired = false;
        LevelType completedLevel = default;
        bool completedSuccess = false;
        System.Action<LevelType, bool> onCompleted = (lvl, ok) => { completedFired = true; completedLevel = lvl; completedSuccess = ok; };
        GameManager.OnLevelCompleted += onCompleted;

        InvokePrivate(tGm, gm, "ForzarSimulacionRetos123");
        cmWarmup.ForceSimulate();

        bool cumpleVictoria = (bool)InvokePrivate(tGm, gm, "CumpleVictoriaRetos123");
        Debug.Log($"[Reto3Celebracion] CumpleVictoriaRetos123() = {cumpleVictoria} (esperado True)");

        SetPrivate(tGm, gm, "_tiempoInicioReto", Time.time - 3f);
        InvokePrivate(tGm, gm, "OnCircuitChangedAutoCheck");

        GameManager.OnLevelCompleted -= onCompleted;

        bool levelCompletedFlag = (bool)GetPrivate(tGm, gm, "_levelCompleted");
        Debug.Log($"[Reto3Celebracion] _levelCompleted={levelCompletedFlag}  OnLevelCompleted disparado={completedFired} " +
                  $"(level={completedLevel}, success={completedSuccess})");

        if (!cumpleVictoria) { fails++; Debug.LogError("[Reto3Celebracion] ✗ El circuito no quedó correcto."); }
        if (!levelCompletedFlag) { fails++; Debug.LogError("[Reto3Celebracion] ✗ _levelCompleted nunca pasó a true."); }
        if (!completedFired) { fails++; Debug.LogError("[Reto3Celebracion] ✗ GameManager.OnLevelCompleted NUNCA disparó — PlayerFeedbackUI jamás se habría enterado, el HUD de felicitación no podía aparecer."); }
        else if (!completedSuccess) { fails++; Debug.LogError("[Reto3Celebracion] ✗ OnLevelCompleted disparó pero con success=false."); }
        else Debug.Log("[Reto3Celebracion] ✓ OnLevelCompleted disparó correctamente con success=true — PlayerFeedbackUI mostraría el HUD.");

        Debug.Log(fails == 0
            ? "\n[Reto3Celebracion] ===== RESULTADO: ✓ La celebración del Reto 3 dispara correctamente con la instalación real ====="
            : $"\n[Reto3Celebracion] ===== RESULTADO: ✗ {fails} verificación(es) fallaron =====");
        if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    static object InvokePrivate(System.Type t, object instance, string method, object[] args = null)
    {
        var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        return m.Invoke(instance, args ?? new object[0]);
    }

    static object GetPrivate(System.Type t, object instance, string field) =>
        t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance);

    static void SetPrivate(System.Type t, object instance, string field, object value) =>
        t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(instance, value);

    static void SetPrivateOn(System.Type t, object instance, string field, object value)
    {
        var f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null) Debug.LogError($"[Reto3Celebracion] No until el campo privado '{field}' en {t.Name}.");
        else f.SetValue(instance, value);
    }
}

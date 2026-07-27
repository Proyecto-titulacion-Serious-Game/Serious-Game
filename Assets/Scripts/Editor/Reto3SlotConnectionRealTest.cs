using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verifica que el Reto 3 se completa instalando los 3 componentes correctos por la RUTA REAL de
/// slot físico (ComponentSlot.OnTriggerStay → ComponentDeliverySystem.OnExplorerInstalled), no solo
/// la validación abstracta — para cada pieza: coloca un objeto agarrable con el ElectricalComponent
/// correcto cerca del ComponentSlot real de la escena, deja que el imán del slot lo "succione" solo
/// (igual que hace el jugador soltándolo), y confirma la reparación.
///
/// Ejecutar: Tools → TITA → Reto 3 → Test conexion de slot real (headless)
/// </summary>
public static class Reto3SlotConnectionRealTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 3/Test conexion de slot real (headless)")]
    public static void Run()
    {
        int fails = 0;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        var tGm = typeof(GameManager);
        InvokePrivate(tGm, gm, "LoadLevel", new object[] { 2 }); // índice 2 = LevelType.Mixed (Reto 3)

        var delivery = Object.FindAnyObjectByType<ComponentDeliverySystem>(FindObjectsInactive.Include);
        if (delivery == null) { Debug.LogError("[Reto3Slot] No hay ComponentDeliverySystem."); Finish(1); return; }

        // AutoDetectComponents() puebla la lista interna de CircuitManager (batería incluida) — sin
        // esto el motor eléctrico no sabe que existe la fuente y todo el circuito da 0V. En el juego
        // real esto corre solo al activarse la zona; en este test hay que forzarlo (mismo patrón que
        // Reto3AutoCompleteGateTest, que sí lo hacía).
        var cmWarmup = gm.reto3Zone.GetComponent<CircuitManager>();
        cmWarmup.AutoDetectComponents();

        // ── Encontrar los 3 componentes FALLADOS reales de la zona del Reto 3 y sus slots ──
        Resistor resistorFaulty = null;
        foreach (var r in Object.FindObjectsByType<Resistor>(FindObjectsInactive.Exclude))
            if (r != null && r.hasFault) { resistorFaulty = r; break; }
        LED ledInvertido = null;
        foreach (var l in Object.FindObjectsByType<LED>(FindObjectsInactive.Exclude))
            if (l != null && l.polarityInverted) { ledInvertido = l; break; }
        Capacitor capInvertido = null;
        foreach (var c in Object.FindObjectsByType<Capacitor>(FindObjectsInactive.Exclude))
            if (c != null && c.polarityInverted) { capInvertido = c; break; }

        Debug.Log($"[Reto3Slot] Piezas falladas reales: Resistor={(resistorFaulty!=null?$"{resistorFaulty.name} (correctResistance={resistorFaulty.correctResistance}Ω)":"NINGUNO")} " +
                  $"LED={(ledInvertido!=null?ledInvertido.name:"NINGUNO")} Capacitor={(capInvertido!=null?capInvertido.name:"NINGUNO")}");

        if (resistorFaulty == null || ledInvertido == null || capInvertido == null)
        {
            Debug.LogError("[Reto3Slot] ✗ No until las 3 piezas falladas esperadas en la escena real.");
            Finish(1); return;
        }

        // ── Encontrar los ComponentSlot reales de la zona (uno por tipo aceptado) ──
        var reto3Zone = gm.reto3Zone;
        ComponentSlot slotResistor = null, slotLed = null, slotCap = null;
        foreach (var s in reto3Zone.GetComponentsInChildren<ComponentSlot>(true))
        {
            if (s.acceptedType == ComponentSlotType.Resistor && slotResistor == null) slotResistor = s;
            if (s.acceptedType == ComponentSlotType.LED && slotLed == null) slotLed = s;
            if (s.acceptedType == ComponentSlotType.Capacitor && slotCap == null) slotCap = s;
        }
        Debug.Log($"[Reto3Slot] Slots reales: Resistor={(slotResistor!=null?slotResistor.name:"NINGUNO")} " +
                  $"LED={(slotLed!=null?slotLed.name:"NINGUNO")} Capacitor={(slotCap!=null?slotCap.name:"NINGUNO")}");

        // ── Simular la ENTREGA del Técnico (arma _pendingType/_pendingValue) + INSTALAR físicamente
        // en el slot real vía el mismo trigger que usa el jugador al soltar la pieza. ──
        Debug.Log($"[Reto3Slot] slotResistor.damagedComponent={(slotResistor.damagedComponent!=null?slotResistor.damagedComponent.name:"NULL")} " +
                  $"esResistorFaulty={(slotResistor.damagedComponent==resistorFaulty.gameObject)} activeAntes={(slotResistor.damagedComponent!=null?slotResistor.damagedComponent.activeInHierarchy.ToString():"-")}");
        Debug.Log($"[Reto3Slot] slotLed.damagedComponent={(slotLed.damagedComponent!=null?slotLed.damagedComponent.name:"NULL")} " +
                  $"esLedInvertido={(slotLed.damagedComponent==ledInvertido.gameObject)} activeAntes={(slotLed.damagedComponent!=null?slotLed.damagedComponent.activeInHierarchy.ToString():"-")}");
        Debug.Log($"[Reto3Slot] slotCap.damagedComponent={(slotCap.damagedComponent!=null?slotCap.damagedComponent.name:"NULL")} " +
                  $"esCapInvertido={(slotCap.damagedComponent==capInvertido.gameObject)} activeAntes={(slotCap.damagedComponent!=null?slotCap.damagedComponent.activeInHierarchy.ToString():"-")}");

        fails += InstalarYVerificar(delivery, slotResistor, ComponentType.Resistor, resistorFaulty.correctResistance, "Resistor 220Ω (valor REAL del reto)");
        fails += InstalarYVerificar(delivery, slotLed, ComponentType.LED, 1f, "LED polaridad correcta");
        fails += InstalarYVerificar(delivery, slotCap, ComponentType.Capacitor, 1f, "Capacitor polaridad correcta");

        Debug.Log($"[Reto3Slot] TRAS instalar: R nodeA={(resistorFaulty.nodeA!=null?resistorFaulty.nodeA.name:"NULL")} nodeB={(resistorFaulty.nodeB!=null?resistorFaulty.nodeB.name:"NULL")} hasFault={resistorFaulty.hasFault} resistance={resistorFaulty.resistance}");
        Debug.Log($"[Reto3Slot] TRAS instalar: LED nodeA={(ledInvertido.nodeA!=null?ledInvertido.nodeA.name:"NULL")} nodeB={(ledInvertido.nodeB!=null?ledInvertido.nodeB.name:"NULL")} polarityInverted={ledInvertido.polarityInverted} isOn={ledInvertido.isOn} state={ledInvertido.state}");
        Debug.Log($"[Reto3Slot] TRAS instalar: CAP nodeA={(capInvertido.nodeA!=null?capInvertido.nodeA.name:"NULL")} nodeB={(capInvertido.nodeB!=null?capInvertido.nodeB.name:"NULL")} polarityInverted={capInvertido.polarityInverted}");

        // ── Recalcular y comprobar el gate real de auto-completar (igual que el jugador ve) ──
        var cmReto3 = reto3Zone.GetComponent<CircuitManager>();
        InvokePrivate(tGm, gm, "ForzarSimulacionRetos123");
        cmReto3.ForceSimulate();

        Debug.Log($"[Reto3Slot] TRAS ForceSimulate: LED isOn={ledInvertido.isOn} state={ledInvertido.state} current={ledInvertido.current*1000f:F2}mA " +
                  $"Va={(ledInvertido.nodeA!=null?ledInvertido.nodeA.voltage:0):F2} Vb={(ledInvertido.nodeB!=null?ledInvertido.nodeB.voltage:0):F2}");

        bool cumpleVictoria = (bool)InvokePrivate(tGm, gm, "CumpleVictoriaRetos123");
        Debug.Log($"[Reto3Slot] CumpleVictoriaRetos123() tras instalar por slot real = {cumpleVictoria} (esperado True)");
        if (!cumpleVictoria) { fails++; Debug.LogError("[Reto3Slot] ✗ El circuito no queda correcto tras instalar por el slot real."); }

        SetPrivate(tGm, gm, "_tiempoInicioReto", Time.time - 3f); // grace period de 2s
        InvokePrivate(tGm, gm, "OnCircuitChangedAutoCheck");
        bool completado = (bool)GetPrivate(tGm, gm, "_levelCompleted");
        Debug.Log($"[Reto3Slot] _levelCompleted tras OnCircuitChangedAutoCheck = {completado} (esperado True)");
        if (!completado) { fails++; Debug.LogError("[Reto3Slot] ✗ El Reto 3 NO se completó pese a los 3 componentes correctos instalados por slot real."); }

        Debug.Log(fails == 0
            ? "\n[Reto3Slot] ===== RESULTADO: ✓ Los 3 componentes correctos, instalados por el slot físico real, completan el Reto 3 ====="
            : $"\n[Reto3Slot] ===== RESULTADO: ✗ {fails} verificación(es) fallaron =====");
        Finish(fails == 0 ? 0 : 1);
    }

    /// <summary>Crea un objeto agarrable con el tipo eléctrico correcto, lo posiciona DENTRO del
    /// trigger del slot real, y deja correr OnTriggerStay (vía invocación directa, ya que no hay
    /// bucle de físicas fuera de Play Mode) para que el imán lo instale de verdad.</summary>
    static int InstalarYVerificar(ComponentDeliverySystem delivery, ComponentSlot slot, ComponentType tipo, float valor, string descripcion)
    {
        if (slot == null) { Debug.LogError($"[Reto3Slot] ✗ No hay slot real para {tipo}."); return 1; }

        // ComponentSlot.Awake() auto-resuelve 'delivery' vía FindAnyObjectByType si quedó null en el
        // Inspector — pero Awake() no corre de forma fiable para objetos YA GUARDADOS en escena fuera
        // de Play Mode (limitación de siempre en esta sesión). En el juego real esto se resuelve solo;
        // forzarlo a mano para que el test sea representativo.
        Debug.Log($"[Reto3Slot]   slot.delivery ANTES = {(slot.delivery != null ? slot.delivery.name : "NULL")}");
        if (slot.delivery == null) slot.delivery = delivery;

        // 1. El Técnico "envía" la pieza: arma el pending del ComponentDeliverySystem (mismo estado
        //    que deja el RPC real cuando el Técnico entrega, antes de que el Explorador la instale).
        var tDelivery = typeof(ComponentDeliverySystem);
        SetPrivateOn(tDelivery, delivery, "_pendingType", tipo);
        SetPrivateOn(tDelivery, delivery, "_pendingValue", valor);
        SetPrivateOn(tDelivery, delivery, "_waitingForInstall", true);

        // 2. Crear la pieza física agarrable con el componente eléctrico correcto, posicionada
        //    exactamente donde el imán del slot la detectaría.
        var go = new GameObject($"Test_{tipo}_SlotReal");
        go.transform.position = slot.transform.position;
        var gc = go.AddComponent<GrabbableComponent>();
        AttachElectricalComponent(go, tipo, valor);
        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;

        // 3. Disparar el MISMO trigger que usa el juego: ComponentSlot.OnTriggerStay(col).
        bool? instaladoEvento = null, repararValidadoEvento = null;
        void OnInst(bool ok) => instaladoEvento = ok;
        void OnRep(bool ok) => repararValidadoEvento = ok;
        ComponentDeliverySystem.OnComponentInstalled += OnInst;
        ComponentDeliverySystem.OnRepairValidated    += OnRep;

        var onTriggerStay = typeof(ComponentSlot).GetMethod("OnTriggerStay", BindingFlags.NonPublic | BindingFlags.Instance);
        onTriggerStay.Invoke(slot, new object[] { col });

        ComponentDeliverySystem.OnComponentInstalled -= OnInst;
        ComponentDeliverySystem.OnRepairValidated    -= OnRep;

        bool instalado = slot.InstalledObject == go;
        Debug.Log($"[Reto3Slot] [{descripcion}] slot.InstalledObject==pieza? {instalado}  " +
                  $"OnComponentInstalled={(instaladoEvento.HasValue ? instaladoEvento.ToString() : "NUNCA DISPARÓ")}  " +
                  $"OnRepairValidated={(repararValidadoEvento.HasValue ? repararValidadoEvento.ToString() : "NUNCA DISPARÓ")}");

        int fails = 0;
        if (!instalado) { fails++; Debug.LogError($"[Reto3Slot] ✗ El imán del slot real NO instaló la pieza de {tipo}."); }
        if (repararValidadoEvento != true) { fails++; Debug.LogError($"[Reto3Slot] ✗ OnExplorerInstalled NO validó/reparó la pieza de {tipo} como correcta."); }

        // En el juego real, tras una reparación válida ComponentDeliverySystem destruye
        // _spawnedComponent (la pieza entregada que el jugador acaba de instalar) — el componente
        // FIJO original de la escena es el que queda con las propiedades corregidas. _spawnedComponent
        // solo se llena por el flujo normal de entrega (SpawnInDeliveryTray), que este test no usa,
        // así que hay que limpiar la pieza de prueba a mano para no dejar un duplicado fantasma
        // compitiendo con el objeto real ya reparado en el escaneo scene-wide del circuito.
        if (repararValidadoEvento == true)
            Object.DestroyImmediate(go);

        return fails;
    }

    static void AttachElectricalComponent(GameObject go, ComponentType tipo, float valor)
    {
        switch (tipo)
        {
            case ComponentType.Resistor:
                var r = go.AddComponent<Resistor>();
                r.resistance = valor;
                break;
            case ComponentType.LED:
                var led = go.AddComponent<LED>();
                led.polarityInverted = valor < 0;
                break;
            case ComponentType.Capacitor:
                var cap = go.AddComponent<Capacitor>();
                cap.polarityInverted = valor < 0;
                break;
        }
    }

    static void Finish(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }

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
        if (f == null) Debug.LogError($"[Reto3Slot] No until el campo privado '{field}' en {t.Name}.");
        else f.SetValue(instance, value);
    }
}

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Cobertura EditMode del motor eléctrico real (<see cref="CircuitManager"/>) que usan
/// los Retos 1-3: Ley de Ohm en serie, divisor de corriente en paralelo, y el circuito
/// mixto serie-paralelo del Reto 3. Construye componentes en memoria y llama
/// <see cref="CircuitManager.ForceSimulate"/> directamente, sin depender de una escena.
///
/// IMPORTANTE (post-refactor a CircuitGraphAnalyzer/MNA unificado): a diferencia del motor
/// viejo (SimulateSeries/Parallel/Mixed, que interpretaba 'topology' para decidir la fórmula),
/// el motor actual resuelve el grafo que le des tal cual esté cableado — 'topology' ya no
/// cambia el cálculo, solo sigue existiendo como campo informativo para la UI. Por eso cada
/// test aquí cablea EXPLÍCITAMENTE nodeA/nodeB de cada componente (VCC/GND/nodos intermedios)
/// para formar la topología deseada, y deja que CircuitManager.ForceSimulate() derive
/// sourceNode/gndNode del propio VoltageSource — exactamente como pasa en la escena real,
/// donde esos campos del Inspector nunca se asignan a mano.
/// </summary>
public class CircuitManagerTests
{
    readonly List<GameObject> _roots = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        // Desuscribir a mano antes de destruir: igual que Awake()/OnEnable(), no hay garantía de
        // que OnDisable() se invoque solo en este harness fuera de Play Mode — sin esto, el
        // 'ForceSimulate' de un CircuitManager ya destruido podría seguir colgado del evento
        // estático y disparar sobre un objeto muerto en el siguiente test.
        var onDisable = typeof(CircuitManager)
            .GetMethod("OnDisable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        foreach (var r in _roots)
        {
            if (r == null) continue;
            var cm = r.GetComponent<CircuitManager>();
            if (cm != null) onDisable?.Invoke(cm, null);
            UnityEngine.Object.DestroyImmediate(r);
        }
        _roots.Clear();
    }

    GameObject NewChild(GameObject root, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform);
        return go;
    }

    ElectricalNode NewNode(GameObject root, string name) => NewChild(root, name).AddComponent<ElectricalNode>();

    /// <summary>
    /// Crea el circuito de prueba con 2 nodos ya listos (VCC/GND) y deja que 'wire' cablee
    /// componentes entre ellos (y cualquier nodo intermedio que necesite). No asigna
    /// cm.sourceNode/cm.gndNode a mano — se derivan del VoltageSource, igual que en producción.
    /// </summary>
    CircuitManager BuildCircuit(CircuitTopology topology, Action<GameObject, ElectricalNode, ElectricalNode> wire)
    {
        var root = new GameObject("TestCircuitRoot");
        _roots.Add(root);

        var vcc = NewNode(root, "VCC");
        var gnd = NewNode(root, "GND");
        wire(root, vcc, gnd);

        var cm = root.AddComponent<CircuitManager>();
        // Fuera de Play Mode, Unity no invoca Awake()/OnEnable() para un MonoBehaviour
        // recién añadido (CircuitManager no lleva [ExecuteAlways]): sin esta llamada
        // explícita, components queda vacío y RunSimulation() nunca se ejecuta.
        cm.AutoDetectComponents();

        // Mismo gotcha para OnEnable(): sin invocarlo a mano, la suscripción real
        // (CircuitGraphAnalyzer.OnCircuitChanged += ForceSimulate) nunca ocurre en este harness —
        // MarkDirty() (que dispara ese evento) sería un no-op AQUÍ aunque en Play Mode real
        // funcione solo. Se invoca por reflexión porque OnEnable() es privado.
        typeof(CircuitManager)
            .GetMethod("OnEnable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(cm, null);

        cm.topology = topology;
        cm.ForceSimulate();
        return cm;
    }

    [Test]
    public void Serie_LeyDeOhm_SinLED_CorrienteEsVoltajeEntreResistenciaTotal()
    {
        var cm = BuildCircuit(CircuitTopology.Series, (root, vcc, gnd) =>
        {
            var fuente = NewChild(root, "Fuente").AddComponent<VoltageSource>();
            fuente.voltage = 9f; fuente.nodeA = vcc; fuente.nodeB = gnd;

            var r1 = NewChild(root, "R1").AddComponent<Resistor>();
            r1.resistance = 450f; r1.nodeA = vcc; r1.nodeB = gnd;
        });

        Assert.IsFalse(cm.isShortCircuited);
        Assert.AreEqual(9f / 450f, cm.totalCurrent, 1e-4f);
    }

    [Test]
    public void Serie_ConLED_RestaElForwardVoltageAntesDeDividir()
    {
        var cm = BuildCircuit(CircuitTopology.Series, (root, vcc, gnd) =>
        {
            var fuente = NewChild(root, "Fuente").AddComponent<VoltageSource>();
            fuente.voltage = 9f; fuente.nodeA = vcc; fuente.nodeB = gnd;

            var mid = NewNode(root, "Mid");
            var r1 = NewChild(root, "R1").AddComponent<Resistor>();
            r1.resistance = 350f; r1.nodeA = vcc; r1.nodeB = mid;

            var led = NewChild(root, "LED1").AddComponent<LED>();
            led.resistance = 50f; led.forwardVoltage = 2f;
            led.nodeA = mid; led.nodeB = gnd;
        });

        float esperado = (9f - 2f) / (350f + 50f);
        Assert.AreEqual(esperado, cm.totalCurrent, 1e-4f);
    }

    [Test]
    public void Serie_ResistenciaTotalCasiCero_DetectaCortocircuito()
    {
        var cm = BuildCircuit(CircuitTopology.Series, (root, vcc, gnd) =>
        {
            var fuente = NewChild(root, "Fuente").AddComponent<VoltageSource>();
            fuente.voltage = 9f; fuente.nodeA = vcc; fuente.nodeB = gnd;

            var r1 = NewChild(root, "R1").AddComponent<Resistor>();
            r1.resistance = 0f; r1.nodeA = vcc; r1.nodeB = gnd;
        });

        // SolveMNA clampea R=0 a un mínimo de 0.1 mΩ en vez de fallar (matriz igual de
        // resoluble, solo con corriente enorme) — el cortocircuito se detecta por umbral de
        // corriente (>1 A), no porque la resolución del MNA falle.
        Assert.IsTrue(cm.isShortCircuited);
        Assert.Greater(cm.totalCurrent, 1f);
    }

    [Test]
    public void Paralelo_DosRamasLED_CorrienteTotalEsLaSumaDeCadaRama()
    {
        const float proteccion = 470f; // mismo valor que Reto2CircuitGuard.protectionOhms

        var cm = BuildCircuit(CircuitTopology.Parallel, (root, vcc, gnd) =>
        {
            var fuente = NewChild(root, "Fuente").AddComponent<VoltageSource>();
            fuente.voltage = 9f; fuente.nodeA = vcc; fuente.nodeB = gnd;

            // El motor unificado ya no aplica una "resistencia de protección" implícita por
            // rama (eso era una regla hardcoded del viejo SimulateParallel) — cada rama necesita
            // su propio resistor de protección REAL cableado en serie, igual que en el juego.
            var mid1 = NewNode(root, "Mid1");
            var rp1  = NewChild(root, "Rprot1").AddComponent<Resistor>();
            rp1.resistance = proteccion; rp1.nodeA = vcc; rp1.nodeB = mid1;
            var l1 = NewChild(root, "LED1").AddComponent<LED>();
            l1.resistance = 50f; l1.forwardVoltage = 2f; l1.nodeA = mid1; l1.nodeB = gnd;

            var mid2 = NewNode(root, "Mid2");
            var rp2  = NewChild(root, "Rprot2").AddComponent<Resistor>();
            rp2.resistance = proteccion; rp2.nodeA = vcc; rp2.nodeB = mid2;
            var l2 = NewChild(root, "LED2").AddComponent<LED>();
            l2.resistance = 50f; l2.forwardVoltage = 2f; l2.nodeA = mid2; l2.nodeB = gnd;
        });

        float iRama = (9f - 2f) / (proteccion + 50f);
        Assert.AreEqual(iRama * 2f, cm.totalCurrent, 1e-4f);
    }

    [Test]
    public void Mixto_SerieMasParaleloConLEDCorrecto_ReflejaElForwardVoltageDelLED()
    {
        var cm = BuildCircuit(CircuitTopology.Mixed, (root, vcc, gnd) =>
        {
            var fuente = NewChild(root, "Fuente").AddComponent<VoltageSource>();
            fuente.voltage = 9f; fuente.nodeA = vcc; fuente.nodeB = gnd;

            var mid = NewNode(root, "Mid");
            var rserie = NewChild(root, "Rserie").AddComponent<Resistor>();
            rserie.resistance = 200f; rserie.nodeA = vcc; rserie.nodeB = mid;

            var led = NewChild(root, "LEDparalelo").AddComponent<LED>();
            led.resistance = 50f; led.forwardVoltage = 2f;
            led.nodeA = mid; led.nodeB = gnd;
        });

        // R_equiv paralelo (una sola rama) = 50 Ω → R_total = 250 Ω; V_efectiva = 9 - 2 = 7 V
        Assert.AreEqual(7f / 250f, cm.totalCurrent, 1e-4f);
    }

    /// <summary>
    /// Regresión del bug encontrado en el motor viejo (SimulateMixed()): antes del fix, el
    /// bloque paralelo sumaba la conductancia (1/R) de un LED con polaridad invertida como si
    /// condujera con normalidad, y además dejaba de restar su Forward Voltage — el resultado
    /// neto era que la corriente agregada (la que el Técnico lee en Technicianworkstation y en
    /// TechnicianTelemetryUI) salía MÁS ALTA con el LED mal instalado que con el LED correcto,
    /// exactamente lo opuesto de lo que un diodo en polaridad inversa hace físicamente. Esa es
    /// la falla #1 que el Reto 3 pide diagnosticar, así que el dato que ve el Técnico debía
    /// reflejarla. El motor unificado (CircuitGraphAnalyzer.SolveMNA) modela esto de raíz: un
    /// LED invertido recibe una conductancia ínfima (G_DIODE_OFF) en vez de 1/R — este test
    /// confirma que esa garantía se sostiene tras el refactor a MNA.
    /// </summary>
    [Test]
    public void Mixto_LEDConPolaridadInvertida_NoAportaConductanciaComoSiConduejeraNormal()
    {
        CircuitManager Construir(bool invertido)
        {
            return BuildCircuit(CircuitTopology.Mixed, (root, vcc, gnd) =>
            {
                var fuente = NewChild(root, "Fuente").AddComponent<VoltageSource>();
                fuente.voltage = 9f; fuente.nodeA = vcc; fuente.nodeB = gnd;

                var mid = NewNode(root, "Mid");
                var rserie = NewChild(root, "Rserie").AddComponent<Resistor>();
                rserie.resistance = 200f; rserie.nodeA = vcc; rserie.nodeB = mid;

                var led = NewChild(root, "LED").AddComponent<LED>();
                led.resistance = 50f; led.forwardVoltage = 2f;
                led.polarityInverted = invertido;
                led.nodeA = mid; led.nodeB = gnd;
            });
        }

        var conLedCorrecto  = Construir(invertido: false);
        var conLedInvertido = Construir(invertido: true);

        Assert.Less(conLedInvertido.totalCurrent, conLedCorrecto.totalCurrent * 0.5f,
            $"Con el LED invertido la corriente agregada ({conLedInvertido.totalCurrent * 1000f:F2} mA) " +
            $"no debería acercarse a la del LED bien instalado ({conLedCorrecto.totalCurrent * 1000f:F2} mA): " +
            "un diodo en polaridad inversa bloquea esa rama, no debería aportar conductancia.");
    }

    /// <summary>
    /// Cubre el hueco real que dejó el refactor a MNA unificado (2026-07-26): ForceSimulate()
    /// dependía de 'sourceNode'/'gndNode' del Inspector, que NUNCA se asignan en la escena real —
    /// así que el método hacía early-return SIEMPRE, sin llegar nunca a 'OnCircuitChanged?.Invoke()'.
    /// GameManager.OnCircuitChangedAutoCheck (el auto-completado de Retos 1 y 3) escucha justo ese
    /// evento — con el bug, la victoria de esos retos jamás se hubiera disparado en una partida real,
    /// aunque el circuito quedara perfecto. Los tests de arriba llaman ForceSimulate()/totalCurrent
    /// directamente y no lo habrían detectado; este test verifica el EVENTO en sí, no solo el cálculo.
    /// </summary>
    [Test]
    public void MarkDirty_DisparaOnCircuitChanged_ParaQueGameManagerPuedaEscucharlo()
    {
        var cm = BuildCircuit(CircuitTopology.Series, (root, vcc, gnd) =>
        {
            var fuente = NewChild(root, "Fuente").AddComponent<VoltageSource>();
            fuente.voltage = 9f; fuente.nodeA = vcc; fuente.nodeB = gnd;
            var r1 = NewChild(root, "R1").AddComponent<Resistor>();
            r1.resistance = 450f; r1.nodeA = vcc; r1.nodeB = gnd;
        });

        bool disparado = false;
        void Handler() => disparado = true;
        CircuitManager.OnCircuitChanged += Handler;
        try
        {
            cm.MarkDirty();
            Assert.IsTrue(disparado,
                "MarkDirty() debe terminar invocando CircuitManager.OnCircuitChanged — si no, " +
                "GameManager.OnCircuitChangedAutoCheck (suscrito a este evento) nunca se entera de " +
                "que el circuito cambió, y Retos 1/3 no se completan solos aunque queden correctos.");
        }
        finally
        {
            CircuitManager.OnCircuitChanged -= Handler;
        }
    }
}

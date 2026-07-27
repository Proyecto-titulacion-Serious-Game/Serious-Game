using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Motor central unificado para TODOS los retos.
/// Utiliza el CircuitGraphAnalyzer (MNA) en lugar de matemáticas hardcoded.
/// 100% basado en Eventos para optimizar la Realidad Virtual.
/// </summary>
public class CircuitManager : MonoBehaviour
{
    [Header("Componentes del circuito")]
    public List<ElectricalComponent> components = new List<ElectricalComponent>();
    
    // ✅ RESTAURADO: Necesario para que CircuitDiagramPanel y GameManager sepan qué reto es.
    [Header("Topología activa")]
    public CircuitTopology topology = CircuitTopology.Series;

// Nodos principales para el analizador MNA
    public ElectricalNode sourceNode;
    public ElectricalNode gndNode;
    public float sourceVoltage = 9f; 

    // ✅ NUEVO: Puente de compatibilidad para que los Unit Tests compilen
    [Header("Legacy (Para Unit Tests)")]
    public float parallelBranchProtection = 470f;

    [Header("Resultados (solo lectura)")]
    [SerializeField] private float _totalCurrent;
    [SerializeField] private float _totalPower;       
    public bool isShortCircuited = false;           

    public HapticFeedback haptics;

    public float totalCurrent  => _totalCurrent;
    public float totalPower    => _totalPower;        

    public static event Action OnCircuitChanged;

    void Awake()
    {
        AutoDetectComponents();
    }

    void OnEnable()
    {
        CircuitGraphAnalyzer.OnCircuitChanged += ForceSimulate;
    }

    void OnDisable()
    {
        CircuitGraphAnalyzer.OnCircuitChanged -= ForceSimulate;
    }

    public void AutoDetectComponents()
    {
        components.Clear();
        var found = GetComponentsInChildren<ElectricalComponent>(true);
        components.AddRange(found);
        ForceSimulate();
    }

    // ✅ RESTAURADO: Actúa como puente. Arregla los errores en PlayerInteraction, ComponentDeliverySystem, etc.
    public void MarkDirty()
    {
        CircuitGraphAnalyzer.OnCircuitChanged?.Invoke();
    }

    // ✅ RESTAURADO: Arregla los errores en MultimeterDebugHelper
    public T FindCircuitComponent<T>() where T : ElectricalComponent
    {
        foreach (var c in components)
            if (c is T found) return found;
        return null;
    }

    public void ForceSimulate()
    {
        if (components.Count == 0) return;

        // sourceNode/gndNode del Inspector nunca se asignan a mano en la escena real — derivarlos
        // del VoltageSource presente entre los componentes detectados (mismo patrón ya probado en
        // ProtoboardSimulator.SimulateSingleSource). Sin esto, ForceSimulate() quedaba en no-op
        // permanente: nunca llegaba a SolveMNA ni a OnCircuitChanged?.Invoke(), así que el auto-check
        // de victoria de Retos 1/3 (que escucha justo ese evento) nunca se disparaba.
        var source = components.OfType<VoltageSource>().FirstOrDefault();
        ElectricalNode srcNode = sourceNode != null ? sourceNode : source?.nodeA;
        ElectricalNode gnd     = gndNode    != null ? gndNode    : source?.nodeB;
        float srcV = source != null ? source.GetEffectiveVoltage() : sourceVoltage;

        // El MNA espera solo componentes PASIVOS (sin la propia fuente) — pasarle el VoltageSource
        // lo trataba como una resistencia casi nula (cortocircuito falso de decenas de miles de A).
        var passiveComps = components.Where(c => c != null && !(c is VoltageSource)).ToList();

        if (srcNode == null || gnd == null || srcV <= 0.001f || passiveComps.Count == 0)
        {
            isShortCircuited = false;
            _totalCurrent = 0f;
            _totalPower = 0f;
            OnCircuitChanged?.Invoke();
            return;
        }

        isShortCircuited = false;

        // Usamos tu super motor matemático MNA
        bool isValid = CircuitGraphAnalyzer.SolveMNA(passiveComps, srcNode, srcV, gnd);

        if (!isValid)
        {
            isShortCircuited = true;
            _totalCurrent = 0f;
            _totalPower = 0f;

            Debug.LogWarning("[CircuitManager] ¡CORTOCIRCUITO DETECTADO por el Motor MNA!");
            if (haptics != null) haptics.PlayError();
        }
        else
        {
            _totalCurrent = srcNode.current;
            _totalPower = srcV * _totalCurrent;

            // SolveMNA clampea toda resistencia a un mínimo de 0.1 mΩ en vez de fallar (una R=0
            // real da una matriz igual de resoluble, solo con corriente enorme) — así que un
            // cortocircuito NUNCA hace que isValid sea false; se detecta por corriente excesiva,
            // mismo umbral (1 A) que ya usa ProtoboardSimulator.SimulateSingleSource.
            if (Mathf.Abs(_totalCurrent) > 1f)
            {
                isShortCircuited = true;
                if (haptics != null) haptics.PlayError();
            }

            // SolveMNA solo resuelve voltajes/corrientes del grafo — cada componente necesita este
            // paso aparte para actualizar su propio estado visual (LED: isOn/state/color).
            foreach (var comp in passiveComps)
            {
                if (comp is LED led) led.ApplyResolvedCurrent();
                else                 comp.Calculate();
            }
        }

        OnCircuitChanged?.Invoke();
    }

    

    public bool AreAllLEDsOn()
    {
        bool foundAny = false;
        foreach (var comp in components)
        {
            if (comp is LED led)
            {
                foundAny = true;
                if (!led.isOn || led.isOpenCircuit) return false;
            }
        }
        return foundAny;
    }
}

public enum CircuitTopology
{
    Series,
    Parallel,
    Mixed
}
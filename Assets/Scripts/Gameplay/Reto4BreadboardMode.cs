using System;
using UnityEngine;

/// <summary>
/// MODO PROTOBOARD para el Reto 4. En los Retos 1-3 los componentes se "succionan" a UN solo
/// ComponentSlot (imán de slot único). En el Reto 4 queremos comportamiento de protoboard real:
/// cada PATA del componente entra en un hueco distinto (dos nodos), y los cables puentean esas
/// filas al Arduino y al LED.
///
/// Mientras está activo el Reto 4 (LevelType.Arduino):
///   • Desactiva los imanes de ComponentSlot (que arrastraban el componente entero a un hueco).
///   • Fija un span MÍNIMO de patas en ProtoboardConnector para que cada pata alcance un hueco
///     distinto aunque el modelo del componente sea corto.
/// Al salir del Reto 4 revierte ambos.
///
/// Cada pata se engancha sola a su ProtoboardSlot más cercano (vía ProtoboardConnector.Bind),
/// y el motor del sandbox (BuildAdjacency + FindPath) trata cada componente como una arista
/// entre dos nodos → topología de protoboard libre.
/// </summary>
[DisallowMultipleComponent]
public class Reto4BreadboardMode : MonoBehaviour
{
    [Tooltip("Solo aplicar en el Reto 4 (LevelType.Arduino). Si está off, aplica siempre.")]
    public bool soloEnReto4 = true;

    [Tooltip("Separación mínima entre patas (m) para que cada una caiga en un hueco distinto. " +
             "La grilla está a ~4.2 cm hueco↔hueco; 0.042 = un paso de hueco. 0 = usar el modelo.")]
    public float separacionPatas = 0.042f;

    [Tooltip("Escala ABSOLUTA fijada al resistor entregado en Reto 4 para que sus patas entren en " +
             "huecos distintos del bareboard. (0,0,0) = no tocar (usa la escala del prefab).")]
    public Vector3 escalaResistor = new Vector3(13.8603153f, 22.1765041f, 10.2368851f);

    /// <summary>Escala a aplicar al resistor entregado mientras el modo protoboard está activo
    /// (null = no override). La lee ExplorerComponentReceiver al configurar el componente.</summary>
    public static Vector3? ResistorScaleReto4;

    bool _activo;
    ComponentSlot[] _slotsDesactivados;

    void OnEnable()  => GameManager.OnLevelLoaded += OnLevel;
    void OnDisable()
    {
        GameManager.OnLevelLoaded -= OnLevel;
        Desactivar();   // limpieza al destruirse
    }

    void Start()
    {
        // Aplicar según el nivel actual (por si ya estamos en Reto 4/2 al arrancar / F4).
        var gm = FindAnyObjectByType<GameManager>();
        if (!soloEnReto4 || (gm != null && EsRetoProtoboard(gm.currentLevel)))
            Activar();
    }

    // Retos con protoboard libre: Reto 4 (Arduino) y el piloto del Reto 2 (Parallel).
    static bool EsRetoProtoboard(LevelType lvl) =>
        lvl == LevelType.Arduino || lvl == LevelType.Parallel;

    void OnLevel(LevelType lvl)
    {
        if (!soloEnReto4) { Activar(); return; }
        if (EsRetoProtoboard(lvl)) Activar();
        else                       Desactivar();
    }

    void Activar()
    {
        if (_activo) return;
        _activo = true;

        ProtoboardConnector.MinLeadSpan = separacionPatas;
        ResistorScaleReto4 = escalaResistor;

        // Desactivar SOLO los imanes de ComponentSlot (Retos 1-3). Los ProtoboardSlot (Reto 4)
        // son otra clase y no se tocan.
        _slotsDesactivados = FindObjectsByType<ComponentSlot>(FindObjectsInactive.Include);
        int n = 0;
        foreach (var s in _slotsDesactivados)
            if (s != null && s.enabled) { s.enabled = false; n++; }

        Debug.Log($"[Breadboard] Modo protoboard ON (Reto 4): imanes de slot único desactivados ({n}), " +
                  $"span de patas mín = {separacionPatas*100f:0.#} cm. Cada pata se engancha a su hueco.", this);
    }

    void Desactivar()
    {
        if (!_activo) return;
        _activo = false;

        ProtoboardConnector.MinLeadSpan = 0f;
        ResistorScaleReto4 = null;

        if (_slotsDesactivados != null)
            foreach (var s in _slotsDesactivados)
                if (s != null) s.enabled = true;
        _slotsDesactivados = null;

        Debug.Log("[Breadboard] Modo protoboard OFF: imanes de slot único restaurados.", this);
    }
}

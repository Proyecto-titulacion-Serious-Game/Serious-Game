using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Panel de diagnóstico del multímetro — no requiere VR ni teclado.
/// Se auto-testea 1 segundo después de Start para dar tiempo al GameManager.
///
/// SETUP: Añadir este script a cualquier GameObject activo (p.ej. GameManager_System).
///
/// TECLAS (Game View debe estar enfocado):
///   1 / 2  → asignar sonda roja / negra manualmente
///   0      → reset sondas
///   S      → forzar simulación del CM
///   E      → emergencia: escribir voltaje directamente en los nodos (bypasa CM)
///   R      → rebuscar CircuitManager
///   I      → imprimir diagnóstico completo en Console
/// </summary>
public class MultimeterDebugHelper : MonoBehaviour
{
    [Header("Panel")]
    public bool showGui = true;

    // ─────────── referencias ───────────
    Multimeter     _mm;
    CircuitManager _cm;
    VoltageSource  _vs;
    ElectricalNode _nPos, _nNeg;

    // ─────────── mensajes GUI ───────────
    string _statusLine  = "Iniciando…";
    string _simLine     = "";
    string _probeLine   = "";
    Color  _statusColor = Color.yellow;

    // ─────────────────────────────────────────────
    void Start()
    {
        _mm = FindAnyObjectByType<Multimeter>();
        StartCoroutine(AutoTestRoutine());
    }

    // Espera 1s para que GameManager.Start() haya corrido LoadLevel(0)
    IEnumerator AutoTestRoutine()
    {
        yield return new WaitForSeconds(1f);
        Log("Auto-test después de 1s…");
        TryFindCircuit();
        if (_cm != null)
        {
            ForceSimulate();
            AssignProbes();
        }
    }

    // ─────────────────────────────────────────────
    void Update()
    {
        if (_mm == null) return;
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.digit1Key.wasPressedThisFrame) AssignProbeManual(red: true);
        if (kb.digit2Key.wasPressedThisFrame) AssignProbeManual(red: false);
        if (kb.digit0Key.wasPressedThisFrame) { _mm.ResetProbes(); Log("Probes reset."); }
        if (kb.sKey.wasPressedThisFrame)      ForceSimulate();
        if (kb.eKey.wasPressedThisFrame)      EmergencySetVoltage();
        if (kb.rKey.wasPressedThisFrame)      { TryFindCircuit(); ForceSimulate(); AssignProbes(); }
        if (kb.iKey.wasPressedThisFrame)      PrintDiagnostic();
    }

    // ─────────────────────────────────────────────
    //  Búsqueda de circuito
    // ─────────────────────────────────────────────
    void TryFindCircuit()
    {
        _cm = null; _vs = null; _nPos = null; _nNeg = null;

        // Lista ordenada: primero el CM de GameManager, luego el resto (incluye inactivos)
        var seen  = new HashSet<CircuitManager>();
        var order = new List<CircuitManager>();

        var gm = FindAnyObjectByType<GameManager>();
        CircuitManager gmCircuit = (gm != null && gm.circuit != null) ? gm.circuit.GetCompanionCircuitManager() : null;
        if (gmCircuit != null) { order.Add(gmCircuit); seen.Add(gmCircuit); }

        foreach (var c in FindObjectsByType<CircuitManager>(FindObjectsInactive.Include))
            if (seen.Add(c)) order.Add(c);

        foreach (var cm in order)
        {
            // Asegurar que tenga componentes
            if (cm.components.Count == 0) cm.AutoDetectComponents();

            var vs = cm.FindCircuitComponent<VoltageSource>();
            if (vs == null) continue;

            // VoltageSource con nodos asignados — usar aunque sean 0V por ahora
            _cm  = cm;
            _vs  = vs;
            _nPos = vs.nodeA;
            _nNeg = vs.nodeB;

            _statusLine  = $"CM: '{cm.name}'  comps={cm.components.Count}  Vs={vs.voltage:F1}V";
            _statusColor = _nPos != null ? Color.cyan : Color.yellow;

            if (_nPos == null || _nNeg == null)
                _statusLine += "\n¡VoltageSource sin nodeA/nodeB asignados en Inspector!";

            Log($"CircuitManager encontrado: '{cm.name}', comps={cm.components.Count}, " +
                $"VS.voltage={vs.voltage}V, nodeA={vs.nodeA?.name ?? "NULL"}, nodeB={vs.nodeB?.name ?? "NULL"}");
            return;
        }

        _statusLine  = "ERROR: Ningún CM tiene VoltageSource. Verifica la escena.";
        _statusColor = Color.red;
        Log("No se encontró CircuitManager con VoltageSource.");
    }

    // ─────────────────────────────────────────────
    //  Simulación
    // ─────────────────────────────────────────────
    void ForceSimulate()
    {
        if (_cm == null) { TryFindCircuit(); if (_cm == null) return; }
        if (_cm.components.Count == 0) _cm.AutoDetectComponents();
        _cm.ForceSimulate();

        float vPos = _nPos != null ? _nPos.voltage : float.NaN;
        float vNeg = _nNeg != null ? _nNeg.voltage : float.NaN;
        _simLine = $"Post-sim: CM.sourceV={_cm.sourceVoltage:F2}V | nodo+={vPos:F3}V | nodo–={vNeg:F3}V";

        bool nodesLit = _nPos != null && _nPos.voltage > 0.01f;
        Log($"ForceSimulate → sourceV={_cm.sourceVoltage:F2}V  nodo+={vPos:F3}V  nodo–={vNeg:F3}V  " +
            (nodesLit ? "✓ VOLTAJE OK" : "✗ nodos siguen en 0, prueba E=emergencia"));
    }

    // ─────────────────────────────────────────────
    //  Asignación de sondas
    // ─────────────────────────────────────────────
    void AssignProbes()
    {
        if (_mm == null || _nPos == null || _nNeg == null) return;
        _mm.SetRedNode(_nPos);
        _mm.SetBlackNode(_nNeg);
        UpdateProbeLine();
        Log($"Sondas auto-asignadas → roja='{_nPos.name}'={_nPos.voltage:F3}V  negra='{_nNeg.name}'={_nNeg.voltage:F3}V  medición={_mm.measuredVoltage:F3}V");
    }

    void AssignProbeManual(bool red)
    {
        ElectricalNode node = red ? _nPos : _nNeg;
        if (node == null) { Log($"Nodo {(red?"+":" –")} es NULL. Presiona R primero."); return; }
        if (red) _mm.SetRedNode(node); else _mm.SetBlackNode(node);
        UpdateProbeLine();
        Log($"Sonda {(red?"ROJA":"NEGRA")} asignada → '{node.name}'={node.voltage:F3}V");
    }

    void UpdateProbeLine()
    {
        if (_mm == null) return;
        string r = _mm.probeA != null ? $"'{_mm.probeA.name}'={_mm.probeA.voltage:F2}V" : "—";
        string b = _mm.probeB != null ? $"'{_mm.probeB.name}'={_mm.probeB.voltage:F2}V" : "—";
        _probeLine = $"Roja={r}  Negra={b}  →  {_mm.measuredVoltage:F3} V";
    }

    // ─────────────────────────────────────────────
    //  Emergencia: escribir voltaje directo en nodos
    // ─────────────────────────────────────────────
    void EmergencySetVoltage()
    {
        if (_vs == null || _nPos == null || _nNeg == null)
        {
            Log("EMERGENCIA: _vs o nodos son null. Presiona R.");
            return;
        }
        _nPos.voltage = _vs.voltage;
        _nNeg.voltage = 0f;
        _nPos.current = _vs.voltage / 60f;
        _nNeg.current = _vs.voltage / 60f;
        AssignProbes();
        Log($"EMERGENCIA: nodo+ forzado a {_vs.voltage}V, medición={_mm?.measuredVoltage:F3}V");
    }

    // ─────────────────────────────────────────────
    //  Diagnóstico
    // ─────────────────────────────────────────────
    void PrintDiagnostic()
    {
        Debug.Log(
            $"[DebugHelper] ══════ DIAGNÓSTICO COMPLETO ══════\n" +
            $"  Multimeter:       {(_mm != null ? _mm.name : "NULL")}\n" +
            $"  isReading:        {_mm?.isReading}\n" +
            $"  measuredVoltage:  {_mm?.measuredVoltage:F4} V\n" +
            $"  CircuitManager:   {(_cm != null ? $"'{_cm.name}' comps={_cm.components.Count}" : "NULL")}\n" +
            $"  CM.sourceVoltage: {_cm?.sourceVoltage:F3} V\n" +
            $"  CM.topology:      {_cm?.topology}\n" +
            $"  CM.shortCircuit:  {_cm?.isShortCircuited}\n" +
            $"  VoltageSource:    {(_vs != null ? $"'{_vs.name}' voltage.field={_vs.voltage}V" : "NULL")}\n" +
            $"  nodo+:            {(_nPos != null ? $"'{_nPos.name}' voltage={_nPos.voltage:F4}V" : "NULL")}\n" +
            $"  nodo–:            {(_nNeg != null ? $"'{_nNeg.name}' voltage={_nNeg.voltage:F4}V" : "NULL")}\n" +
            $"  probeRed:         {(_mm?.probeA != null ? $"'{_mm.probeA.name}'={_mm.probeA.voltage:F4}V" : "NULL")}\n" +
            $"  probeBlack:       {(_mm?.probeB != null ? $"'{_mm.probeB.name}'={_mm.probeB.voltage:F4}V" : "NULL")}"
        );
    }

    void Log(string msg) => Debug.Log($"[DebugHelper] {msg}");

    // ─────────────────────────────────────────────
    //  GUI
    // ─────────────────────────────────────────────
    void OnGUI()
    {
        if (!showGui || _mm == null) return;

        const float W = 370f, PAD = 8f;
        float h = 240f;
        var bg = new Rect(PAD, PAD, W, h);

        GUI.color = new Color(0f, 0f, 0f, 0.78f);
        GUI.DrawTexture(bg, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(PAD + 6, PAD + 4, W - 12, h - 8));

        Style(13, Color.cyan, FontStyle.Bold, out var sTitle);
        Style(11, Color.white, FontStyle.Normal, out var sNorm);
        Style(11, Color.yellow, FontStyle.Normal, out var sWarn);
        Style(11, Color.green, FontStyle.Normal, out var sGood);
        Style(10, new Color(.6f,.6f,.6f), FontStyle.Normal, out var sHint);

        GUILayout.Label("▶ MULTÍMETRO DEBUG", sTitle);
        GUILayout.Space(2);

        // Línea de CM / estado
        GUI.color = _statusColor;
        GUILayout.Label(_statusLine, sNorm);
        GUI.color = Color.white;

        // Línea post-simulación
        if (!string.IsNullOrEmpty(_simLine))
        {
            bool ok = _nPos != null && _nPos.voltage > 0.01f;
            GUILayout.Label(_simLine, ok ? sGood : sWarn);
        }

        GUILayout.Space(3);

        // Medición actual
        bool reading = _mm.isReading;
        GUI.color = reading ? Color.green : new Color(.9f,.4f,.4f);
        GUILayout.Label(
            reading
                ? $"MIDIENDO → {Fv(_mm.measuredVoltage)}  /  {_mm.measuredCurrent*1000f:F1} mA"
                : "SIN CONTACTO — sondas no asignadas",
            sNorm);
        GUI.color = Color.white;

        // Sondas
        if (!string.IsNullOrEmpty(_probeLine))
            GUILayout.Label(_probeLine, sNorm);

        GUILayout.Space(3);

        // Nodos crudos
        float vr = _nPos?.voltage ?? float.NaN;
        float vb = _nNeg?.voltage ?? float.NaN;
        bool nodesLit = _nPos != null && _nPos.voltage > 0.01f;
        GUILayout.Label(
            _nPos != null
                ? $"Nodo+: {_nPos.name}={vr:F2}V   Nodo–: {_nNeg?.name ?? "null"}={vb:F2}V"
                : "Nodo+ null — VoltageSource sin nodeA",
            nodesLit ? sGood : sWarn);

        GUILayout.Space(4);
        GUILayout.Label("[1]Rojo  [2]Negro  [0]Reset  [S]Simular  [E]Emergencia  [R]Rebuscar  [I]Info", sHint);

        GUILayout.EndArea();
    }

    static string Fv(float v) => Mathf.Abs(v) >= 1f ? $"{v:F2} V" : $"{v*1000f:F1} mV";

    static void Style(int size, Color col, FontStyle fs, out GUIStyle s)
    {
        s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs };
        s.normal.textColor = col;
    }
}

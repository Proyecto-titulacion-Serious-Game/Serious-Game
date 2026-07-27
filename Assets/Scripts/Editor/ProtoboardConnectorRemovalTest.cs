using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Verifica headless el mecanismo real de "quitar" un componente del Reto 4: no existe un botón
/// o método explícito de remover — <see cref="ProtoboardConnector.Bind"/> reengancha por
/// proximidad en cada tick (20 Hz), así que alejar físicamente el componente más allá de
/// <see cref="ProtoboardConnector.snapRadius"/> debe dejar sus nodos en null en el siguiente Bind,
/// excluyéndolo de la próxima simulación.
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod ProtoboardConnectorRemovalTest.Run -logFile -
/// </summary>
public static class ProtoboardConnectorRemovalTest
{
    [MenuItem("Tools/TITA/Reto 4/Test de retiro de componente (headless)")]
    public static void Run()
    {
        Debug.Log("===== RETO 4 — TEST DE RETIRO POR PROXIMIDAD =====");
        bool ok = true;

        var slotGo = new GameObject("SlotNodo");
        var slotNode = slotGo.AddComponent<ElectricalNode>();
        var points = new List<ConnectionPoint> { new ConnectionPoint(Vector3.zero, slotNode) };

        var compGo = new GameObject("ResistorDePrueba");
        var resistor = compGo.AddComponent<Resistor>();
        var connector = compGo.AddComponent<ProtoboardConnector>();
        connector.snapRadius = 0.012f;

        // Fuera de Play Mode, Awake() no corre (sin [ExecuteAlways]): ni leadA/leadB se
        // auto-crean (EnsureLeads) ni el campo privado _comp se asigna (Bind() lo necesita y
        // no-opea en silencio si es null). Se asignan los leads a mano y se invoca Awake() por
        // reflexión — EnsureLeads() no pisa los leads ya asignados (chequea != null primero).
        var leadAGo = new GameObject("LeadA"); leadAGo.transform.SetParent(compGo.transform);
        var leadBGo = new GameObject("LeadB"); leadBGo.transform.SetParent(compGo.transform);
        connector.leadA = leadAGo.transform;
        connector.leadB = leadBGo.transform;
        typeof(ProtoboardConnector).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(connector, null);

        // ── Paso 1: colocado ENCIMA del slot → debe engancharse ──
        compGo.transform.position = Vector3.zero;
        connector.leadA.position = Vector3.zero;
        connector.leadB.position = Vector3.zero;
        connector.Bind(points);

        bool enganchado = resistor.nodeA == slotNode && resistor.nodeB == slotNode;
        Debug.Log($"[1] Colocado sobre el slot → nodeA={(resistor.nodeA != null ? resistor.nodeA.name : "null")} " +
                  $"nodeB={(resistor.nodeB != null ? resistor.nodeB.name : "null")} (esperado: ambos={slotNode.name})");
        if (!enganchado) { ok = false; Debug.LogError("[1] FALLO: no se enganchó estando encima del slot."); }

        // ── Paso 2: el jugador lo AGARRA y ALEJA (fuera de snapRadius) → debe desconectarse ──
        connector.leadA.position = new Vector3(5f, 5f, 5f);
        connector.leadB.position = new Vector3(5f, 5f, 5f);
        connector.Bind(points);

        bool desconectado = resistor.nodeA == null && resistor.nodeB == null;
        Debug.Log($"[2] Alejado 5 unidades → nodeA={(resistor.nodeA != null ? resistor.nodeA.name : "null")} " +
                  $"nodeB={(resistor.nodeB != null ? resistor.nodeB.name : "null")} (esperado: ambos=null)");
        if (!desconectado) { ok = false; Debug.LogError("[2] FALLO: sigue enganchado tras alejarlo — 'quitar' no funcionaría en el juego real."); }

        // ── Paso 3: lo vuelve a acercar → debe re-engancharse (confirma que no quedó en un estado roto) ──
        connector.leadA.position = Vector3.zero;
        connector.leadB.position = Vector3.zero;
        connector.Bind(points);

        bool reenganchado = resistor.nodeA == slotNode && resistor.nodeB == slotNode;
        Debug.Log($"[3] Reacercado al slot → nodeA={(resistor.nodeA != null ? resistor.nodeA.name : "null")} " +
                  $"(esperado: {slotNode.name})");
        if (!reenganchado) { ok = false; Debug.LogError("[3] FALLO: no se re-enganchó tras volver a acercarlo."); }

        Object.DestroyImmediate(compGo);
        Object.DestroyImmediate(slotGo);

        Debug.Log(ok
            ? "===== RESULTADO: ✓ Colocar/quitar/recolocar por proximidad funciona como se espera ====="
            : "===== RESULTADO: ✗ FALLÓ el mecanismo de retiro por proximidad =====");

        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }
}

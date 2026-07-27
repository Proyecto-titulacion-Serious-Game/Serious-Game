using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Prueba el camino COMPLETO y REAL para el Reto 4: el receptor ACTIVO de verdad en la escena
/// ('ComponentReceiver_Caja', confirmado tras corregir el falso positivo de un fork de auditoría)
/// recibe un componente (simulando el evento real que dispara GameSession.RPC_EnviarComponente),
/// lo genera físicamente, y ese objeto generado se conecta solo a 2 slots reales del protoboard vía
/// el imán real (ProtoboardConnector.Bind) — sin crear ningún objeto de prueba desde cero como en
/// tests anteriores. Esto cierra el hueco que un fork de auditoría señaló (con razón, aunque exageró
/// el diagnóstico): ningún test anterior había ejercitado el receptor REAL, todos usaban atajos de
/// ComponentDeliverySystem.
///
/// Ejecutar: Tools → TITA → Reto 4 → Test entrega real + conexion (headless)
/// </summary>
public static class Reto4EntregaRealYConexionTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 4/Test entrega real + conexion (headless)")]
    public static void Run()
    {
        int fails = 0;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        var tGm = typeof(GameManager);
        InvokePrivate(tGm, gm, "LoadLevel", new object[] { 3 }); // Reto 4

        // ── El receptor REAL, activo en la escena — no uno de prueba ──
        var receptores = Object.FindObjectsByType<ExplorerComponentReceiver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var receptor = receptores.FirstOrDefault(r => r.gameObject.activeInHierarchy);
        Debug.Log($"[Reto4EntregaReal] Receptores en escena={receptores.Length}, activo='{(receptor != null ? receptor.name : "NINGUNO")}'");
        if (receptor == null) { Debug.LogError("[Reto4EntregaReal] ✗ No hay ningún ExplorerComponentReceiver activo."); Finish(1); return; }

        var tRecv = typeof(ExplorerComponentReceiver);
        // OnEnable() no corre de forma fiable fuera de Play Mode para objetos ya guardados en escena
        // (limitación de siempre esta sesión) — el receptor real necesita _gm resuelto y su campo
        // estático _primario apuntando a sí mismo para procesar el evento (SpawnComponente hace
        // 'if (_primario != null && _primario != this) return;'). Forzarlo a mano.
        var primarioField = tRecv.GetField("_primario", BindingFlags.NonPublic | BindingFlags.Static);
        primarioField.SetValue(null, receptor);
        var gmField = tRecv.GetField("_gm", BindingFlags.NonPublic | BindingFlags.Instance);
        gmField.SetValue(receptor, gm);

        // ── Contar Resistors ANTES de "enviar" ──
        int resistoresAntes = Object.FindObjectsByType<Resistor>(FindObjectsInactive.Exclude).Length;

        // ── Simular el envío real: mismo método que invoca GameSession.RPC_EnviarComponente al
        // recibir el RPC, con el valor REAL correcto del Reto 4 (330Ω, pin D9 — mismo patrón usado
        // hoy en las 3 pruebas nuevas). ──
        var handleMethod = tRecv.GetMethod("HandleComponenteRecibido", BindingFlags.NonPublic | BindingFlags.Instance);
        handleMethod.Invoke(receptor, new object[] { ComponentType.Resistor, 330f, (int)ComponentVariant.Default });

        var resistoresDespues = Object.FindObjectsByType<Resistor>(FindObjectsInactive.Exclude).ToList();
        Debug.Log($"[Reto4EntregaReal] Resistors en escena: antes={resistoresAntes} despues={resistoresDespues.Count}");
        if (resistoresDespues.Count <= resistoresAntes)
        {
            fails++;
            Debug.LogError("[Reto4EntregaReal] ✗ El receptor real NO generó ningún resistor nuevo tras el envío simulado.");
            Finish(1); return;
        }

        // El resistor recién generado: el que tenga resistance≈330 (los demás objetos de la escena,
        // si los hay, serían valores distintos o piezas falladas de otros retos).
        var nuevo = resistoresDespues.FirstOrDefault(r => Mathf.Approximately(r.resistance, 330f));
        Debug.Log($"[Reto4EntregaReal] Resistor generado: '{(nuevo != null ? nuevo.name : "NO ENCONTRADO")}' " +
                  $"pos={(nuevo != null ? nuevo.transform.position.ToString() : "-")} " +
                  $"tieneProtoboardConnector={(nuevo != null && nuevo.GetComponent<ProtoboardConnector>() != null)}");
        if (nuevo == null) { fails++; Debug.LogError("[Reto4EntregaReal] ✗ No until el resistor de 330Ω recién generado."); Finish(1); return; }

        // ── Confirmar que trae ProtoboardConnector (EnsureOn se llama en ConfigurarComponente) ──
        var connector = nuevo.GetComponent<ProtoboardConnector>();
        if (connector == null) { fails++; Debug.LogError("[Reto4EntregaReal] ✗ El resistor generado NO tiene ProtoboardConnector — nunca se engancharía a la protoboard."); }

        // ── Conectarlo de verdad a 2 slots reales del protoboard (imán real, no asignación manual) ──
        var sim = gm.protoSim;
        var buildNodeMap = typeof(ProtoboardSimulator).GetMethod("BuildNodeMap", BindingFlags.NonPublic | BindingFlags.Instance);
        buildNodeMap.Invoke(sim, null);
        var slots = sim.todosLosSlots.Where(s => s != null && s.assignedNode != null).ToList();
        var slotA = slots[0];
        var slotB = slots[1];

        if (connector != null)
        {
            if (connector.leadA == null || connector.leadB == null)
            {
                var awake = typeof(ProtoboardConnector).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
                awake.Invoke(connector, null);
            }
            connector.leadA.position = slotA.transform.position;
            connector.leadB.position = slotB.transform.position;

            var onEnable = typeof(ProtoboardConnector).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
            if (!ProtoboardConnector.Active.Contains(connector)) onEnable.Invoke(connector, null);

            var runSim = typeof(ProtoboardSimulator).GetMethod("RunSimulation", BindingFlags.NonPublic | BindingFlags.Instance);
            runSim.Invoke(sim, null);

            Debug.Log($"[Reto4EntregaReal] Tras conectar a '{slotA.name}'/'{slotB.name}': " +
                      $"nodeA={(nuevo.nodeA != null ? nuevo.nodeA.name : "NULL")} nodeB={(nuevo.nodeB != null ? nuevo.nodeB.name : "NULL")}");

            bool conectado = nuevo.nodeA == slotA.assignedNode && nuevo.nodeB == slotB.assignedNode;
            if (!conectado) { fails++; Debug.LogError("[Reto4EntregaReal] ✗ El resistor entregado por el receptor real NO se enganchó a los slots reales."); }
            else Debug.Log("[Reto4EntregaReal] ✓ El resistor entregado por el receptor REAL se conectó solo a 2 slots reales de la protoboard.");
        }

        Debug.Log(fails == 0
            ? "\n[Reto4EntregaReal] ===== RESULTADO: ✓ Entrega real (receptor activo de la escena) + conexión real a la protoboard, de punta a punta ====="
            : $"\n[Reto4EntregaReal] ===== RESULTADO: ✗ {fails} verificación(es) fallaron =====");
        Finish(fails == 0 ? 0 : 1);
    }

    static void Finish(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }

    static object InvokePrivate(System.Type t, object instance, string method, object[] args = null)
    {
        var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        return m.Invoke(instance, args ?? new object[0]);
    }
}

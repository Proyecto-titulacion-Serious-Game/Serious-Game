using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Mismo patrón que Reto2VictoriaDiagTest: reproduce, sobre la escena REAL (Explorador.unity), el
/// escenario "Reto 1 resuelto perfectamente" (resistencia reparada + LED con polaridad correcta) y
/// llama al MISMO chequeo de victoria que usa el botón físico (GameManager.CumpleVictoriaRetos123)
/// para confirmar que da true — y si no, leer su diagnóstico interno (_diagOhm) para la causa exacta.
///
/// A diferencia del Reto 2, el Reto 1 usa el motor CircuitManager (piezas fijas, topología Serie),
/// NO ProtoboardSimulator — no hay cables físicos/jumpers que simular aquí.
///
/// Menú: Tools → TITA → Reto 1 → Diagnosticar por qué no completa (headless)
/// </summary>
public static class Reto1VictoriaDiagTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 1/Diagnosticar por qué no completa (headless)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>();
        if (gm == null) { Debug.LogError("[Reto1VictoriaDiag] No hay GameManager en la escena."); Finish(1); return; }
        var tGm = typeof(GameManager);

        // LoadLevel(0) real: activa reto1Zone y desactiva 2/3/4, igual que en juego real — evita que
        // LEDs/resistencias de OTROS retos contaminen el conteo (mismo hallazgo que en Reto 2).
        InvokePrivate(tGm, gm, "LoadLevel", new object[] { 0 });
        Debug.Log($"[Reto1VictoriaDiag] reto1Zone.active={(gm.reto1Zone != null ? gm.reto1Zone.activeSelf.ToString() : "null")} " +
                  $"reto2Zone.active={(gm.reto2Zone != null ? gm.reto2Zone.activeSelf.ToString() : "null")} " +
                  $"reto3Zone.active={(gm.reto3Zone != null ? gm.reto3Zone.activeSelf.ToString() : "null")} " +
                  $"reto4Zone.active={(gm.reto4Zone != null ? gm.reto4Zone.activeSelf.ToString() : "null")}");

        if (gm.reto1Zone == null) { Debug.LogError("[Reto1VictoriaDiag] gm.reto1Zone no está asignado."); Finish(1); return; }

        var resistor = gm.reto1Zone.GetComponentInChildren<Resistor>(true);
        var led      = gm.reto1Zone.GetComponentInChildren<LED>(true);
        var cm       = gm.reto1Zone.GetComponentInChildren<CircuitManager>(true);

        if (resistor == null) { Debug.LogError("[Reto1VictoriaDiag] No encontré ningún Resistor bajo reto1Zone."); Finish(1); return; }
        if (led == null)      { Debug.LogError("[Reto1VictoriaDiag] No encontré ningún LED bajo reto1Zone.");      Finish(1); return; }
        if (cm == null)       { Debug.LogWarning("[Reto1VictoriaDiag] No encontré ningún CircuitManager bajo reto1Zone (¿usa ProtoboardSimulator?)."); }
        else
        {
            // _deferToProtoboard: si el CircuitManager detecta un ProtoboardSimulator entre sus
            // PROPIOS hijos, se abstiene de simular por completo (RunSimulation() hace return
            // inmediato) — el LED se queda apagado para siempre sin importar qué resistor se ponga.
            var ps = cm.GetComponentInChildren<ProtoboardSimulator>(true);
            Debug.Log($"[Reto1VictoriaDiag] CircuitManager '{cm.name}' — ProtoboardSimulator en sus hijos: " +
                      $"{(ps != null ? $"SÍ ('{ps.name}' en {RutaCompleta(ps.transform)})" : "no")}");
        }

        Debug.Log($"[Reto1VictoriaDiag] Resistor '{resistor.name}' ANTES: resistance={resistor.resistance} correctResistance={resistor.correctResistance} hasFault={resistor.hasFault} nodeA={(resistor.nodeA != null ? resistor.nodeA.name : "null")} nodeB={(resistor.nodeB != null ? resistor.nodeB.name : "null")}");
        Debug.Log($"[Reto1VictoriaDiag] LED '{led.name}' ANTES: polarityInverted={led.polarityInverted} isOn={led.isOn} state={led.state} nodeA={(led.nodeA != null ? led.nodeA.name : "null")} nodeB={(led.nodeB != null ? led.nodeB.name : "null")}");

        // ── Armar el estado "todo perfecto" ──
        resistor.Repair();                 // resistance = correctResistance; hasFault = false;
        led.polarityInverted = false;

        var sw = gm.reto1Zone.GetComponentInChildren<CircuitSwitch>(true);
        if (sw != null)
        {
            Debug.Log($"[Reto1VictoriaDiag] CircuitSwitch '{sw.name}' encontrado — isOn ANTES={sw.isOn}. Forzando isOn=true (jugador debe tocarlo en VR).");
            sw.isOn = true;
        }
        else
        {
            Debug.Log("[Reto1VictoriaDiag] No hay CircuitSwitch bajo reto1Zone.");
        }

        if (cm != null)
        {
            cm.AutoDetectComponents();
            Debug.Log($"[Reto1VictoriaDiag] cm.components ({cm.components.Count}) tras AutoDetectComponents:");
            foreach (var c in cm.components)
                Debug.Log($"[Reto1VictoriaDiag]   - {(c != null ? c.GetType().Name : "null")} '{(c != null ? c.name : "")}' " +
                          $"nodeA={(c != null && c.nodeA != null ? c.nodeA.name : "null")} nodeB={(c != null && c.nodeB != null ? c.nodeB.name : "null")}");
            Debug.Log($"[Reto1VictoriaDiag] cm.topology={cm.topology}");

            var vsScene = Object.FindAnyObjectByType<VoltageSource>();
            Debug.Log($"[Reto1VictoriaDiag] VoltageSource en TODA la escena: {(vsScene != null ? $"'{vsScene.name}' en {RutaCompleta(vsScene.transform)} voltage={vsScene.voltage} hasFault={vsScene.hasFault}" : "NINGUNO")}");

            cm.ForceSimulate();
        }
        else Debug.LogWarning("[Reto1VictoriaDiag] Sin CircuitManager no puedo forzar la simulación — el LED puede quedar con isOn desactualizado.");

        Debug.Log($"[Reto1VictoriaDiag] Resistor '{resistor.name}' DESPUÉS: resistance={resistor.resistance} hasFault={resistor.hasFault}");
        Debug.Log($"[Reto1VictoriaDiag] LED '{led.name}' DESPUÉS: isOn={led.isOn} state={led.state} I={led.current * 1000f:0.##}mA " +
                  $"Va={(led.nodeA != null ? led.nodeA.voltage : 0):0.##} Vb={(led.nodeB != null ? led.nodeB.voltage : 0):0.##}");

        // ── Chequeo de victoria REAL (el mismo que usa el botón físico) ──
        var resultado = (bool)InvokePrivate(tGm, gm, "CumpleVictoriaRetos123");
        string diagOhm = (string)GetPrivateField(tGm, gm, "_diagOhm");

        Debug.Log($"[Reto1VictoriaDiag] CumpleVictoriaRetos123() = {resultado}");
        Debug.Log($"[Reto1VictoriaDiag] _diagOhm = {diagOhm}");

        // ── Diagnóstico de texto (mismo motor que reporta al clipboard del Técnico) ──
        var diag = new DiagnosticSystem();
        string textoDiag   = diag.GetDiagnosisOhmLaw();
        string textoAccion = diag.GetNextActionOhmLaw();
        Debug.Log($"[Reto1VictoriaDiag] GetDiagnosisOhmLaw() ({textoDiag.Length} chars) =\n{textoDiag}");
        Debug.Log($"[Reto1VictoriaDiag] GetNextActionOhmLaw() ({textoAccion.Length} chars) = {textoAccion}");
        int total = textoDiag.Length + 4 /* "\n\n> " */ + textoAccion.Length;
        int chunks = Mathf.CeilToInt(total / 200f);
        Debug.Log($"[Reto1VictoriaDiag] Resumen combinado total={total} chars → {chunks} trozo(s) de 200 chars (chunking de GameSession.ReportarDiagnosticoReto).");

        Debug.Log(resultado
            ? "\n[Reto1VictoriaDiag] ===== RESULTADO: ✓ CumpleVictoriaRetos123() da TRUE con resistencia reparada + LED bien polarizado ====="
            : "\n[Reto1VictoriaDiag] ===== RESULTADO: ✗ CumpleVictoriaRetos123() da FALSE — ver _diagOhm arriba para la causa exacta =====");

        Finish(resultado ? 0 : 1);
    }

    static string RutaCompleta(Transform t)
    {
        string r = t.name;
        for (var p = t.parent; p != null; p = p.parent) r = p.name + "/" + r;
        return r;
    }

    static void Finish(int code)
    {
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    static object InvokePrivate(System.Type t, object instance, string method, object[] args = null)
    {
        var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) { Debug.LogError($"[Reto1VictoriaDiag] No encontré el método privado '{method}' en {t.Name}."); return null; }
        return m.Invoke(instance, args ?? new object[0]);
    }

    static object GetPrivateField(System.Type t, object instance, string field)
    {
        var f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null) { Debug.LogError($"[Reto1VictoriaDiag] No encontré el campo privado '{field}' en {t.Name}."); return null; }
        return f.GetValue(instance);
    }
}

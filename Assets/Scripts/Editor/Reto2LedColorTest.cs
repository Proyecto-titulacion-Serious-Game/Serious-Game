using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Compara, con el camino REAL (receptor activo + Reto2CircuitGuard real), qué pasa al entregar
/// un LED ROJO vs un LED AMARILLO — reproduce el bug reportado en VR: "rojo funciona, amarillo/
/// verde no". Cada color se prueba en una recarga de escena limpia e independiente.
///
/// Ejecutar: Tools → TITA → Reto 2 → Test color de LED (headless)
/// </summary>
public static class Reto2LedColorTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 2/Test color de LED (headless)")]
    public static void Run()
    {
        int fails = 0;
        fails += ProbarColor(ComponentVariant.LedRed, "ROJO");
        fails += ProbarColor(ComponentVariant.LedYellow, "AMARILLO");
        fails += ProbarColor(ComponentVariant.LedGreen, "VERDE");

        Debug.Log(fails == 0
            ? "\n[Reto2LedColor] ===== RESULTADO: ✓ Los 3 colores funcionan igual ====="
            : $"\n[Reto2LedColor] ===== RESULTADO: ✗ {fails} verificación(es) fallaron =====");
        if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    static int ProbarColor(ComponentVariant variante, string nombreColor)
    {
        int fails = 0;
        Debug.Log($"\n[Reto2LedColor] ===== Probando LED {nombreColor} =====");
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        var tGm = typeof(GameManager);
        InvokePrivate(tGm, gm, "LoadLevel", new object[] { 1 }); // índice 1 = LevelType.Parallel (Reto 2)

        // Reto2CircuitGuard: bootstrap manual (RuntimeInitializeOnLoadMethod no corre fuera de Play Mode)
        var guard = Object.FindAnyObjectByType<Reto2CircuitGuard>();
        if (guard == null)
        {
            var go = new GameObject("Reto2CircuitGuard_Test");
            guard = go.AddComponent<Reto2CircuitGuard>();
            typeof(Reto2CircuitGuard).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, guard);
        }
        var tGuard = typeof(Reto2CircuitGuard);
        InvokePrivate(tGuard, guard, "Activar");

        // ── Entrega real: mismo receptor activo, mismo evento que dispara la RPC real ──
        var receptores = Object.FindObjectsByType<ExplorerComponentReceiver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var receptor = receptores.FirstOrDefault(r => r.gameObject.activeInHierarchy);
        var tRecv = typeof(ExplorerComponentReceiver);
        typeof(ExplorerComponentReceiver).GetField("_primario", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, receptor);
        typeof(ExplorerComponentReceiver).GetField("_gm", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(receptor, gm);

        int ledsAntes = Object.FindObjectsByType<LED>(FindObjectsInactive.Exclude).Length;
        var handleMethod = tRecv.GetMethod("HandleComponenteRecibido", BindingFlags.NonPublic | BindingFlags.Instance);
        handleMethod.Invoke(receptor, new object[] { ComponentType.LED, 1f, (int)variante });

        var ledsDespues = Object.FindObjectsByType<LED>(FindObjectsInactive.Exclude).ToList();
        Debug.Log($"[Reto2LedColor] LEDs en escena: antes={ledsAntes} despues={ledsDespues.Count}");

        // El LED recién entregado es el que tiene polarityInverted=false Y NO es el dañado original
        // (que ya tenía polaridad forzada invertida por Activar()).
        var nuevo = ledsDespues.FirstOrDefault(l => !l.polarityInverted && l.name.Contains("Delivered"));
        if (nuevo == null) nuevo = ledsDespues.LastOrDefault(); // fallback: el más reciente
        if (nuevo == null) { fails++; Debug.LogError($"[Reto2LedColor] ✗ No until ningún LED entregado para {nombreColor}."); return fails; }
        Debug.Log($"[Reto2LedColor] LED entregado: '{nuevo.name}'");

        // ── Simular el drop en el slot correcto: posicionar exactamente ahí y llamar el enganche
        // real directamente (StartCoroutine no avanza fuera de Play Mode). ──
        var sim = (ProtoboardSimulator)GetPrivateField(tGuard, guard, "_sim");
        var buscarSlot = tGuard.GetMethod("BuscarSlotCorrecto", BindingFlags.NonPublic | BindingFlags.Instance);
        var slot = (ProtoboardSlot)buscarSlot.Invoke(guard, null);
        if (slot == null) { fails++; Debug.LogError("[Reto2LedColor] ✗ BuscarSlotCorrecto() devolvió null."); return fails; }

        nuevo.transform.position = slot.transform.position;

        var engancharMethod = tGuard.GetMethod("EngancharReemplazo", BindingFlags.NonPublic | BindingFlags.Instance);
        engancharMethod.Invoke(guard, new object[] { nuevo.gameObject });

        var intentarMethod = tGuard.GetMethod("IntentarEncajar", BindingFlags.NonPublic | BindingFlags.Instance);
        // ReemplazoPendiente es una clase anidada privada — encontrar la instancia recién creada
        // reflexionando sobre el campo estático/instancia no es directo; en su lugar, llamamos
        // CablearEnRamaDanada() DIRECTO (lo que IntentarEncajar llamaría si el snap fuera exitoso),
        // que es el paso real que importa para "funciona o no funciona".
        var cablearMethod = tGuard.GetMethod("CablearEnRamaDanada", BindingFlags.NonPublic | BindingFlags.Instance);
        cablearMethod.Invoke(guard, new object[] { nuevo.gameObject });

        // ── Forzar simulación y leer el estado eléctrico real del LED ──
        var runSim = typeof(ProtoboardSimulator).GetMethod("RunSimulation", BindingFlags.NonPublic | BindingFlags.Instance);
        if (sim != null) runSim.Invoke(sim, null);
        sim?.ForzarValidacion();

        Debug.Log($"[Reto2LedColor] LED {nombreColor} tras cablear: nodeA={(nuevo.nodeA != null ? nuevo.nodeA.name : "NULL")} " +
                  $"nodeB={(nuevo.nodeB != null ? nuevo.nodeB.name : "NULL")} isOn={nuevo.isOn} state={nuevo.state} " +
                  $"current={nuevo.current * 1000f:F2}mA polarityInverted={nuevo.polarityInverted}");

        bool ok = nuevo.nodeA != null && nuevo.nodeB != null && nuevo.isOn && nuevo.state == LEDState.Correct;
        if (!ok) { fails++; Debug.LogError($"[Reto2LedColor] ✗ El LED {nombreColor} NO quedó en estado Correct/isOn tras cablear."); }
        else Debug.Log($"[Reto2LedColor] ✓ El LED {nombreColor} quedó correctamente encendido (isOn=True, state=Correct).");

        return fails;
    }

    static object InvokePrivate(System.Type t, object instance, string method, object[] args = null)
    {
        var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        return m.Invoke(instance, args ?? new object[0]);
    }

    static object GetPrivateField(System.Type t, object instance, string field) =>
        t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance);
}

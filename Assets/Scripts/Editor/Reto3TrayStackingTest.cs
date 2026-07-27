using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verifica el fix del bug #2 del capacitor (Reto 3): el Resistor entregado (aún sin cargar por el
/// jugador) y el Capacitor entregado después caen en el MISMO punto de entrega (slotResistor/
/// slotLED/slotCapacitor sin asignar en ComponentReceiver.prefab) — confirma que sus colliders
/// tienen la colisión física ignorada entre sí.
///
/// Ejecutar: Tools → TITA → Pruebas → Reto3 tray stacking (headless)
/// </summary>
public static class Reto3TrayStackingTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Pruebas/Reto3 tray stacking (headless)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>();
        typeof(GameManager).GetMethod("LoadLevel", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(gm, new object[] { 2 }); // Reto 3

        var receiver = Object.FindObjectsByType<ExplorerComponentReceiver>(FindObjectsInactive.Include)
            .FirstOrDefault(r => r.gameObject.activeInHierarchy);
        if (receiver == null) { Debug.LogError("[TrayStack] ✗ No hay ExplorerComponentReceiver activo."); Finish(1); return; }

        var t = typeof(ExplorerComponentReceiver);
        t.GetField("_primario", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, receiver);
        var spawn = t.GetMethod("SpawnComponente", BindingFlags.NonPublic | BindingFlags.Instance);

        // Técnico entrega Resistor, luego LED, luego Capacitor — como en el flujo real del Reto 3.
        // El jugador NO carga ninguno (siguen todos en la bandeja) al momento de recibir el siguiente.
        spawn.Invoke(receiver, new object[] { ComponentType.Resistor, 470f, null, ComponentVariant.Default });
        spawn.Invoke(receiver, new object[] { ComponentType.LED, 1f, null, ComponentVariant.Default });
        spawn.Invoke(receiver, new object[] { ComponentType.Capacitor, 1f, null, ComponentVariant.Default });

        var recibidos = (System.Collections.Generic.List<GameObject>)
            t.GetField("_componentesRecibidos", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(receiver);

        if (recibidos.Count < 3)
        {
            Debug.LogError($"[TrayStack] ✗ Solo se registraron {recibidos.Count}/3 piezas entregadas.");
            Finish(1); return;
        }

        int fails = 0;
        for (int i = 0; i < recibidos.Count; i++)
            for (int j = i + 1; j < recibidos.Count; j++)
            {
                var a = recibidos[i]; var b = recibidos[j];
                if (a == null || b == null) continue;
                var colsA = a.GetComponentsInChildren<Collider>(true);
                var colsB = b.GetComponentsInChildren<Collider>(true);
                bool todasIgnoradas = true;
                foreach (var ca in colsA) foreach (var cb in colsB)
                    if (!Physics.GetIgnoreCollision(ca, cb)) todasIgnoradas = false;
                Debug.Log($"[TrayStack] '{a.name}' <-> '{b.name}': colisión ignorada en todos los pares = {todasIgnoradas}");
                if (!todasIgnoradas) fails++;
            }

        Debug.Log(fails == 0
            ? "\n[TrayStack] ===== RESULTADO: ✓ Las 3 piezas entregadas (bandeja compartida) no colisionan entre sí ====="
            : $"\n[TrayStack] ===== RESULTADO: ✗ {fails} par(es) siguen colisionando =====");
        Finish(fails == 0 ? 0 : 1);
    }

    static void Finish(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }
}

using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verifica los 2 bugs reportados por el usuario tras el último APK:
///   1. Reto 3 — el capacitor "sale volando" al colocarlo: confirma que la pieza recién
///      entregada ignora la colisión física contra el capacitor FIJO original de la escena
///      (Physics.GetIgnoredCollision), el mecanismo real del fix en ExplorerComponentReceiver.cs.
///   2. Multímetro — punta roja: NO se encontró una causa de código/datos tras 2 revisiones
///      independientes (transform/rotación/colliders IDÉNTICOS entre ambas puntas, controllerNode
///      y probeType correctos). Este test solo REPORTA el estado geométrico verificado — no hay
///      fix que confirmar porque no se encontró una asimetría real.
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod BugsVerificationTest.Run -logFile -
/// </summary>
public static class BugsVerificationTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Pruebas/Verificar bugs capacitor + multimetro")]
    public static void Run()
    {
        int fails = 0;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        fails += VerificarCapacitorReto3() ? 0 : 1;
        VerificarMultimetro();   // diagnóstico, no falla el test

        Debug.Log(fails == 0
            ? "\n##BUGSTEST## RESULTADO: ✓ Fix del capacitor verificado (mecanismo real, no solo que el reto complete)."
            : $"\n##BUGSTEST## RESULTADO: ✗ {fails} verificación(es) fallaron.");
        if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  1. Capacitor Reto 3 — verifica el MECANISMO del fix (IgnoreCollision),
    //     no solo que el reto complete (eso ya pasaba antes del fix).
    // ─────────────────────────────────────────────────────────────────────
    static bool VerificarCapacitorReto3()
    {
        var gm = Object.FindAnyObjectByType<GameManager>();
        typeof(GameManager).GetMethod("LoadLevel", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(gm, new object[] { 2 }); // Reto 3

        var capacitorFijo = Object.FindObjectsByType<Capacitor>(FindObjectsInactive.Exclude)
            .FirstOrDefault(c => c.nodeA != null && c.nodeB != null);
        if (capacitorFijo == null)
        {
            Debug.LogError("[BugsTest] ✗ No encontré el capacitor FIJO del Reto 3 (nodeA/nodeB asignados).");
            return false;
        }
        var colsFijo = capacitorFijo.GetComponentsInChildren<Collider>(true);
        if (colsFijo.Length == 0)
        {
            Debug.LogWarning($"[BugsTest] '{capacitorFijo.name}' no tiene Collider — nada que pudiera empujar. " +
                              "Fix no aplicable (no hay bug de física posible aquí); no cuenta como fallo.");
            return true;
        }

        var receiver = Object.FindObjectsByType<ExplorerComponentReceiver>(FindObjectsInactive.Include)
            .FirstOrDefault(r => r.gameObject.activeInHierarchy);
        if (receiver == null)
        {
            Debug.LogError("[BugsTest] ✗ No encontré ExplorerComponentReceiver activo en la escena.");
            return false;
        }

        var tRecv = typeof(ExplorerComponentReceiver);
        var primarioField = tRecv.GetField("_primario", BindingFlags.NonPublic | BindingFlags.Static);
        primarioField.SetValue(null, receiver);
        var gsInstance = GameSession.Instance; // puede ser null en headless: SpawnComponente cae a _gm.currentLevel

        // Mismo camino real: RPC_EnviarComponente → OnComponenteRecibido → SpawnComponente (privado).
        var spawnMethod = tRecv.GetMethod("SpawnComponente", BindingFlags.NonPublic | BindingFlags.Instance);
        spawnMethod.Invoke(receiver, new object[] { ComponentType.Capacitor, 1f, null, ComponentVariant.Default });

        var recibidosField = tRecv.GetField("_componentesRecibidos", BindingFlags.NonPublic | BindingFlags.Instance);
        var recibidos = (System.Collections.Generic.List<GameObject>)recibidosField.GetValue(receiver);
        var piezaNueva = recibidos.LastOrDefault();
        if (piezaNueva == null)
        {
            Debug.LogError("[BugsTest] ✗ SpawnComponente no generó ninguna pieza nueva (¿faltan prefabs/slots asignados en esta escena de prueba?).");
            return false;
        }
        var colsNueva = piezaNueva.GetComponentsInChildren<Collider>(true);
        if (colsNueva.Length == 0)
        {
            Debug.LogError($"[BugsTest] ✗ La pieza nueva '{piezaNueva.name}' no tiene Collider — no se puede verificar el ignore.");
            return false;
        }

        bool todasIgnoradas = true;
        int pares = 0;
        foreach (var cn in colsNueva)
            foreach (var cf in colsFijo)
            {
                pares++;
                bool ignorada = Physics.GetIgnoreCollision(cn, cf);
                if (!ignorada) todasIgnoradas = false;
                Debug.Log($"[BugsTest] IgnoreCollision('{cn.name}' nueva pieza, '{cf.name}' fija '{capacitorFijo.name}') = {ignorada}");
            }

        if (todasIgnoradas)
            Debug.Log($"[BugsTest] ✓ Los {pares} par(es) de colliders (pieza nueva ↔ capacitor fijo original) tienen la colisión IGNORADA — ya no pueden empujarse entre sí.");
        else
            Debug.LogError($"[BugsTest] ✗ Al menos un par de colliders SIGUE colisionando físicamente — el fix no cubrió este caso.");

        return todasIgnoradas;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  2. Multímetro — solo diagnóstico geométrico (no se encontró fix que aplicar)
    // ─────────────────────────────────────────────────────────────────────
    static void VerificarMultimetro()
    {
        var redGO   = GameObject.Find("Probe_Red_Tip");
        var blackGO = GameObject.Find("Probe_Black_Tip");
        if (redGO == null || blackGO == null)
        {
            Debug.LogWarning("[BugsTest] Multímetro: no until Probe_Red_Tip/Probe_Black_Tip en la escena — sin diagnóstico posible.");
            return;
        }

        var redProbe   = redGO.GetComponent<MultimeterProbe>();
        var blackProbe = blackGO.GetComponent<MultimeterProbe>();
        Debug.Log($"[BugsTest] Multímetro — DIAGNÓSTICO (sin fix aplicado, no se halló asimetría de datos):\n" +
                  $"  Red_Tip:   pos={redGO.transform.localPosition} rot={redGO.transform.localRotation.eulerAngles} " +
                  $"probeType={redProbe?.probeType} controllerNode={redProbe?.controllerNode}\n" +
                  $"  Black_Tip: pos={blackGO.transform.localPosition} rot={blackGO.transform.localRotation.eulerAngles} " +
                  $"probeType={blackProbe?.probeType} controllerNode={blackProbe?.controllerNode}\n" +
                  "  Si esto sigue fallando en VR, la causa es runtime (input del controlador físico, " +
                  "hover/selección de XRI, o algo que solo se reproduce con el hardware real) — no un dato de escena.");
    }
}

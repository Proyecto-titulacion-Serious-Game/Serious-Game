using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Reproduce, sobre Tecnico.unity REAL, la secuencia de la tecla N: Saludo → (pausa esperando
/// nombre de grupo) → RoomCodeEntryUI.DebeMostrarse() → confirmar nombre → Historia → Roles → ...
/// Todo por invocación directa (reflexión), porque Start()/Update() no corren fuera de Play Mode.
///
/// Menú: Tools → TITA → Reto 2 → Diagnosticar tecla N del NPC (headless)
/// </summary>
public static class TutorialNPCNKeyTest
{
    const string ScenePath = "Assets/Scenes/Tecnico/Tecnico.unity";

    [MenuItem("Tools/TITA/Reto 2/Diagnosticar tecla N del NPC (headless)")]
    public static void Run()
    {
        int fails = 0;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Awake()/OnEnable() de componentes YA GUARDADOS en la escena no corren de forma fiable al
        // abrir la escena fuera de Play Mode (limitación ya documentada en la memoria del proyecto:
        // "causa raíz de todo: Awake/OnEnable no corren fuera de Play Mode para objetos ya guardados
        // en escena"). ConnectionManager.Instance quedaría NULL sin esto, dando un falso negativo en
        // DebeMostrarse() que NO reproduce el juego real (en Play Mode/un .exe corriendo, Awake() sí
        // corre normal). Forzarlo a mano para que el test sea representativo.
        var cmScene = Object.FindAnyObjectByType<ConnectionManager>(FindObjectsInactive.Include);
        if (cmScene != null && ConnectionManager.Instance == null)
        {
            InvokePrivate(typeof(ConnectionManager), cmScene, "Awake");
            Debug.Log($"[NKeyDiag] ConnectionManager.Awake() forzado a mano — Instance ahora = {(ConnectionManager.Instance != null ? ConnectionManager.Instance.gameObject.name : "sigue NULL")}");
        }

        var npc = Object.FindAnyObjectByType<TutorialNPC>();
        if (npc == null) { Debug.LogError("[NKeyDiag] No hay TutorialNPC en la escena."); Finish(1); return; }
        var t = typeof(TutorialNPC);

        // Start() no corre fuera de Play Mode (sin bucle de frames) — invocarlo a mano, igual que
        // en otros tests de esta sesión (Reto2ClipboardYLedRuntimeTest con Reto2CircuitGuard).
        int pasoIntro = (int)GetPrivate(t, npc, "_pasoIntro");
        Debug.Log($"[NKeyDiag] _pasoIntro ANTES de Start()={pasoIntro} (−1 = intro no arrancada)");
        if (pasoIntro < 0)
        {
            InvokePrivate(t, npc, "Start");
            pasoIntro = (int)GetPrivate(t, npc, "_pasoIntro");
        }
        Debug.Log($"[NKeyDiag] _pasoIntro tras Start()={pasoIntro} (esperado 0 = 'Saludo')");
        if (pasoIntro != 0) { Debug.LogError("[NKeyDiag] ✗ La intro no arrancó en el paso 0."); fails++; }

        Debug.Log($"[NKeyDiag] pasosIntro.Count={((System.Collections.IList)GetPrivate(t, npc, null, "pasosIntro")).Count}");

        // ── Simular 1ª presión de N (Saludo → intento de Historia) ──
        InvokePrivate(t, npc, "AvanzarIntro");
        pasoIntro = (int)GetPrivate(t, npc, "_pasoIntro");
        bool pausado = (bool)GetPrivate(t, npc, "_pausadoPorNombreGrupo");
        bool puedePedir = TutorialNPC.PuedePedirNombreGrupo;
        Debug.Log($"[NKeyDiag] Tras 1ª N: _pasoIntro={pasoIntro} (esperado 1) _pausadoPorNombreGrupo={pausado} (esperado True) " +
                  $"PuedePedirNombreGrupo={puedePedir} (esperado True)");
        if (pasoIntro != 1) { Debug.LogError("[NKeyDiag] ✗ _pasoIntro no avanzó a 1."); fails++; }
        if (!pausado) { Debug.LogError("[NKeyDiag] ✗ No se activó la pausa por nombre de grupo."); fails++; }
        if (!puedePedir) { Debug.LogError("[NKeyDiag] ✗ PuedePedirNombreGrupo sigue False — RoomCodeEntryUI nunca mostraría el panel."); fails++; }

        // ── ¿RoomCodeEntryUI.DebeMostrarse() daría true en este punto? ──
        var rce = Object.FindAnyObjectByType<RoomCodeEntryUI>(FindObjectsInactive.Include);
        if (rce == null)
        {
            Debug.LogWarning("[NKeyDiag] No hay RoomCodeEntryUI en la escena todavía (se auto-crea por RuntimeInitializeOnLoadMethod, " +
                              "que no corre fuera de Play Mode) — instanciándolo a mano para probar DebeMostrarse().");
            var go = new GameObject("RoomCodeEntryUI_Test");
            rce = go.AddComponent<RoomCodeEntryUI>();
        }
        var tRce = typeof(RoomCodeEntryUI);
        var cmCheck = ConnectionManager.Instance;
        Debug.Log($"[NKeyDiag] Desglose de DebeMostrarse(): ConnectionManager.Instance={(cmCheck != null ? cmCheck.gameObject.name : "NULL")} " +
                  $"rolAutomatico={(cmCheck != null ? cmCheck.rolAutomatico.ToString() : "N/A")} " +
                  $"esperarEntradaDeCodigo={(cmCheck != null ? cmCheck.esperarEntradaDeCodigo.ToString() : "N/A")}");
        var runnerField = tRce.GetField("_runner", BindingFlags.NonPublic | BindingFlags.Instance);
        Debug.Log($"[NKeyDiag] rce._runner={(runnerField?.GetValue(rce) ?? "NULL")}");

        bool debeMostrarse = (bool)InvokePrivate(tRce, rce, "DebeMostrarse");
        Debug.Log($"[NKeyDiag] RoomCodeEntryUI.DebeMostrarse()={debeMostrarse} (esperado True — el panel debería aparecer acá)");
        if (!debeMostrarse) { Debug.LogError("[NKeyDiag] ✗ El panel de nombre de grupo NO se mostraría — acá se traba la secuencia."); fails++; }

        // ── Confirmar nombre de grupo (como si el Técnico hubiera escrito y presionado Crear) ──
        TutorialNPC.NotificarNombreGrupoListo();
        pausado = (bool)GetPrivate(t, npc, "_pausadoPorNombreGrupo");
        pasoIntro = (int)GetPrivate(t, npc, "_pasoIntro");
        Debug.Log($"[NKeyDiag] Tras NotificarNombreGrupoListo(): _pausadoPorNombreGrupo={pausado} (esperado False) _pasoIntro={pasoIntro} (esperado 1, 'Historia' ya aplicado)");
        if (pausado) { Debug.LogError("[NKeyDiag] ✗ La pausa no se liberó tras confirmar el nombre de grupo."); fails++; }

        // ── 2ª, 3ª, 4ª presión de N: recorrer el resto de la intro sin trabarse ──
        int totalPasos = ((System.Collections.IList)GetPrivate(t, npc, null, "pasosIntro")).Count;
        int intentos = 0;
        while ((int)GetPrivate(t, npc, "_pasoIntro") >= 0 && intentos < totalPasos + 2)
        {
            InvokePrivate(t, npc, "AvanzarIntro");
            intentos++;
        }
        bool introCompletada = TutorialNPC.IntroCompletada;
        Debug.Log($"[NKeyDiag] Tras {intentos} presiones adicionales de N: IntroCompletada={introCompletada} (esperado True)");
        if (!introCompletada) { Debug.LogError("[NKeyDiag] ✗ La intro nunca terminó de recorrerse con N."); fails++; }

        Debug.Log(fails == 0
            ? "\n[NKeyDiag] ===== RESULTADO: ✓ La secuencia completa de N funciona de punta a punta ====="
            : $"\n[NKeyDiag] ===== RESULTADO: ✗ {fails} verificación(es) fallaron =====");

        Finish(fails == 0 ? 0 : 1);
    }

    static void Finish(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }

    static object InvokePrivate(System.Type t, object instance, string method, object[] args = null)
    {
        var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) { Debug.LogError($"[NKeyDiag] No encontré el método privado '{method}' en {t.Name}."); return null; }
        return m.Invoke(instance, args ?? new object[0]);
    }

    static object GetPrivate(System.Type t, object instance, string field, string publicField = null)
    {
        if (publicField != null)
        {
            var pf = t.GetField(publicField, BindingFlags.Public | BindingFlags.Instance);
            if (pf != null) return pf.GetValue(instance);
        }
        var f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null) { Debug.LogError($"[NKeyDiag] No encontré el campo '{field}' en {t.Name}."); return null; }
        return f.GetValue(instance);
    }
}

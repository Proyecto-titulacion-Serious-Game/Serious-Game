using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Prueba de extremo a extremo SOBRE LA ESCENA REAL (Explorador.unity), NO sobre objetos
/// sintéticos: abre la escena, deja que Reto2CircuitGuard se auto-instancie (RuntimeInitializeOnLoadMethod,
/// igual que en juego real), lo activa como si el jugador hubiera entrado a Reto 2, y verifica:
///
///  TEST A — Clipboard: reproduce la condición de carrera que causaba "el clipboard nunca se
///  muestra". En Editor/batchmode GameSession.Instance SIEMPRE es null (no hay sesión Fusion real),
///  que es EXACTAMENTE la ventana de arranque real (el reporter arranca casi instantáneo, el
///  handshake de Fusion tarda segundos). Con el bug viejo, EnviarResumenTecnico() solo mandaba el
///  evento la PRIMERA vez (luego deduplicaba para siempre). Con el fix, debe mandarlo TODAS las
///  veces mientras no haya sesión — se verifica llamándolo 2 veces seguidas con el mismo estado.
///
///  TEST B — LED: confirma que el slot de "rescate" (BuscarSlotCorrecto) está cerca de la rama
///  real del LED dañado, NO en el pivote centro del board (el bug reportado: "aparece en medio
///  de las 2 ramas").
///
/// Menú: Tools → TITA → Reto 2 → Test extremo a extremo clipboard + LED (headless)
/// </summary>
public static class Reto2ClipboardYLedRuntimeTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 2/Test extremo a extremo clipboard + LED (headless)")]
    public static void Run()
    {
        int fails = 0;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // RuntimeInitializeOnLoadMethod (el Bootstrap real de Reto2CircuitGuard) solo dispara al
        // ENTRAR a Play Mode o en un build corriendo — EditorSceneManager.OpenScene() en Edit Mode
        // NO lo activa. Para probar sobre la escena real sin entrar a Play Mode, se instancia el
        // MISMO componente a mano (Awake/OnEnable sí corren igual en Edit Mode al hacer AddComponent).
        var guard = Object.FindAnyObjectByType<Reto2CircuitGuard>();
        if (guard == null)
        {
            var go = new GameObject("Reto2CircuitGuard_Test");
            guard = go.AddComponent<Reto2CircuitGuard>();
            typeof(Reto2CircuitGuard).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                ?.SetValue(null, guard);
            Debug.Log("[Reto2E2E] Reto2CircuitGuard instanciado a mano (Bootstrap real no corre fuera de Play Mode).");
        }
        else
        {
            Debug.Log("[Reto2E2E] ✓ Reto2CircuitGuard ya existía en la escena.");
        }

        var tGuard = typeof(Reto2CircuitGuard);
        InvokePrivate(tGuard, guard, "Activar");   // simula GameManager.OnLevelLoaded(Parallel)
        Debug.Log("[Reto2E2E] Activar() invocado — simula entrar a Reto 2.");

        // Confirmar que Activar() encontró el simulador real de la escena.
        var sim = (ProtoboardSimulator)GetPrivateField(tGuard, guard, "_sim");
        if (sim == null)
        {
            Debug.LogError("[Reto2E2E] ✗ _sim quedó null tras Activar() — LocalizarSim() no encontró el board del Reto 2.");
            fails++;
        }
        else
        {
            Debug.Log($"[Reto2E2E] ✓ _sim = '{sim.name}' (board real encontrado).");
        }

        // ───────── TEST A: clipboard — no debe dejar de reenviar mientras no hay sesión ─────────
        Debug.Log("\n[Reto2E2E] ===== TEST A: reenvío del clipboard sin sesión de red =====");
        Debug.Log($"[Reto2E2E] GameSession.Instance == null: {GameSession.Instance == null} (debe ser true en Editor — no hay Fusion real).");

        int probeHits = 0;
        System.Action<int, string> probe = (r, s) => { if (r == 2) probeHits++; };
        GameSession.OnDiagnosticoRetoActualizado += probe;

        // Forzar semilla conocida en _ultimoResumen para que la 1ª llamada de este test NO cuente
        // como "primera vez siempre manda" — así medimos de verdad si DEDUPLICA cuando no debería.
        SetPrivateField(tGuard, guard, "_ultimoResumen", "SEMILLA_DE_PRUEBA_QUE_NUNCA_COINCIDE");

        InvokePrivate(tGuard, guard, "EnviarResumenTecnico");
        int hitsTras1 = probeHits;
        InvokePrivate(tGuard, guard, "EnviarResumenTecnico");   // MISMO estado del circuito → mismo texto
        int hitsTras2 = probeHits;

        GameSession.OnDiagnosticoRetoActualizado -= probe;

        Debug.Log($"[Reto2E2E] Llamada 1: probeHits={hitsTras1} (esperado 1). Llamada 2 (mismo estado, SIN sesión): probeHits={hitsTras2} (esperado 2 — antes del fix se quedaba en 1).");
        if (hitsTras1 != 1) { Debug.LogError("[Reto2E2E] ✗ La primera llamada no disparó el evento."); fails++; }
        if (hitsTras2 != 2) { Debug.LogError("[Reto2E2E] ✗ BUG DE CARRERA SIGUE PRESENTE: la 2ª llamada con el mismo diagnóstico no reenvió (dedup activo sin sesión de red)."); fails++; }
        else Debug.Log("[Reto2E2E] ✓ Reenvía correctamente mientras no hay sesión de red — el clipboard ya no se queda vacío por esta causa.");

        // ───────── TEST B: LED — el slot de rescate no debe caer en el centro del board ─────────
        Debug.Log("\n[Reto2E2E] ===== TEST B: destino del rescate del LED =====");
        if (sim == null)
        {
            Debug.LogError("[Reto2E2E] ✗ No se puede probar el rescate sin _sim.");
            fails++;
        }
        else
        {
            var slotRescate = (ProtoboardSlot)InvokePrivate(tGuard, guard, "BuscarSlotCorrecto");
            if (slotRescate == null)
            {
                Debug.LogError("[Reto2E2E] ✗ BuscarSlotCorrecto() devolvió null — el rescate caería al fallback (centro del board).");
                fails++;
            }
            else
            {
                Vector3 destinoRescate = slotRescate.transform.position;
                Vector3 centroBoard    = sim.transform.position;
                float distDelCentro    = Vector3.Distance(destinoRescate, centroBoard);

                // Las 2 ramas están a ~2.3m de distancia entre sí (medido en el diagnóstico previo:
                // Branch1 x≈-58.3..-58.8, Branch2 x≈-56.5..-57.5) — el bug viejo devolvía EXACTO
                // el centro (distancia 0 del propio pivote). Cualquier distancia > 0 ya prueba que
                // el destino cambió; exigimos que además esté del lado de la rama 2 (LED dañado),
                // no en el punto medio.
                Debug.Log($"[Reto2E2E] Slot de rescate: '{slotRescate.name}' pos={destinoRescate}");
                Debug.Log($"[Reto2E2E] Centro del board (destino ANTES del fix): pos={centroBoard}  distancia al slot de rescate={distDelCentro:0.00}m");

                if (distDelCentro < 0.3f)
                {
                    Debug.LogError("[Reto2E2E] ✗ El slot de rescate sigue prácticamente en el centro del board — el fix no lo alejó lo suficiente.");
                    fails++;
                }
                else
                {
                    Debug.Log("[Reto2E2E] ✓ El destino del rescate está claramente FUERA del centro del board (cerca de la rama real), ya no 'en medio de las 2 ramas'.");
                }

                // Verificación adicional: el LED dañado real (Circuit_LED2) debe estar MÁS cerca del
                // slot de rescate que del centro geométrico del board — confirma que efectivamente
                // apunta hacia la rama, no a un punto arbitrario.
                var danado = (GameObject)InvokePrivate(tGuard, guard, "BuscarLedDanado", new object[] { null });
                if (danado != null)
                {
                    float dSlotADanado   = Vector3.Distance(destinoRescate, danado.transform.position);
                    float dCentroADanado = Vector3.Distance(centroBoard, danado.transform.position);
                    Debug.Log($"[Reto2E2E] LED dañado '{danado.name}' pos={danado.transform.position} — " +
                              $"distancia al slot de rescate={dSlotADanado:0.00}m, distancia al centro del board={dCentroADanado:0.00}m");
                    if (dSlotADanado >= dCentroADanado)
                    {
                        Debug.LogError("[Reto2E2E] ✗ El slot de rescate NO está más cerca del LED dañado que el centro del board.");
                        fails++;
                    }
                    else
                    {
                        Debug.Log("[Reto2E2E] ✓ El slot de rescate está más cerca de la rama dañada que el centro del board — coherente con el fix.");
                    }
                }
            }
        }

        Debug.Log(fails == 0
            ? "\n[Reto2E2E] ===== RESULTADO: ✓ Clipboard y LED verificados end-to-end sobre la escena real ====="
            : $"\n[Reto2E2E] ===== RESULTADO: ✗ {fails} verificación(es) fallaron =====");

        Finish(fails);
    }

    static void Finish(int fails)
    {
        if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    static object InvokePrivate(System.Type t, object instance, string method, object[] args = null)
    {
        var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) { Debug.LogError($"[Reto2E2E] No encontré el método privado '{method}'."); return null; }
        return m.Invoke(instance, args ?? new object[0]);
    }

    static object GetPrivateField(System.Type t, object instance, string field)
    {
        var f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        return f?.GetValue(instance);
    }

    static void SetPrivateField(System.Type t, object instance, string field, object value)
    {
        var f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        f?.SetValue(instance, value);
    }
}

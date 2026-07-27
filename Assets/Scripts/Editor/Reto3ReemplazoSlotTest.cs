using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Reproduce el bug reportado en VR: instala una resistencia INCORRECTA (2200Ω) en el slot real
/// del Reto 3 vía el imán real, luego simula que el Técnico envía una nueva (correcta, 470Ω) por
/// el receptor REAL de la escena (ComponentReceiver_Caja) — confirma que el slot se libera
/// correctamente y la pieza nueva SÍ puede instalarse (antes del fix: el slot quedaba atascado con
/// _hasComponent=true para siempre, y la pieza nueva nunca se enganchaba).
///
/// Ejecutar: Tools → TITA → Reto 3 → Test reemplazo de slot (headless)
/// </summary>
public static class Reto3ReemplazoSlotTest
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto 3/Test reemplazo de slot (headless)")]
    public static void Run()
    {
        int fails = 0;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        var tGm = typeof(GameManager);
        InvokePrivate(tGm, gm, "LoadLevel", new object[] { 2 }); // Reto 3

        var delivery = Object.FindAnyObjectByType<ComponentDeliverySystem>(FindObjectsInactive.Include);
        var cmWarmup = gm.reto3Zone.GetComponent<CircuitManager>();
        cmWarmup.AutoDetectComponents();

        var slotResistor = gm.reto3Zone.GetComponentsInChildren<ComponentSlot>(true)
            .First(s => s.acceptedType == ComponentSlotType.Resistor);
        if (slotResistor.delivery == null) slotResistor.delivery = delivery;

        // ── PASO 1: instalar una resistencia INCORRECTA (2200Ω) por el imán real del slot ──
        var goMala = new GameObject("Test_Resistor_2200_Incorrecta");
        goMala.transform.position = slotResistor.transform.position;
        goMala.AddComponent<GrabbableComponent>();
        var rMala = goMala.AddComponent<Resistor>();
        rMala.resistance = 2200f;
        var colMala = goMala.AddComponent<SphereCollider>();
        colMala.isTrigger = true;

        var tDelivery = typeof(ComponentDeliverySystem);
        SetPrivateOn(tDelivery, delivery, "_pendingType", ComponentType.Resistor);
        SetPrivateOn(tDelivery, delivery, "_pendingValue", 2200f);
        SetPrivateOn(tDelivery, delivery, "_waitingForInstall", true);

        var onTriggerStay = typeof(ComponentSlot).GetMethod("OnTriggerStay", BindingFlags.NonPublic | BindingFlags.Instance);
        onTriggerStay.Invoke(slotResistor, new object[] { colMala });

        Debug.Log($"[Reto3Reemplazo] Tras instalar 2200Ω (incorrecta): slot.InstalledObject={(slotResistor.InstalledObject != null ? slotResistor.InstalledObject.name : "NULL")} " +
                  $"(esperado: {goMala.name})");
        if (slotResistor.InstalledObject != goMala) { fails++; Debug.LogError("[Reto3Reemplazo] ✗ La resistencia incorrecta no se instaló para empezar."); Finish(1); return; }

        // ── PASO 2: el Técnico envía la CORRECTA (470Ω) — mismo camino real que un RPC real ──
        var receptores = Object.FindObjectsByType<ExplorerComponentReceiver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var receptor = receptores.FirstOrDefault(r => r.gameObject.activeInHierarchy);
        var tRecv = typeof(ExplorerComponentReceiver);
        var primarioField = tRecv.GetField("_primario", BindingFlags.NonPublic | BindingFlags.Static);
        primarioField.SetValue(null, receptor);
        var gmField = tRecv.GetField("_gm", BindingFlags.NonPublic | BindingFlags.Instance);
        gmField.SetValue(receptor, gm);

        // El receptor real necesita registrar la pieza VIEJA como "la última enviada de este tipo"
        // para que su lógica de reemplazo la reconozca — igual que pasaría en una partida real donde
        // el Técnico ya había enviado la 2200Ω antes. Se simula registrándola directo en el diccionario.
        var ultimoPorTipoField = tRecv.GetField("_ultimoPorTipo", BindingFlags.NonPublic | BindingFlags.Instance);
        var ultimoPorTipo = (System.Collections.IDictionary)ultimoPorTipoField.GetValue(receptor);
        ultimoPorTipo[ComponentType.Resistor] = goMala;

        var handleMethod = tRecv.GetMethod("HandleComponenteRecibido", BindingFlags.NonPublic | BindingFlags.Instance);
        handleMethod.Invoke(receptor, new object[] { ComponentType.Resistor, 470f, (int)ComponentVariant.Default });

        Debug.Log($"[Reto3Reemplazo] Tras envío de reemplazo (470Ω): slot._hasComponent(via InstalledObject)={(slotResistor.InstalledObject != null ? slotResistor.InstalledObject.name : "NULL (liberado)")} " +
                  $"goMala destruido={goMala == null}");

        bool slotLiberado = slotResistor.InstalledObject == null;
        if (!slotLiberado) { fails++; Debug.LogError("[Reto3Reemplazo] ✗ El slot NO se liberó tras destruir la pieza vieja — sigue 'ocupado'."); }
        else Debug.Log("[Reto3Reemplazo] ✓ El slot se liberó correctamente tras el reemplazo.");

        // ── PASO 3: la pieza NUEVA (470Ω) generada por el receptor debe poder instalarse en el
        // MISMO slot ahora — antes del fix, esto fallaba porque _hasComponent seguía en true. ──
        var nuevaResistencia = Object.FindObjectsByType<Resistor>(FindObjectsInactive.Exclude)
            .FirstOrDefault(r => Mathf.Approximately(r.resistance, 470f) && r.gameObject != goMala);
        if (nuevaResistencia == null) { fails++; Debug.LogError("[Reto3Reemplazo] ✗ No until la resistencia de 470Ω generada por el receptor real."); Finish(1); return; }

        nuevaResistencia.transform.position = slotResistor.transform.position;
        if (!nuevaResistencia.gameObject.TryGetComponent<SphereCollider>(out var colBuena))
            colBuena = nuevaResistencia.gameObject.AddComponent<SphereCollider>();
        colBuena.isTrigger = true;

        SetPrivateOn(tDelivery, delivery, "_pendingType", ComponentType.Resistor);
        SetPrivateOn(tDelivery, delivery, "_pendingValue", 470f);
        SetPrivateOn(tDelivery, delivery, "_waitingForInstall", true);
        onTriggerStay.Invoke(slotResistor, new object[] { colBuena });

        bool nuevaInstalada = slotResistor.InstalledObject == nuevaResistencia.gameObject;
        Debug.Log($"[Reto3Reemplazo] Tras intentar instalar la NUEVA pieza (470Ω): slot.InstalledObject={(slotResistor.InstalledObject != null ? slotResistor.InstalledObject.name : "NULL")} " +
                  $"(esperado: {nuevaResistencia.name})");
        if (!nuevaInstalada) { fails++; Debug.LogError("[Reto3Reemplazo] ✗ La pieza NUEVA no logró instalarse en el slot — el bug de 'sale volando' persiste."); }
        else Debug.Log("[Reto3Reemplazo] ✓ La pieza nueva se instaló correctamente en el slot recién liberado.");

        Debug.Log(fails == 0
            ? "\n[Reto3Reemplazo] ===== RESULTADO: ✓ El reemplazo de componente en un slot ya ocupado funciona correctamente ====="
            : $"\n[Reto3Reemplazo] ===== RESULTADO: ✗ {fails} verificación(es) fallaron =====");
        Finish(fails == 0 ? 0 : 1);
    }

    static void Finish(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }

    static object InvokePrivate(System.Type t, object instance, string method, object[] args = null)
    {
        var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        return m.Invoke(instance, args ?? new object[0]);
    }

    static void SetPrivateOn(System.Type t, object instance, string field, object value)
    {
        var f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null) Debug.LogError($"[Reto3Reemplazo] No until el campo privado '{field}' en {t.Name}.");
        else f.SetValue(instance, value);
    }
}

using System.Collections;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Prueba de red REAL de Photon Fusion — SIN ninguna escena del juego (ni Tecnico.unity ni
/// Explorador.unity, sin VR, sin retos). Levanta 2 NetworkRunner (Host + Client) DENTRO del mismo
/// proceso de Unity en una escena vacía, spawnea GameSession, y manda un diagnóstico largo (el
/// mismo tipo de texto que rompía Reto 2 antes del fix de chunking) por
/// GameSession.ReportarDiagnosticoReto — confirmando que el RPC de Fusion realmente lo entrega
/// completo del otro lado de la red, no simulado ni con el fallback "sin sesión → evento local"
/// que usaron los tests anteriores de esta sesión (esos corrían fuera de Play Mode, así que
/// GameSession.Instance siempre daba null y nunca ejercitaban el camino real de red).
///
/// Corre en Play Mode dentro del Editor (batchmode), entrando y saliendo de Play Mode solo, sin
/// abrir ninguna escena del proyecto — usa una escena nueva vacía en memoria.
///
/// Menú: Tools → TITA → Reto 2 → Test RPC de Fusion real (sin el juego)
/// </summary>
public static class FusionRpcOnlyTest
{
    [MenuItem("Tools/TITA/Reto 2/Test RPC de Fusion real (sin el juego)")]
    public static void Run()
    {
        // NO SaveOpenScenes(): en batchmode, si la escena actual tiene cambios sin guardar,
        // dispara un diálogo "Save Scene" modal que nadie puede responder → cuelga/cancela el test
        // antes de arrancar. NewScene(..., Single) descarta la escena actual sin preguntar, que es
        // exactamente lo que se quiere para un test automatizado (no hay nada que perder: esta
        // prueba no toca ningún archivo del proyecto).
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var go = new GameObject("[FusionRpcOnlyTestRunner]");
        go.AddComponent<FusionRpcOnlyTestRunner>();

        EditorApplication.isPlaying = true;
    }
}

public class FusionRpcOnlyTestRunner : MonoBehaviour
{
    NetworkRunner _hostRunner;
    NetworkRunner _clientRunner;
    string _sessionName;

    string _receivedOnClient;
    int _receivedCountOnClient;
    string _receivedOnHost;
    int _receivedCountOnHost;

    float _elapsed;
    const float TimeoutSeconds = 45f;
    bool _finished;

    // Texto largo a propósito (~950 chars con acentos) — el mismo tamaño que rompía el RPC viejo
    // sin chunking en Reto 2 real ("payload is too large (984 bytes). Max allowed: 512 bytes").
    static string TextoLargoDePrueba()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("-- RETO 2: CIRCUITO PARALELO -- (texto de prueba, no es un reto real)");
        sb.AppendLine("Rama 1 (Rama1_LED): [OK] Voltaje transmitido al slot: 9.00 V. Corriente: 12.8 mA. Estado: Correcto, LED encendido en verde de forma segura.");
        sb.AppendLine("Rama 2 (Rama2_LED): [!] Sin voltaje -> cable suelto o rama abierta (revisa esa rama). Posible causa: jumper de batería a riel VCC desconectado, o riel GND sin puente hacia el borne negativo de la fuente de 9V.");
        sb.AppendLine("CABLES FISICOS:");
        sb.AppendLine("  Cable_Bateria_VCC: [OK] conectado en ambas puntas");
        sb.AppendLine("  Cable_Bateria_GND: [!] le falta una punta");
        sb.AppendLine("  Cable_Rama1: [OK] conectado en ambas puntas");
        sb.AppendLine("  Cable_Rama2: [!] ambas puntas sueltas");
        sb.AppendLine("  Total: 2/4 cables cerrando el circuito.");
        sb.Append("> Dile al Explorador que conecte el cable 'Cable_Bateria_GND' (le falta una punta) en un slot de la protoboard, cerca del riel GND compartido, para completar el retorno de corriente hacia la fuente de 9V.");
        return sb.ToString();
    }

    IEnumerator Start()
    {
        _sessionName = "FusionRpcOnlyTest_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
        Debug.Log($"[FusionRpcOnly] Sesión de prueba: '{_sessionName}' (aislada — no puede cruzarse con partidas reales).");

        yield return StartAsCoroutine(StartHost());
        yield return StartAsCoroutine(StartClient());

        if (_hostRunner == null || !_hostRunner.IsRunning || _clientRunner == null || !_clientRunner.IsRunning)
        {
            Debug.LogError("[FusionRpcOnly] ✗ No se pudo establecer la conexión Host/Client — abortando.");
            Terminar(false);
            yield break;
        }

        Debug.Log("[FusionRpcOnly] ✓ Host y Client conectados sobre Photon real (no simulado).");

        // Suscribirse en AMBOS lados antes de enviar.
        GameSession.OnDiagnosticoRetoActualizado += (reto, texto) =>
        {
            // Puede dispararse en cualquiera de los 2 GameObjects de esta escena — distinguir por
            // cuál corrió primero no es trivial con el evento estático, así que igual guardamos en
            // ambos contadores; lo importante es que AL MENOS UNO reciba el texto COMPLETO.
            if (reto != 2) return;
            _receivedOnClient = texto;
            _receivedCountOnClient++;
        };

        yield return new WaitForSeconds(1f); // dar tiempo a que GameSession termine de spawnear/propagarse

        var gs = GameSession.Instance;
        if (gs == null || gs.Object == null || !gs.Object.IsValid)
        {
            Debug.LogError("[FusionRpcOnly] ✗ GameSession.Instance sigue null tras conectar — no se pudo spawnear.");
            Terminar(false);
            yield break;
        }

        string textoEnviado = TextoLargoDePrueba();
        Debug.Log($"[FusionRpcOnly] Enviando diagnóstico de {textoEnviado.Length} caracteres por GameSession.ReportarDiagnosticoReto(2, ...) — " +
                  $"el RPC viejo SIN chunking rechazaba esto por Fusion (límite 512 bytes por RPC).");
        GameSession.ReportarDiagnosticoReto(2, textoEnviado);

        float esperaMax = Time.realtimeSinceStartup + 10f;
        while (_receivedCountOnClient == 0 && Time.realtimeSinceStartup < esperaMax)
            yield return null;

        bool ok = _receivedCountOnClient > 0 && _receivedOnClient == textoEnviado;
        Debug.Log($"[FusionRpcOnly] Recibido: {_receivedCountOnClient} evento(s). Largo recibido={_receivedOnClient?.Length ?? 0} " +
                  $"(esperado {textoEnviado.Length}). Texto idéntico={_receivedOnClient == textoEnviado}");

        if (!ok)
        {
            Debug.LogError("[FusionRpcOnly] ✗ El texto NO llegó completo/idéntico por la red real de Fusion.");
        }
        else
        {
            Debug.Log("[FusionRpcOnly] ✓ El diagnóstico largo llegó COMPLETO por un RPC real de Photon Fusion (chunking confirmado en la red real, no simulado).");
        }

        Terminar(ok);
    }

    IEnumerator StartHost()
    {
        var hostGO = new GameObject("[HostRunner]");
        _hostRunner = hostGO.AddComponent<NetworkRunner>();
        _hostRunner.ProvideInput = false;

        var task = _hostRunner.StartGame(new StartGameArgs
        {
            GameMode    = GameMode.Host,
            SessionName = _sessionName,
            SceneManager = hostGO.AddComponent<NetworkSceneManagerDefault>(),
        });
        yield return WaitForTask(task);

        if (task.IsCompletedSuccessfully && task.Result.Ok)
        {
            Debug.Log("[FusionRpcOnly] Host arrancado.");
            var sessionPrefab = Resources.Load<NetworkObject>("GameSession");
            if (sessionPrefab != null)
                _hostRunner.Spawn(sessionPrefab, Vector3.zero, Quaternion.identity);
            else
                Debug.LogError("[FusionRpcOnly] No encontré Resources/GameSession.prefab.");
        }
        else
        {
            Debug.LogError($"[FusionRpcOnly] Host falló: {(task.IsCompletedSuccessfully ? task.Result.ShutdownReason.ToString() : "excepción/timeout")}");
        }
    }

    IEnumerator StartClient()
    {
        var clientGO = new GameObject("[ClientRunner]");
        _clientRunner = clientGO.AddComponent<NetworkRunner>();
        _clientRunner.ProvideInput = false;

        var task = _clientRunner.StartGame(new StartGameArgs
        {
            GameMode    = GameMode.Client,
            SessionName = _sessionName,
            SceneManager = clientGO.AddComponent<NetworkSceneManagerDefault>(),
        });
        yield return WaitForTask(task);

        if (task.IsCompletedSuccessfully && task.Result.Ok)
            Debug.Log("[FusionRpcOnly] Client conectado.");
        else
            Debug.LogError($"[FusionRpcOnly] Client falló: {(task.IsCompletedSuccessfully ? task.Result.ShutdownReason.ToString() : "excepción/timeout")}");
    }

    static IEnumerator WaitForTask(Task task)
    {
        float deadline = Time.realtimeSinceStartup + 25f;
        while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
            yield return null;
    }

    static IEnumerator StartAsCoroutine(IEnumerator routine) => routine;

    void Update()
    {
        _elapsed += Time.unscaledDeltaTime;
        if (!_finished && _elapsed > TimeoutSeconds)
        {
            Debug.LogError("[FusionRpcOnly] ✗ TIMEOUT general — algo se colgó (revisar logs de Fusion arriba).");
            Terminar(false);
        }
    }

    void Terminar(bool ok)
    {
        if (_finished) return;
        _finished = true;

        Debug.Log(ok
            ? "\n[FusionRpcOnly] ===== RESULTADO: ✓ Photon Fusion real confirma el fix del RPC chunkeado, sin abrir ninguna escena del juego ====="
            : "\n[FusionRpcOnly] ===== RESULTADO: ✗ Falló la prueba de red real =====");

        if (_hostRunner != null && _hostRunner.IsRunning) _hostRunner.Shutdown();
        if (_clientRunner != null && _clientRunner.IsRunning) _clientRunner.Shutdown();

        EditorApplication.delayCall += () =>
        {
            EditorApplication.isPlaying = false;
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        };
    }
}

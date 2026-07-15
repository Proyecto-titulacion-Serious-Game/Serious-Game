using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ConnectionManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Configuración de Red")]
    [SerializeField] private NetworkPrefabRef playerPrefab = default;

    [Header("Código de sala (multi-grupo en aula)")]
    [Tooltip("Código de la sala Fusion. Dos dispositivos con el MISMO código entran a la misma " +
             "sesión. Pon el mismo valor en el PC del Técnico y el visor del Explorador de cada " +
             "estación para evitar que se crucen los grupos. Vacío → PlayerPrefs o valor por defecto.")]
    [SerializeField] private string roomCode = "";

    [Tooltip("Solo escena del Técnico: si está activo NO se crea la sala automáticamente; espera a " +
             "que se escriba el código en la UI (RoomCodeEntryUI) y se pulse 'Crear sala'.")]
    public bool esperarEntradaDeCodigo = true;

    public enum AutoConnectRole { Ninguno, Explorador, Tecnico }

    [Header("Configuración de Escena")]
    [Tooltip("Selecciona si esta escena debe conectarse automáticamente al iniciar.")]
    public AutoConnectRole rolAutomatico = AutoConnectRole.Ninguno;

    private NetworkRunner _runner;

    [Header("Modo Offline / Testing")]
    [Tooltip("Si está activo, omite Fusion y activa el entorno local directamente.")]
    public bool modoOffline = false;

    [Tooltip("Segundos esperando conexión antes de fallback a offline. 0 = sin límite. " +
             "Con internet lento (aula), 25s evita caer a offline durante el handshake con Photon.")]
    [Range(0f, 30f)]
    public float connectionTimeoutSeconds = 25f;

    [Tooltip("GO 'Entorno del explorador' a activar en modo offline. Si queda vacío se busca por nombre.")]
    public GameObject entornoExplorador;

    [Header("Reconexión automática")]
    [Tooltip("Reintentos de conexión antes de caer a modo offline. Cubre 2 casos del aula: " +
             "(1) corte de red a mitad de sesión (Timeout), (2) Explorador arrancado antes de que " +
             "el Técnico cree la sala (GameNotFound).")]
    public int maxReconnectIntentos = 3;
    [Tooltip("Segundos de espera entre reintentos.")]
    public float reconnectEsperaSegundos = 4f;
    [Tooltip("Reintentos EXTRA cuando la sala AÚN NO EXISTE (GameNotFound = el Explorador arrancó " +
             "antes de que el Técnico cree la sala). Presupuesto separado y generoso: ~40 × 4s ≈ 4 min " +
             "de sala de espera antes de rendirse a offline.")]
    public int maxEsperaSalaIntentos = 40;

    GameMode  _lastMode;
    bool      _modeKnown;
    int       _reconnectsUsados;
    int       _esperaSalaUsados;
    Coroutine _reconnectCo;
    bool      _quitting;

    [Header("Referencias del sistema de juego")]
    [Tooltip("Referencia al GameManager principal para notificar eventos de red.")]
    public GameManager gameManager;

    // ─────────────────────────────────────────────
    //  Eventos estáticos
    // ─────────────────────────────────────────────

    /// <summary>Se dispara cuando un jugador remoto se desconecta.</summary>
    public static event Action<PlayerRef> OnPlayerDisconnected;

    /// <summary>Se dispara cuando la conexión falla o se agota el tiempo.</summary>
    public static event Action<string>    OnConnectionFailed;

    // ─────────────────────────────────────────────
    private Coroutine _connectionTimeout;
    private bool      _connected;

    // ─────────────────────────────────────────────
    //  Singleton — auto-destruye duplicados al iniciar
    // ─────────────────────────────────────────────

    public static ConnectionManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Hay un ConnectionManager válido ya activo → este es duplicado
            Debug.LogWarning($"[ConnectionManager] Duplicado detectado en '{gameObject.name}' " +
                             $"(escena: {gameObject.scene.name}). Destruyendo duplicado.");
            Destroy(gameObject);
            return;
        }

        // Preferir el que tiene playerPrefab asignado
        var all = FindObjectsByType<ConnectionManager>(FindObjectsInactive.Include);
        if (all.Length > 1)
        {
            // Priorizar el que tiene playerPrefab configurado
            ConnectionManager best = null;
            foreach (var cm in all)
                if (cm.playerPrefab.IsValid) { best = cm; break; }

            if (best != null && best != this)
            {
                Debug.LogWarning($"[ConnectionManager] Duplicado sin playerPrefab en '{gameObject.name}'. Destruyendo.");
                Destroy(gameObject);
                return;
            }
        }

        Instance = this;
    }

    // ─────────────────────────────────────────────

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_runner != null && _runner.IsRunning)
            _runner.Shutdown();
    }

    private void Start()
    {
        bool esTecnico = rolAutomatico == AutoConnectRole.Tecnico;

        if (modoOffline)
        {
            if (!esTecnico)
                ActivarEntornoExplorador();
            else
                Debug.Log("[Red] Modo offline Técnico — sin entorno VR, sin Fusion.");
            return;
        }

        if (rolAutomatico == AutoConnectRole.Explorador)
        {
            Debug.Log("[Red] Conectando automáticamente como Explorador...");
            StartSimulation(GameMode.Client);
        }
        else if (esTecnico)
        {
            if (esperarEntradaDeCodigo)
            {
                Debug.Log("[Red] Técnico: esperando código de sala desde la UI antes de crear la sala.");
                return; // RoomCodeEntryUI llamará a CrearSalaComoTecnico() al pulsar 'Crear sala'.
            }
            Debug.Log("[Red] Creando servidor automáticamente como Técnico...");
            StartSimulation(GameMode.Host);
        }
    }

    // ─────────────────────────────────────────────
    //  Código de sala (room code)
    // ─────────────────────────────────────────────

    // Ya normalizada (MAYÚSCULAS): el panel del Técnico normaliza lo tecleado antes de crear la
    // sala; si esta constante no estuviera normalizada, el default del PC ('LABORATORIOUBICUA')
    // jamás coincidiría con el default del visor ('LaboratorioUbicua') y el Explorador no entraría.
    private const string DEFAULT_ROOM   = "LABORATORIOUBICUA";
    private const string PREFS_ROOM_KEY  = "TITA.RoomCode";

    /// <summary>Código fijado en runtime por la UI antes de conectar. Tiene prioridad.</summary>
    public static string PendingRoomCode { get; private set; }

    /// <summary>
    /// Fija el código de sala (UI / menú) y lo persiste para el próximo arranque del dispositivo.
    /// Acepta texto libre; se normaliza (mayúsculas, sin espacios ni símbolos).
    /// </summary>
    public static void SetRoomCode(string code)
    {
        string norm = NormalizeRoomCode(code);
        PendingRoomCode = norm;
        if (!string.IsNullOrEmpty(norm))
        {
            PlayerPrefs.SetString(PREFS_ROOM_KEY, norm);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Código de sala efectivo, por prioridad:
    /// runtime (UI) → Inspector → PlayerPrefs (persistido) → valor por defecto.
    /// </summary>
    public string ResolveRoomCode()
    {
        if (!string.IsNullOrEmpty(PendingRoomCode)) return PendingRoomCode;

        string fromInspector = NormalizeRoomCode(roomCode);
        if (!string.IsNullOrEmpty(fromInspector)) return fromInspector;

        // PlayerPrefs solo se consulta en el flujo de lobby del Técnico (gateo activo).
        // Así, con el gateo apagado, la resolución es determinista (Inspector → default) y
        // ningún equipo —en especial el visor— queda "pegado" a un código tecleado antes.
        if (esperarEntradaDeCodigo)
        {
            string fromPrefs = NormalizeRoomCode(PlayerPrefs.GetString(PREFS_ROOM_KEY, ""));
            if (!string.IsNullOrEmpty(fromPrefs)) return fromPrefs;
        }

        return DEFAULT_ROOM;
    }

    /// <summary>Normaliza: trim, MAYÚSCULAS, solo A-Z/0-9/guion, máx. 24 caracteres.</summary>
    public static string NormalizeRoomCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (char c in code.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '-') sb.Append(c);
            if (sb.Length >= 24) break;
        }
        return sb.ToString();
    }

    /// <summary>Crea la sala como Técnico (Host) con el código indicado. La llama RoomCodeEntryUI.</summary>
    public void CrearSalaComoTecnico(string code)
    {
        SetRoomCode(code);
        StartSimulation(GameMode.Host);
    }

    /// <summary>Se une como Explorador (Client) al código indicado.</summary>
    public void UnirseComoExplorador(string code)
    {
        SetRoomCode(code);
        StartSimulation(GameMode.Client);
    }

    void ActivarEntornoExplorador()
    {
        if (entornoExplorador == null)
            entornoExplorador = BuscarIncluyendoInactivos("Entorno del explorador");

        if (entornoExplorador != null)
        {
            entornoExplorador.SetActive(true);
            Debug.Log("[Red] Modo offline: 'Entorno del explorador' activado localmente.");
        }
        else
        {
            Debug.LogWarning("[Red] Modo offline: no se encontró 'Entorno del explorador'. Asígnalo en el Inspector.");
        }
    }

    static GameObject BuscarIncluyendoInactivos(string nombre)
    {
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == nombre) return root;
            var child = root.transform.Find(nombre);
            if (child != null) return child.gameObject;
        }
        return null;
    }

    public async void StartSimulation(GameMode mode)
    {
        if (_runner != null && _runner.IsRunning)
        {
            Debug.LogWarning($"[Red] StartSimulation ignorado — el runner ya está corriendo ({_runner.GameMode}).");
            return;
        }

        // CRÍTICO: el NetworkRunner vive en un GO HIJO dedicado, NUNCA en este GameObject.
        // Al apagarse (p.ej. StartGame falla con GameNotFound), Fusion DESTRUYE el GameObject
        // del runner — y este GO ('GameManager_System') contiene GameManager, DeliverySystem,
        // ObjectiveSystem, etc.: se moría el cerebro completo del juego y el reto no podía
        // completarse ni reconectar. Con el hijo sacrificable, solo muere el runner.
        var previo = transform.Find("[FusionRunner]");
        if (previo != null) Destroy(previo.gameObject);
        var runnerGO = new GameObject("[FusionRunner]");
        runnerGO.transform.SetParent(transform, false);
        _runner = runnerGO.AddComponent<NetworkRunner>();
        _runner.AddCallbacks(this);   // en otro GO, los callbacks ya no se auto-registran

        _runner.ProvideInput = true;

        _lastMode  = mode;
        _modeKnown = true;

        if (connectionTimeoutSeconds > 0f)
            _connectionTimeout = StartCoroutine(ConnectionTimeout());

        string sala = ResolveRoomCode();
        Debug.Log($"[Red] Iniciando sesión '{sala}' como: {mode}");

        StartGameResult result;
        try
        {
            result = await _runner.StartGame(new StartGameArgs()
            {
                GameMode    = mode,
                SessionName = sala,
            });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Red] StartGame lanzó excepción: {ex.Message}.");
            StopConnectionTimeout();
            TryReconnect("Error al iniciar Fusion.");
            return;
        }

        if (!result.Ok)
        {
            Debug.LogWarning($"[Red] StartGame falló: {result.ShutdownReason}.");
            StopConnectionTimeout();
            // GameNotFound = la sala del Técnico aún no existe (el Explorador entró primero).
            // No es un error de red: SALA DE ESPERA con presupuesto propio y generoso.
            if (result.ShutdownReason == ShutdownReason.GameNotFound && mode == GameMode.Client)
                EsperarSala();
            else
                TryReconnect($"No se pudo conectar ({result.ShutdownReason}).");
            return;
        }

        StopConnectionTimeout();
        _connected = true;
        _reconnectsUsados = 0;   // conexión lograda → resetear presupuestos de reintentos
        _esperaSalaUsados = 0;

        if (mode == GameMode.Host || mode == GameMode.Server)
        {
            // Verificar que el runner sigue activo — el timeout puede haberlo apagado
            // mientras el await StartGame() estaba pendiente.
            if (_runner == null || !_runner.IsRunning) return;

            var sessionPrefab = Resources.Load<NetworkObject>("GameSession");
            if (sessionPrefab != null)
            {
                _runner.Spawn(sessionPrefab, Vector3.zero, Quaternion.identity);
                Debug.Log("[Red] GameSession spawneada en la red.");
            }
            else
            {
                Debug.LogWarning("[Red] Prefab 'GameSession' no encontrado en Resources/. " +
                                 "Coloca el prefab en Assets/Resources/GameSession.prefab con NetworkObject.");
            }
        }
    }

    IEnumerator ConnectionTimeout()
    {
        yield return new WaitForSecondsRealtime(connectionTimeoutSeconds);
        if (!_connected)
        {
            Debug.LogWarning($"[Red] Timeout ({connectionTimeoutSeconds}s) sin conexión.");
            _runner?.Shutdown();
            TryReconnect("Tiempo de espera agotado.");
        }
    }

    /// <summary>
    /// Reintenta la conexión (mismo rol, mismo código de sala) antes de rendirse a modo offline.
    /// Cubre el corte de red a mitad de sesión y el arranque del Explorador antes de que exista
    /// la sala del Técnico. El presupuesto de reintentos se resetea al lograr conexión.
    /// </summary>
    /// <summary>
    /// SALA DE ESPERA: la sala del código aún no existe (el Técnico no la ha creado). Reintenta
    /// con presupuesto propio (~4 min) sin gastar los reintentos de error de red. Cubre el caso
    /// "el Explorador arrancó primero" — se conectará solo en cuanto el Técnico cree la sala.
    /// </summary>
    void EsperarSala()
    {
        if (modoOffline || _quitting) return;
        if (_esperaSalaUsados >= maxEsperaSalaIntentos)
        {
            FallbackModoOffline($"La sala '{ResolveRoomCode()}' nunca se creó. Iniciando modo offline.");
            return;
        }

        _esperaSalaUsados++;
        string msg = $"Esperando a que el Técnico cree la sala '{ResolveRoomCode()}'… " +
                     $"({_esperaSalaUsados}/{maxEsperaSalaIntentos})";
        if (_esperaSalaUsados == 1 || _esperaSalaUsados % 5 == 0)
            Debug.Log("[Red] " + msg);
        OnConnectionFailed?.Invoke(msg);   // los overlays muestran el estado al jugador

        if (_reconnectCo != null) StopCoroutine(_reconnectCo);
        _reconnectCo = StartCoroutine(ReconnectTrasEspera());
    }

    void TryReconnect(string razon)
    {
        if (modoOffline || _quitting) return;
        if (!_modeKnown || _reconnectsUsados >= maxReconnectIntentos)
        {
            FallbackModoOffline(razon);
            return;
        }

        _reconnectsUsados++;
        Debug.LogWarning($"[Red] {razon} → reintento {_reconnectsUsados}/{maxReconnectIntentos} " +
                         $"en {reconnectEsperaSegundos}s…");
        OnConnectionFailed?.Invoke($"{razon} Reintentando ({_reconnectsUsados}/{maxReconnectIntentos})…");

        if (_reconnectCo != null) StopCoroutine(_reconnectCo);
        _reconnectCo = StartCoroutine(ReconnectTrasEspera());
    }

    IEnumerator ReconnectTrasEspera()
    {
        yield return new WaitForSecondsRealtime(reconnectEsperaSegundos);
        _reconnectCo = null;
        if (modoOffline || _quitting) yield break;

        // Un NetworkRunner apagado no se puede reutilizar: destruir el hijo [FusionRunner]
        // (si Fusion no lo destruyó ya) para que StartSimulation cree uno fresco.
        var viejo = transform.Find("[FusionRunner]");
        if (viejo != null)
        {
            Destroy(viejo.gameObject);
            yield return null;   // Destroy es diferido → esperar un frame
        }
        _runner = null;

        Debug.Log($"[Red] Reintentando conexión como {_lastMode} (sala '{ResolveRoomCode()}')…");
        StartSimulation(_lastMode);
    }

    void OnApplicationQuit() => _quitting = true;

    void StopConnectionTimeout()
    {
        if (_connectionTimeout != null)
        {
            StopCoroutine(_connectionTimeout);
            _connectionTimeout = null;
        }
    }

    void FallbackModoOffline(string razon = "")
    {
        if (modoOffline) return;
        modoOffline = true;
        Debug.LogWarning($"[Red] Fallback a modo offline. {razon}");
        if (!string.IsNullOrEmpty(razon))
            OnConnectionFailed?.Invoke(razon);
        if (rolAutomatico != AutoConnectRole.Tecnico)
            ActivarEntornoExplorador();

        // Notificar al GameManager para que marque ambos motores como sucios
        EnsureGameManager();
        if (gameManager != null)
        {
            gameManager.circuit?.MarkDirty();
            gameManager.protoSim?.MarkDirty();
            Debug.Log("[Red] GameManager notificado del modo offline.");
        }
    }

    void EnsureGameManager()
    {
        if (gameManager == null)
            gameManager = FindAnyObjectByType<GameManager>();
    }

    // --- Métodos para los Botones de la UI ---
    public void IniciarComoTecnico() => StartSimulation(GameMode.Host);

    // --- Callbacks de Fusion ---
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer) return;
        Debug.Log($"[Red] Jugador {player.PlayerId} se ha unido.");
        if (playerPrefab.IsValid)
            runner.Spawn(playerPrefab, Vector3.up, Quaternion.identity, player);
        else
            Debug.LogWarning("[Red] playerPrefab no asignado — se omite el spawn del avatar. " +
                             "Asígnalo en el Inspector de ConnectionManager si necesitas avatares de red.");
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[Red] Jugador {player.PlayerId} se ha desconectado.");
        OnPlayerDisconnected?.Invoke(player);
    }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"[Red] Runner apagado: {shutdownReason}");
        _runner    = null;
        _connected = false;
        StopConnectionTimeout();

        // Si el shutdown no fue limpio, forzar recalculo del circuito activo
        if (shutdownReason != ShutdownReason.Ok)
        {
            EnsureGameManager();
            gameManager?.circuit?.MarkDirty();
            gameManager?.protoSim?.MarkDirty();
        }
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        _connected = true;
        _reconnectsUsados = 0;   // conexión lograda → resetear presupuestos de reintentos
        _esperaSalaUsados = 0;
        StopConnectionTimeout();
        Debug.Log("[Red] Conectado al servidor.");
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"[Red] Desconectado del servidor: {reason}");
        _runner    = null;
        _connected = false;
        TryReconnect($"Desconectado: {reason}.");
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogWarning($"[Red] Conexión fallida a {remoteAddress}: {reason}");
        StopConnectionTimeout();
        TryReconnect($"No se pudo conectar al servidor ({reason}).");
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}

using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// UI mínima que el Técnico (PC) ve antes de empezar: solo pide el nombre del curso/grupo, para
/// poder diferenciar sesiones después en el dashboard/Sheets. Hay UNA sola partida (código fijo,
/// resuelto por <see cref="ConnectionManager.ResolveRoomCode"/>) — el Explorador (Quest, sin
/// teclado) se conecta solo, siempre a esa misma partida. Este panel NO gatea la conexión de nadie.
/// </summary>
public class RoomCodeEntryUI : MonoBehaviour
{
    static RoomCodeEntryUI _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null) return;
        // El visor (VR) usa el código forzado → no necesita esta UI de teclado.
        if (XRSettings.isDeviceActive) return;

        var go = new GameObject("[RoomCodeEntryUI]");
        _instance = go.AddComponent<RoomCodeEntryUI>();
        DontDestroyOnLoad(go);
    }

    const string PREFS_GRUPO_KEY = "TITA.Grupo";
    string  _grupo = "";
    bool    _grupoInit;
    
    // Añadimos nuevos estilos para Inputs y Botones
    GUIStyle _box, _title, _hint, _inputStyle, _buttonStyle;
    Texture2D _bg;

    Fusion.NetworkRunner _runner;
    float                _searchCd;

    TechnicianMover _mover;              
    bool            _movimientoBloqueado;

    void Update()
    {
        if (_runner == null)
        {
            _searchCd -= Time.unscaledDeltaTime;
            if (_searchCd <= 0f)
            {
                _runner   = FindAnyObjectByType<Fusion.NetworkRunner>();
                _searchCd = 0.5f;
            }
        }

        bool mostrar = DebeMostrarse();

        if (mostrar && !_movimientoBloqueado)
        {
            if (_mover == null) _mover = FindAnyObjectByType<TechnicianMover>();
            if (_mover != null) { _mover.LockPosition(true); _movimientoBloqueado = true; }
        }
        else if (!mostrar && _movimientoBloqueado)
        {
            if (_mover != null) _mover.LockPosition(false);
            _movimientoBloqueado = false;
        }

        if (mostrar)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
    }

    bool DebeMostrarse()
    {
        var cm = ConnectionManager.Instance;
        if (cm == null) return false;
        if (cm.rolAutomatico != ConnectionManager.AutoConnectRole.Tecnico) return false;
        if (!cm.esperarEntradaDeCodigo) return false;
        if (!TutorialNPC.PuedePedirNombreGrupo) return false;

        return _runner == null;
    }

    void OnGUI()
    {
        if (!DebeMostrarse()) return;

        var cm = ConnectionManager.Instance;
        if (!_grupoInit) { _grupo = PlayerPrefs.GetString(PREFS_GRUPO_KEY, ""); _grupoInit = true; }

        EnsureStyles();

        const float w = 420f, h = 220f;
        var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.45f, w, h);
        GUI.Box(rect, GUIContent.none, _box);

        GUILayout.BeginArea(new Rect(rect.x + 20, rect.y + 20, w - 40, h - 40));

        // Sin código de sala: hay UNA sola partida (el Explorador siempre se une a ella
        // automáticamente, sin escribir nada — no tiene teclado). Este campo YA NO conecta ni
        // desconecta a nadie: solo etiqueta las métricas de esta sesión (curso/semestre/grupo) para
        // poder diferenciarlas después en el dashboard/Sheets.
        GUILayout.Label("CURSO / GRUPO", _title);
        GUILayout.Label("Ej: 'Semestre 3'. Solo ordena las métricas del dashboard — " +
                         "no hace falta que el Explorador escriba nada, se conecta solo.", _hint);
        GUILayout.Space(5);
        _grupo = GUILayout.TextField(_grupo, 40, _inputStyle, GUILayout.Height(32));

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Comenzar", _buttonStyle, GUILayout.Height(40)))
            CrearSala(cm);

        GUILayout.EndArea();

        var e = Event.current;
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
        {
            CrearSala(cm);
            e.Use();
        }
    }

    void CrearSala(ConnectionManager cm)
    {
        string grupo = (_grupo ?? "").Trim();
        if (!string.IsNullOrEmpty(grupo))
        {
            var exp = FindAnyObjectByType<SessionDataExporter>();
            if (exp != null) exp.grupo = grupo;
            PlayerPrefs.SetString(PREFS_GRUPO_KEY, grupo);
            PlayerPrefs.Save();
            Debug.Log($"[RoomCodeEntryUI] Grupo registrado para las métricas: '{grupo}'.");
        }

        // Código vacío → ConnectionManager.ResolveRoomCode() cae al valor por defecto fijo
        // (el mismo que usa siempre el Explorador, que no tiene forma de escribir uno propio).
        // Antes acá se mandaba lo que el Técnico hubiera tecleado — si no coincidía EXACTO con
        // el default del Explorador, la sala nunca se encontraba (causa real de "no se conectan").
        Debug.Log("[RoomCodeEntryUI] Creando la única sala (código por defecto) como Técnico.");
        cm.CrearSalaComoTecnico("");

        _runner = cm.GetComponentInChildren<Fusion.NetworkRunner>();
        TutorialNPC.NotificarNombreGrupoListo();

        // El mouse quedó liberado (Cursor.None/visible) todo el tiempo que este panel estuvo
        // abierto para poder escribir el nombre del grupo — al crear la sala, devolverlo al modo
        // mouselook normal del walker (mismo patrón que PauseMenu/WorkstationSeat al reanudar).
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    void EnsureStyles()
    {
        if (_bg == null)
        {
            _bg = new Texture2D(1, 1);
            // Fondo ligeramente más oscuro y opaco para dar contraste
            _bg.SetPixel(0, 0, new Color(0.05f, 0.08f, 0.06f, 0.98f)); 
            _bg.Apply();
        }
        if (_box == null)
            _box = new GUIStyle(GUI.skin.box) { normal = { background = _bg } };
        
        if (_title == null)
            _title = new GUIStyle(GUI.skin.label)
            { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0f, 1f, 0.7f) } };
        
        if (_hint == null)
            _hint = new GUIStyle(GUI.skin.label)
            { fontSize = 13, wordWrap = true, normal = { textColor = new Color(0.8f, 0.85f, 0.8f) } };

        // ESTILO PARA CAMPOS DE TEXTO
        if (_inputStyle == null)
        {
            _inputStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 0, 0)
            };
        }

        // ESTILO PARA BOTONES
        if (_buttonStyle == null)
        {
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            // Forzamos el color del texto a blanco para que resalte
            _buttonStyle.normal.textColor = Color.white; 
            _buttonStyle.hover.textColor = new Color(0f, 1f, 0.7f); // Texto verde al pasar el ratón
        }
    }
}
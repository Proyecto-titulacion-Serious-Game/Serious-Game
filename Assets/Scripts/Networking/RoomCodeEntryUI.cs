using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// UI mínima para que el Técnico (PC) escriba el CÓDIGO DE SALA antes de crear la partida.
/// Resuelve el problema de aula: con <see cref="ConnectionManager"/> usando un SessionName fijo,
/// 15 grupos en el mismo Wi-Fi caían todos en la misma sala. Ahora cada estación usa su código.
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

    string  _code;
    bool    _codeInit;
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
        if (!_codeInit)  { _code  = cm.ResolveRoomCode(); _codeInit = true; }
        if (!_grupoInit) { _grupo = PlayerPrefs.GetString(PREFS_GRUPO_KEY, ""); _grupoInit = true; }

        EnsureStyles();

        // 1. Aumentamos el tamaño general del panel
        const float w = 420f, h = 350f; 
        var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.45f, w, h);
        GUI.Box(rect, GUIContent.none, _box);

        GUILayout.BeginArea(new Rect(rect.x + 20, rect.y + 20, w - 40, h - 40));

        GUILayout.Label("NOMBRE DEL GRUPO", _title);
        GUILayout.Label("Estudiantes de esta estación.", _hint);
        GUILayout.Space(5);
        // 2. Aplicamos el nuevo estilo de input
        _grupo = GUILayout.TextField(_grupo, 40, _inputStyle, GUILayout.Height(32));

        GUILayout.Space(15);
        
        GUILayout.Label("CÓDIGO DE SALA", _title);
        GUILayout.Label("Cada estación usa su propio código. El Explorador debe tener este mismo código.", _hint);
        GUILayout.Space(5);

        GUI.SetNextControlName("RoomCodeField");
        // Aplicamos el nuevo estilo de input
        _code = GUILayout.TextField(_code, 24, _inputStyle, GUILayout.Height(32));

        // 3. FlexibleSpace empuja los botones hacia el fondo del área
        GUILayout.FlexibleSpace(); 
        
        GUILayout.BeginHorizontal();

        // 4. Aplicamos el nuevo estilo de botón y aumentamos la altura
        if (GUILayout.Button("Crear sala", _buttonStyle, GUILayout.Height(40)))
            CrearSala(cm);

        GUILayout.Space(10); // Separación entre botones

        if (GUILayout.Button("Aleatorio", _buttonStyle, GUILayout.Width(100), GUILayout.Height(40)))
            _code = $"UDLA-{Random.Range(1000, 9999)}";

        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        var e = Event.current;
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            && GUI.GetNameOfFocusedControl() == "RoomCodeField")
        {
            CrearSala(cm);
            e.Use();
        }
    }

    void CrearSala(ConnectionManager cm)
    {
        string norm = ConnectionManager.NormalizeRoomCode(_code);
        if (string.IsNullOrEmpty(norm))
        {
            Debug.LogWarning("[RoomCodeEntryUI] Código vacío o inválido — escribe algo como 'UDLA-A4'.");
            return;
        }

        string grupo = (_grupo ?? "").Trim();
        if (!string.IsNullOrEmpty(grupo))
        {
            var exp = FindAnyObjectByType<SessionDataExporter>();
            if (exp != null) exp.grupo = grupo;
            PlayerPrefs.SetString(PREFS_GRUPO_KEY, grupo);
            PlayerPrefs.Save();
            Debug.Log($"[RoomCodeEntryUI] Grupo registrado para las métricas: '{grupo}'.");
        }

        Debug.Log($"[RoomCodeEntryUI] Creando sala '{norm}' como Técnico.");
        cm.CrearSalaComoTecnico(norm);

        _runner = cm.GetComponentInChildren<Fusion.NetworkRunner>();
        TutorialNPC.NotificarNombreGrupoListo();
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
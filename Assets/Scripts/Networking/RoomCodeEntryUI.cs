using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// UI mínima para que el Técnico (PC) escriba el CÓDIGO DE SALA antes de crear la partida.
/// Resuelve el problema de aula: con <see cref="ConnectionManager"/> usando un SessionName fijo,
/// 15 grupos en el mismo Wi-Fi caían todos en la misma sala. Ahora cada estación usa su código.
///
/// 100% aditivo y sin cablear nada:
///   • Se auto-crea al cargar la escena (RuntimeInitializeOnLoadMethod), igual que NetworkDemoOverlay.
///   • Solo se dibuja en PC (no XR) cuando hay un ConnectionManager con rol Técnico y
///     <see cref="ConnectionManager.esperarEntradaDeCodigo"/> = true, y aún no hay sesión activa.
///   • Al pulsar "Crear sala" llama a <see cref="ConnectionManager.CrearSalaComoTecnico"/>.
///
/// El Explorador (VR) toma el mismo código de forma forzada (Inspector o PlayerPrefs del visor),
/// así no necesita teclado virtual. Para emparejar una estación: pon el mismo código en el PC
/// (aquí) y en el campo Room Code del ConnectionManager del visor.
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
    bool     _codeInit;
    const string PREFS_GRUPO_KEY = "TITA.Grupo";
    string  _grupo = "";
    bool    _grupoInit;
    GUIStyle _box, _title, _hint;
    Texture2D _bg;

    // Runner cacheado para no hacer FindAnyObjectByType en cada OnGUI (corre 2+ veces/frame).
    Fusion.NetworkRunner _runner;
    float                _searchCd;

    void Update()
    {
        // Refrescar el cache del runner de forma barata (~2 Hz). Unity sobrecarga ==,
        // así que un runner destruido vuelve a leerse como null y el panel reaparece.
        if (_runner == null)
        {
            _searchCd -= Time.unscaledDeltaTime;
            if (_searchCd <= 0f)
            {
                _runner   = FindAnyObjectByType<Fusion.NetworkRunner>();
                _searchCd = 0.5f;
            }
        }
    }

    bool DebeMostrarse()
    {
        var cm = ConnectionManager.Instance;
        if (cm == null) return false;
        if (cm.rolAutomatico != ConnectionManager.AutoConnectRole.Tecnico) return false;
        if (!cm.esperarEntradaDeCodigo) return false;

        // En cuanto EXISTE un runner (aunque StartGame aún esté en curso) ocultamos el panel.
        // Esto evita que un segundo clic en 'Crear sala' dispare StartSimulation otra vez
        // durante el arranque asíncrono.
        return _runner == null;
    }

    void OnGUI()
    {
        if (!DebeMostrarse()) return;

        var cm = ConnectionManager.Instance;
        if (!_codeInit)  { _code  = cm.ResolveRoomCode(); _codeInit = true; }
        if (!_grupoInit) { _grupo = PlayerPrefs.GetString(PREFS_GRUPO_KEY, ""); _grupoInit = true; }

        EnsureStyles();

        const float w = 380f, h = 290f;
        var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.45f, w, h);
        GUI.Box(rect, GUIContent.none, _box);

        GUILayout.BeginArea(new Rect(rect.x + 18, rect.y + 16, w - 36, h - 32));

        GUILayout.Label("NOMBRE DEL GRUPO", _title);
        GUILayout.Label("Estudiantes de esta estación (aparece en las métricas).\n" +
                        "Ej: Grupo 3 - Ana y Luis", _hint);
        GUILayout.Space(4);
        _grupo = GUILayout.TextField(_grupo, 40, GUILayout.Height(28));

        GUILayout.Space(10);
        GUILayout.Label("CÓDIGO DE SALA", _title);
        GUILayout.Label("Cada estación usa su propio código. El Explorador debe tener\n" +
                        "el MISMO código (Inspector del visor). Ej: UDLA-A4", _hint);
        GUILayout.Space(8);

        GUI.SetNextControlName("RoomCodeField");
        _code = GUILayout.TextField(_code, 24, GUILayout.Height(30));

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Crear sala", GUILayout.Height(34)))
            CrearSala(cm);

        if (GUILayout.Button("Aleatorio", GUILayout.Width(90), GUILayout.Height(34)))
            _code = $"UDLA-{Random.Range(1000, 9999)}";

        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        // Enter en el campo también crea la sala.
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

        // Nombre del grupo (estudiantes): viaja a las métricas (Sheets columna 'Equipo',
        // dashboard localhost, CSV). Se recuerda entre sesiones en este PC.
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

        // StartSimulation añade el NetworkRunner de forma síncrona (antes del primer await).
        // OJO: ahora vive en el HIJO [FusionRunner] (aislado para que Fusion no destruya el
        // GameManager al fallar) — buscarlo en children, no en el propio GO.
        _runner = cm.GetComponentInChildren<Fusion.NetworkRunner>();
    }

    void EnsureStyles()
    {
        if (_bg == null)
        {
            _bg = new Texture2D(1, 1);
            _bg.SetPixel(0, 0, new Color(0.02f, 0.05f, 0.04f, 0.92f));
            _bg.Apply();
        }
        if (_box == null)
            _box = new GUIStyle(GUI.skin.box) { normal = { background = _bg } };
        if (_title == null)
            _title = new GUIStyle(GUI.skin.label)
            { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0f, 1f, 0.7f) } };
        if (_hint == null)
            _hint = new GUIStyle(GUI.skin.label)
            { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.8f, 0.85f, 0.8f) } };
    }
}

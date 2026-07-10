using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

/// <summary>
/// NPC tutorial del Técnico (avatar RobotKyle) con globo de texto sobre la cabeza.
///
/// Muestra, por reto, qué debe hacer cada rol y cómo. La HISTORIA de contexto
/// (por qué cada rol hace lo que hace) se escribe en el Inspector — campo
/// <see cref="historiaContexto"/> — para que el docente/autor la redacte a su gusto
/// sin tocar código. Lo mismo con las guías por reto (<see cref="guiaPorReto"/>).
///
/// Controles (PC del Técnico): tecla N = siguiente página · tecla H = mostrar/ocultar globo.
/// Al cambiar de reto (GameManager.OnLevelLoaded) el globo salta solo a la guía de ese reto.
///
/// Setup: Tools → TITA → Técnico → Setup NPC Tutorial (RobotKyle). El globo se construye
/// en runtime (no requiere canvas en la escena); mira siempre a la cámara.
/// </summary>
public class TutorialNPC : MonoBehaviour
{
    [Header("Historia (EDÍTALA AQUÍ — contexto/propósito de cada rol)")]
    [TextArea(5, 16)]
    public string historiaContexto =
        "«EDITA ESTE TEXTO en el Inspector (TutorialNPC → Historia Contexto)».\n\n" +
        "Ejemplo: Bienvenidos a la estación TITA. Una sobrecarga dañó los circuitos del " +
        "laboratorio y las puertas no abren. TÚ (Técnico) tienes los manuales y el IDE, " +
        "pero no puedes entrar; tu compañero (Explorador) está adentro con las manos y el " +
        "multímetro, pero sin documentación. Solo comunicándose por voz podrán repararlo todo.";

    [Header("Guía por reto (editable) — se muestra al cargar cada reto")]
    [TextArea(4, 12)]
    public string[] guiaPorReto =
    {
        "RETO 1 — LEY DE OHM\n" +
        "TÉCNICO: pide al Explorador que mida el circuito con el multímetro y te dicte los valores. " +
        "Con V = I × R y tu manual, calcula la resistencia correcta y envíasela por la bandeja.\n" +
        "EXPLORADOR: mide, dicta los valores por voz e instala la pieza que llegue a la caja.",

        "RETO 2 — PARALELO Y POLARIDAD\n" +
        "TÉCNICO: envía el LED de reemplazo y guía su orientación: pata larga (ánodo) hacia el " +
        "positivo, en la rama protegida por la resistencia de 470 Ω.\n" +
        "EXPLORADOR: coloca el LED con la polaridad correcta — si enciende verde y estable, ganaron.",

        "RETO 3 — CIRCUITO MIXTO\n" +
        "TÉCNICO: hay VARIAS fallas a la vez (resistor averiado, LED o capacitor invertidos). " +
        "Diagnostica con las medidas que te dicte tu compañero y resuelvan pieza por pieza.\n" +
        "EXPLORADOR: revisa polaridades y códigos de color; instala lo que llegue en su lugar.",

        "RETO 4 — ARDUINO\n" +
        "TÉCNICO: escribe el sketch en el IDE (elige un pin digital D2–D13) y pulsa Subir cuando " +
        "el Explorador esté listo. Dicta el cableado: pin → resistencia (≥100 Ω) → LED → GND.\n" +
        "EXPLORADOR: arma ese camino en la protoboard y valida con el botón físico.",
    };

    [Header("Animación por reto (opcional — se reproduce al mostrar la guía de cada reto)")]
    [Tooltip("Clip para cada reto: Elemento 0 = Reto 1 … Elemento 3 = Reto 4. Vacío = idle. " +
             "Si el clip no es loop, al terminar el NPC vuelve solo al idle.")]
    public AnimationClip[] animacionPorReto = new AnimationClip[4];
    [Tooltip("Velocidad de los clips por reto: 1 = normal, 0.5 = más lento.")]
    [Range(0.1f, 2f)]
    public float velocidadAnimacionReto = 1f;

    // ─────────────────────────────────────────────
    //  Secuencia de introducción: cada paso sincroniza UN texto del globo con UNA animación.
    //  Las animaciones se crean con UMotion (exportar .anim Humanoid) y se arrastran aquí.
    // ─────────────────────────────────────────────
    [System.Serializable]
    public class PasoIntro
    {
        [Tooltip("Nombre solo organizativo (no se muestra).")]
        public string nombre;
        [TextArea(3, 12)]
        public string texto;
        [Tooltip("Clip a reproducir durante este paso (exportado de UMotion). Vacío = idle normal.")]
        public AnimationClip animacion;
        [Tooltip("Velocidad del clip: 1 = normal, 0.5 = mitad de velocidad (más lento), 2 = doble.")]
        [Range(0.1f, 2f)]
        public float velocidadAnimacion = 1f;
        [Tooltip("Segundos tras los que avanza SOLO al siguiente paso. 0 = espera la tecla N.")]
        public float autoAvanzarSegundos = 0f;
    }

    [Header("Introducción (texto + animación por paso; se reproduce una vez al inicio)")]
    public List<PasoIntro> pasosIntro = new List<PasoIntro>
    {
        new PasoIntro { nombre = "Saludo",   texto = "¡Hola! Soy el asistente de la estación TITA. " +
            "(EDITA este texto y arrastra aquí tu animación de SALUDO hecha con UMotion.)" },
        new PasoIntro { nombre = "Historia", texto = "«EDITA AQUÍ LA HISTORIA»: qué pasó en la estación y " +
            "por qué hacen falta un Técnico afuera y un Explorador adentro." },
        new PasoIntro { nombre = "Roles",    texto = "TÉCNICO (tú): tienes los manuales, el IDE de Arduino y la " +
            "bandeja de envíos — pero no puedes entrar.\nEXPLORADOR: está adentro con sus manos y el multímetro — " +
            "pero sin documentación. Su única conexión: la VOZ." },
        new PasoIntro { nombre = "A trabajar", texto = "¡Listo! Ve a tu oficina y ayuda al Explorador — " +
            "ya está entrando al laboratorio. Yo te iré recordando la guía de cada reto." },
    };

    [Header("Globo de texto (posición/rotación/escala editables en vivo)")]
    [Tooltip("Posición LOCAL del globo respecto al NPC, en metros (Y = altura).")]
    public Vector3 posicionGlobo = new Vector3(0f, 1.9f, 0f);
    [Tooltip("ON: el globo gira solo para mirar a la cámara (billboard). " +
             "OFF: queda FIJO con la rotación de 'Rotación Globo' — misma ubicación siempre.")]
    public bool mirarACamara = true;
    [Tooltip("Con 'Mirar A Camara' ON: giro EXTRA sobre el billboard (pon Y=0 si el texto se ve espejado). " +
             "Con OFF: rotación local fija del globo.")]
    public Vector3 rotacionGlobo = new Vector3(0f, 180f, 0f);
    [Tooltip("Escala del canvas (sube/baja para agrandar el globo).")]
    public float escalaGlobo = 0.0018f;
    [Tooltip("Mostrar el globo automáticamente al cargar cada reto.")]
    public bool mostrarAlCargarReto = true;

    [Header("Mirada al jugador")]
    public float lookRange  = 6f;
    public float lookSpeed  = 4f;

    // ─────────────────────────────────────────────
    Animator  _animator;
    Transform _headBone;
    Transform _globo;
    TMP_Text  _titulo, _cuerpo, _pie;
    readonly List<string> _paginas = new();
    int  _pagina;
    bool _historiaMostrada;

    // Intro secuenciada
    PlayableGraph _graph;      // reproduce el clip del paso directo sobre el esqueleto
    int   _pasoIntro = -1;     // -1 = intro no activa
    int   _retoActual;
    float _tPaso;

    bool EnIntro => _pasoIntro >= 0;

    static readonly int SpeedHash = Animator.StringToHash("Speed");

    void Start()
    {
        _animator = GetComponent<Animator>();
        if (_animator != null && _animator.isHuman)
            _headBone = _animator.GetBoneTransform(HumanBodyBones.Head);

        ConstruirGlobo();

        if (pasosIntro != null && pasosIntro.Count > 0)
        {
            _historiaMostrada = true;   // la historia vive en la intro; no repetirla como página
            _pasoIntro = 0;
            AplicarPasoIntro();
        }
        else
        {
            ArmarPaginas(0);
            MostrarPagina();
        }
    }

    void OnEnable()  { GameManager.OnLevelLoaded += OnLevelLoaded; }
    void OnDisable() { GameManager.OnLevelLoaded -= OnLevelLoaded; }

    void OnLevelLoaded(LevelType nivel)
    {
        _retoActual = (int)nivel;
        if (EnIntro) return;   // la intro no se interrumpe; al terminar mostrará la guía del reto vigente

        ArmarPaginas(_retoActual);
        if (mostrarAlCargarReto && _globo != null) _globo.gameObject.SetActive(true);
        MostrarPagina();
        ReproducirClipReto(_retoActual);
    }

    float _finClipReto = -1f;   // Time.time en que termina el clip del reto (para volver al idle)

    /// <summary>Reproduce el clip asignado al reto (si hay) y programa la vuelta al idle al terminar.</summary>
    void ReproducirClipReto(int reto)
    {
        AnimationClip clip = (animacionPorReto != null && reto >= 0 && reto < animacionPorReto.Length)
            ? animacionPorReto[reto] : null;
        ReproducirClip(clip, velocidadAnimacionReto);
        _finClipReto = (clip != null && !clip.isLooping)
            ? Time.time + clip.length / Mathf.Max(0.1f, velocidadAnimacionReto)
            : -1f;
    }

    void Update()
    {
        var kb = Keyboard.current;

        if (EnIntro)
        {
            var paso = pasosIntro[_pasoIntro];
            bool porTecla  = kb != null && kb.nKey.wasPressedThisFrame;
            bool porTiempo = paso.autoAvanzarSegundos > 0f && Time.time - _tPaso >= paso.autoAvanzarSegundos;
            if (porTecla || porTiempo) AvanzarIntro();
            if (kb != null && kb.hKey.wasPressedThisFrame && _globo != null)
                _globo.gameObject.SetActive(!_globo.gameObject.activeSelf);
            return;
        }

        // Clip del reto terminado (y no era loop) → volver al idle para no quedar congelado.
        if (_finClipReto > 0f && Time.time >= _finClipReto)
        {
            _finClipReto = -1f;
            ReproducirClip(null);
        }

        if (kb == null) return;
        if (kb.nKey.wasPressedThisFrame && _globo != null && _globo.gameObject.activeSelf)
        {
            _pagina = (_pagina + 1) % _paginas.Count;
            MostrarPagina();
        }
        if (kb.hKey.wasPressedThisFrame && _globo != null)
            _globo.gameObject.SetActive(!_globo.gameObject.activeSelf);
    }

    // ─────────────────────────────────────────────
    //  Intro secuenciada (texto + animación por paso)
    // ─────────────────────────────────────────────
    void AplicarPasoIntro()
    {
        var paso = pasosIntro[_pasoIntro];
        _tPaso = Time.time;
        Debug.Log($"[TutorialNPC] Paso {_pasoIntro + 1}/{pasosIntro.Count} '{paso.nombre}' " +
                  $"(clip={(paso.animacion != null ? paso.animacion.name : "ninguno")}, " +
                  $"autoAvance={paso.autoAvanzarSegundos}s).");
        if (_cuerpo != null) _cuerpo.text = paso.texto;
        if (_pie != null)
            _pie.text = paso.autoAvanzarSegundos > 0f
                ? $"({_pasoIntro + 1}/{pasosIntro.Count}) continúa solo…   ·   [N] Saltar"
                : $"[N] Continuar ({_pasoIntro + 1}/{pasosIntro.Count})   ·   [H] Ocultar";
        ReproducirClip(paso.animacion, paso.velocidadAnimacion);
    }

    void AvanzarIntro()
    {
        _pasoIntro++;
        if (_pasoIntro >= pasosIntro.Count)
        {
            _pasoIntro = -1;
            ArmarPaginas(_retoActual);      // guía del reto vigente…
            MostrarPagina();
            ReproducirClipReto(_retoActual); // …con su animación (o idle si no tiene)
            return;
        }
        AplicarPasoIntro();
    }

    /// <summary>
    /// Reproduce un clip DIRECTO sobre el esqueleto vía Playables (ignora el AnimatorController,
    /// así no importa qué estado/blend-tree tenga). null = soltar el grafo y volver al idle
    /// normal del controller. El clip arranca en el frame 0 junto con el texto del paso.
    /// </summary>
    void ReproducirClip(AnimationClip clip, float velocidad = 1f)
    {
        if (_animator == null) return;

        if (_graph.IsValid()) _graph.Destroy();

        if (clip == null)
        {
            // Volver al controller (idle): rebind + evaluar un frame para que retome ya.
            _animator.Rebind();
            _animator.Update(0f);
            Debug.Log("[TutorialNPC] Fin de clip de paso → idle del controller.");
            return;
        }

        _graph = PlayableGraph.Create("TutorialNPC_Clip");
        var output   = AnimationPlayableOutput.Create(_graph, "npc", _animator);
        var playable = AnimationClipPlayable.Create(_graph, clip);
        playable.SetSpeed(Mathf.Max(0.1f, velocidad));
        output.SetSourcePlayable(playable);
        _graph.Play();
        Debug.Log($"[TutorialNPC] Reproduciendo clip '{clip.name}' " +
                  $"({clip.length:0.0}s, loop={clip.isLooping}, velocidad={velocidad:0.0}x).");
    }

    void OnDestroy()
    {
        if (_graph.IsValid()) _graph.Destroy();
    }

    Camera _camCache;
    float  _tBuscarCam;

    /// <summary>Camera.main, o cualquier cámara activa (la del Técnico no siempre lleva el tag MainCamera).</summary>
    Camera CamaraJugador()
    {
        var cam = Camera.main;
        if (cam != null) return cam;
        if (_camCache != null && _camCache.isActiveAndEnabled) return _camCache;
        if (Time.unscaledTime - _tBuscarCam < 1f) return null;   // no buscar cada frame
        _tBuscarCam = Time.unscaledTime;
        _camCache = FindAnyObjectByType<Camera>();
        return _camCache;
    }

    void LateUpdate()
    {
        if (_animator != null) _animator.SetFloat(SpeedHash, 0f);

        var cam = CamaraJugador();

        // Posición/rotación/escala del globo: SIEMPRE se aplican (editables en vivo desde el
        // Inspector). Solo el modo billboard necesita cámara; el modo fijo funciona sin ella.
        if (_globo != null && _globo.gameObject.activeSelf)
        {
            _globo.localPosition = posicionGlobo;
            _globo.localScale    = Vector3.one * escalaGlobo;
            // Billboard legible: el +Z del canvas debe apuntar LEJOS de la cámara
            // (así el texto se ve de frente, no espejado).
            _globo.rotation = (mirarACamara && cam != null)
                ? Quaternion.LookRotation(_globo.position - cam.transform.position) * Quaternion.Euler(rotacionGlobo)
                : transform.rotation * Quaternion.Euler(rotacionGlobo);
        }

        if (cam == null) return;

        // El robot gira la cabeza hacia el jugador cuando está cerca.
        if (_headBone == null) return;
        float dist = Vector3.Distance(transform.position, cam.transform.position);
        if (dist > lookRange) return;
        float t = Mathf.Clamp01(1f - dist / lookRange);
        Vector3 dir = (cam.transform.position - _headBone.position).normalized;
        if (dir.sqrMagnitude < 0.001f) return;
        _headBone.rotation = Quaternion.Slerp(
            _headBone.rotation, Quaternion.LookRotation(dir), Time.deltaTime * lookSpeed * t);
    }

    // ─────────────────────────────────────────────
    //  Páginas
    // ─────────────────────────────────────────────
    void ArmarPaginas(int reto)
    {
        _paginas.Clear();
        if (!_historiaMostrada && !string.IsNullOrWhiteSpace(historiaContexto))
        {
            _paginas.Add(historiaContexto);
            _historiaMostrada = true;
        }
        if (guiaPorReto != null && reto >= 0 && reto < guiaPorReto.Length &&
            !string.IsNullOrWhiteSpace(guiaPorReto[reto]))
            _paginas.Add(guiaPorReto[reto]);

        if (_paginas.Count == 0) _paginas.Add("(Sin texto: edítalo en TutorialNPC del Inspector.)");
        _pagina = 0;
    }

    void MostrarPagina()
    {
        if (_cuerpo == null) return;
        _cuerpo.text = _paginas[Mathf.Clamp(_pagina, 0, _paginas.Count - 1)];
        _pie.text    = _paginas.Count > 1
            ? $"[N] Siguiente ({_pagina + 1}/{_paginas.Count})   ·   [H] Ocultar"
            : "[H] Ocultar";
    }

    // ─────────────────────────────────────────────
    //  Construcción del globo (runtime, sin assets)
    // ─────────────────────────────────────────────
    void ConstruirGlobo()
    {
        var go = new GameObject("GloboTutorial");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = posicionGlobo;
        go.transform.localRotation = Quaternion.Euler(rotacionGlobo);
        go.transform.localScale    = Vector3.one * escalaGlobo;
        _globo = go.transform;

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(760f, 460f);

        // Fondo
        var fondo = new GameObject("Fondo", typeof(UnityEngine.UI.Image));
        fondo.transform.SetParent(go.transform, false);
        var frt = (RectTransform)fondo.transform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        fondo.GetComponent<UnityEngine.UI.Image>().color = new Color(0.07f, 0.09f, 0.15f, 0.93f);

        _titulo = CrearTexto(go.transform, "Titulo", new Vector2(0f, 190f), new Vector2(720f, 60f), 30f,
                             new Color(0.55f, 0.85f, 1f));
        _titulo.text = "ROBO-GUÍA · TUTORIAL";
        _titulo.fontStyle = FontStyles.Bold;

        _cuerpo = CrearTexto(go.transform, "Cuerpo", new Vector2(0f, -15f), new Vector2(700f, 330f), 24f,
                             Color.white);
        _cuerpo.alignment = TextAlignmentOptions.TopLeft;

        _pie = CrearTexto(go.transform, "Pie", new Vector2(0f, -205f), new Vector2(720f, 40f), 20f,
                          new Color(1f, 0.85f, 0.4f));
    }

    TMP_Text CrearTexto(Transform padre, string nombre, Vector2 pos, Vector2 tam, float fontSize, Color color)
    {
        var go = new GameObject(nombre, typeof(TextMeshProUGUI));
        go.transform.SetParent(padre, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = pos;
        rt.sizeDelta        = tam;
        var t = go.GetComponent<TextMeshProUGUI>();
        t.fontSize  = fontSize;
        t.color     = color;
        t.alignment = TextAlignmentOptions.Center;
        return t;
    }
}

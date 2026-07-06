using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Manual técnico físico sobre la mesa del Técnico.
/// Muestra las páginas del manual en un Canvas World Space sobre el objeto "libro".
///
/// SETUP en Unity:
///   1. Crear un Cube "Manual_Book" (Scale X=0.4, Y=0.01, Z=0.3) sobre la mesa
///   2. Crear hijo: Canvas (World Space, Scale=0.001, Width=400, Height=300)
///   3. Agregar este script al Canvas hijo
///   4. Agregar Botones Anterior/Siguiente al Canvas
///   5. Agregar BoxCollider al Manual_Book para detección de click/hover
/// </summary>
public class TechnicianManualDisplay : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────

    [Header("Referencias")]
    public GameManager      gameManager;
    public TechnicianManual manual;           // fallback legacy

    [Header("Contenido (Data-Driven — ScriptableObjects)")]
    [Tooltip("Una ManualPage por reto, en el mismo orden que LevelType (0=OhmLaw, 1=Parallel...).")]
    public ManualPage[]     manualPages;      // si se asigna, ignora TechnicianManual

    [Header("Textos del manual (TMPs en el Canvas del libro)")]
    public TMP_Text txtTitulo;
    public TMP_Text txtPaginaIzquierda;   // concepto + fórmulas
    public TMP_Text txtPaginaDerecha;     // tabla de valores + objetivo

    [Header("Navegación de páginas")]
    public Button btnPaginaAnterior;
    public Button btnPaginaSiguiente;
    public TMP_Text txtNumeroPagina;      // "Página 1 de 3"

    [Header("Imagen del diagrama (opcional)")]
    public Image   imgDiagrama;
    public Sprite[] diagramas;            // sprites por reto

    // ─────────────────────────────────────────────
    //  Páginas del manual
    // ─────────────────────────────────────────────

    /// <summary>Cada página tiene contenido izquierdo y derecho.</summary>
    private struct Pagina
    {
        public string izquierda;
        public string derecha;
    }

    private Pagina[] _paginas;
    private int      _paginaActual = 0;

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────

    void Awake()
    {
        AutoFindTMPFields();
    }

    void Start()
    {
        // Auto-buscar referencias no asignadas en el Inspector
        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
        if (manual      == null) manual      = FindAnyObjectByType<TechnicianManual>();

        if (gameManager == null)
            Debug.LogWarning("[TechnicianManualDisplay] GameManager no encontrado. " +
                             "Asígnalo en el Inspector de TechnicianManualDisplay.");
        if (manual == null)
        {
            manual = gameObject.AddComponent<TechnicianManual>();
            Debug.LogWarning("[TechnicianManualDisplay] TechnicianManual no encontrado en la escena — " +
                             "se creó como fallback en este GameObject.");
        }

        // Segundo intento de auto-búsqueda (Awake corre antes que otros scripts)
        AutoFindTMPFields();

        if (txtPaginaIzquierda == null)
            Debug.LogWarning("[TechnicianManualDisplay] txtPaginaIzquierda es null. " +
                             "Asígnalo en el Inspector o nombra el TMP_Text con 'izquierda' en su nombre.");
        if (txtPaginaDerecha == null)
            Debug.LogWarning("[TechnicianManualDisplay] txtPaginaDerecha es null. " +
                             "Asígnalo en el Inspector o nombra el TMP_Text con 'derecha' en su nombre.");

        if (btnPaginaAnterior  != null) btnPaginaAnterior.onClick.AddListener(PaginaAnterior);
        if (btnPaginaSiguiente != null) btnPaginaSiguiente.onClick.AddListener(PaginaSiguiente);

        GameManager.OnLevelLoaded += OnLevelLoaded;
        BuildPages();
    }

    void OnEnable()
    {
        // Cada vez que el overlay se activa, refrescar contenido
        if (_paginas != null && _paginas.Length > 0)
            MostrarPagina(_paginaActual);
        // Si _paginas es null, Start() se encarga (primera activación)
    }

    void OnDestroy()
    {
        GameManager.OnLevelLoaded -= OnLevelLoaded;
    }

    // ─────────────────────────────────────────────
    //  Auto-búsqueda de TMP_Text por nombre
    // ─────────────────────────────────────────────

    void AutoFindTMPFields()
    {
        if (txtPaginaIzquierda != null && txtPaginaDerecha != null) return;

        foreach (var t in GetComponentsInChildren<TMP_Text>(true))
        {
            string n = t.name.ToLowerInvariant();
            if      (txtTitulo           == null && (n.Contains("titulo")    || n.Contains("title")))
                txtTitulo           = t;
            else if (txtPaginaIzquierda  == null && (n.Contains("izquierda") || n.Contains("left")  || n.Contains("izq")))
                txtPaginaIzquierda  = t;
            else if (txtPaginaDerecha    == null && (n.Contains("derecha")   || n.Contains("right") || n.Contains("der")))
                txtPaginaDerecha    = t;
            else if (txtNumeroPagina     == null && (n.Contains("pagina")    || n.Contains("page")  || n.Contains("numero") || n.Contains("pag")))
                txtNumeroPagina     = t;
        }
    }

    /// <summary>Forzar reconstrucción de páginas (útil al activar desde ManualScroll).</summary>
    public void RefreshContent()
    {
        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
        AutoFindTMPFields();
        BuildPages();
    }

    // ─────────────────────────────────────────────
    //  Construcción de páginas
    // ─────────────────────────────────────────────

    void OnLevelLoaded(LevelType level)
    {
        _paginaActual = 0;
        BuildPages();
    }

    /// <summary>
    /// Construye las páginas del manual para el reto activo. El número de páginas DEPENDE del
    /// reto: los retos simples llevan las 3 páginas base; los complejos añaden páginas extra
    /// (diagnóstico, y en el Reto 4 además referencia de código Arduino y guía de cableado).
    /// </summary>
    void BuildPages()
    {
        if (gameManager == null) return;

        var lista = new System.Collections.Generic.List<Pagina>();

        // Prioridad: ScriptableObject → TechnicianManual legacy
        ManualPage page = GetManualPage(gameManager.currentLevel);

        if (page != null)
        {
            lista.Add(new Pagina
            {
                izquierda = page.titulo + "\n\n" + page.concepto,
                derecha   = "FORMULAS:\n\n" + page.formula
            });
            lista.Add(new Pagina
            {
                izquierda = "OBJETIVO:\n\n" + page.objetivo,
                derecha   = "TABLA DE REFERENCIA:\n\n" + page.tablaValores
            });
            lista.Add(new Pagina
            {
                izquierda = page.componentesClave,
                derecha   = page.codigoColores
            });
        }
        else if (manual != null)
        {
            // Fallback al sistema legacy (TechnicianManual MonoBehaviour)
            var data = manual.GetManualData(gameManager.currentLevel);
            lista.Add(new Pagina
            {
                izquierda = data.titulo + "\n\n" + data.concepto,
                derecha   = "FORMULAS:\n\n" + data.formula
            });
            lista.Add(new Pagina
            {
                izquierda = "OBJETIVO:\n\n" + data.objetivo,
                derecha   = "TABLA DE REFERENCIA:\n\n" + data.tablaValores
            });
            lista.Add(new Pagina
            {
                // Página 3: sketch de referencia si existe, sino valores/colores genéricos
                izquierda = !string.IsNullOrEmpty(data.programaReferencia)
                                ? data.programaReferencia
                                : BuildComponentValues(),
                derecha   = BuildColorCodes()
            });
        }
        else return;

        // Páginas EXTRA según el reto (diagnóstico, código Arduino, cableado…).
        lista.AddRange(PaginasExtraPorReto(gameManager.currentLevel));

        _paginas = lista.ToArray();
        if (_paginaActual >= _paginas.Length) _paginaActual = 0;
        MostrarPagina(_paginaActual);
    }

    /// <summary>
    /// Páginas adicionales por reto — aquí vive la diferencia de grosor del manual:
    /// Reto 1 = 0 extra (3 en total) · Retos 2-3 = 1 extra de diagnóstico (4) ·
    /// Reto 4 = 3 extra: código Arduino, cableado del protoboard y diagnóstico (6).
    /// Texto ASCII-safe (LiberationSans SDF no tiene flechas ni símbolos especiales).
    /// </summary>
    Pagina[] PaginasExtraPorReto(LevelType level)
    {
        switch (level)
        {
            case LevelType.Parallel:
                return new[]
                {
                    new Pagina
                    {
                        izquierda = "DIAGNOSTICO — RETO 2:\n\n" +
                                    "Sintoma: una rama del paralelo\nno enciende.\n\n" +
                                    "1. Pide al Explorador medir el\n   voltaje de CADA rama.\n" +
                                    "2. Rama con 9V pero LED apagado\n   = LED danado o invertido.\n" +
                                    "3. Rama con 0V = conexion abierta.",
                        derecha   = "GUIA AL EXPLORADOR:\n\n" +
                                    "- El LED nuevo va con el ANODO\n  (pata larga) hacia el positivo.\n" +
                                    "- Verde continuo = polaridad OK.\n" +
                                    "- La rama tiene proteccion de\n  470 Ohm: no se quema al probar.\n\n" +
                                    "Victoria: LED colocado con la\npolaridad correcta."
                    }
                };

            case LevelType.Mixed:
                return new[]
                {
                    new Pagina
                    {
                        izquierda = "DIAGNOSTICO — RETO 3:\n\n" +
                                    "Este circuito tiene VARIAS fallas\na la vez. Orden recomendado:\n\n" +
                                    "1. Polaridad del LED (pata larga\n   al positivo).\n" +
                                    "2. Polaridad del capacitor (franja\n   = negativo).\n" +
                                    "3. Valor de la resistencia serie\n   (leer codigo de colores).",
                        derecha   = "PISTAS POR MEDICION:\n\n" +
                                    "- LED apagado con voltaje OK\n  = polaridad invertida.\n" +
                                    "- I muy baja = R serie muy alta\n  (470 en vez de 220 Ohm).\n" +
                                    "- Voltaje negativo en el cap\n  = capacitor invertido.\n\n" +
                                    "Corrige TODO antes de validar."
                    }
                };

            case LevelType.Arduino:
                return new[]
                {
                    new Pagina
                    {
                        izquierda = "CODIGO ARDUINO — BASICO:\n\n" +
                                    "void setup() {\n  pinMode(13, OUTPUT);\n}\n\n" +
                                    "void loop() {\n  digitalWrite(13, HIGH);\n  delay(500);\n" +
                                    "  digitalWrite(13, LOW);\n  delay(500);\n}\n\n" +
                                    "El objetivo es PARPADEAR:\nHIGH fijo no completa el reto.",
                        derecha   = "FUNCIONES DISPONIBLES:\n\n" +
                                    "pinMode(pin, OUTPUT)\ndigitalWrite(pin, HIGH/LOW)\n" +
                                    "analogWrite(pin, 0-255)  (brillo)\nanalogRead(A0)\ndelay(ms)\nmillis()\n\n" +
                                    "Soporta variables, if, for, while\ny funciones propias. El codigo es\n" +
                                    "LIBRE: cada partida puede usar un\nsketch distinto."
                    },
                    new Pagina
                    {
                        izquierda = "CABLEADO DEL PROTOBOARD:\n\n" +
                                    "Camino obligatorio:\n\n" +
                                    "Pin digital (D2-D13)\n   |\n   cable\n   |\nResistencia (>= 100 Ohm)\n   |\n" +
                                    "LED (anodo al lado del pin)\n   |\nGND del Arduino",
                        derecha   = "REGLAS DE ORO:\n\n" +
                                    "- El pin del CODIGO debe ser el\n  MISMO pin cableado.\n" +
                                    "- Sin resistencia el LED explota\n  (I >= 1 A) y hay que pedir otro.\n" +
                                    "- 330 Ohm es el valor recomendado\n  (I aprox. 9 mA, segura).\n" +
                                    "- El circuito debe CERRAR en GND;\n  un extremo suelto = abierto."
                    },
                    new Pagina
                    {
                        izquierda = "DIAGNOSTICO — RETO 4:\n\n" +
                                    "El boton Comprobar reporta la\nfalla en esta consola:\n\n" +
                                    "- 'Pin sin OUTPUT' = falta\n  pinMode en setup().\n" +
                                    "- 'Sin BLINK' = el loop no alterna\n  HIGH/LOW con delay.\n" +
                                    "- 'No llega a GND' = circuito\n  abierto: revisar cables.",
                        derecha   = "MAS FALLAS TIPICAS:\n\n" +
                                    "- 'LED con polaridad invertida'\n  = girarlo 180 grados.\n" +
                                    "- 'Falta resistencia >= 100 Ohm'\n  = enviar/colocar una R.\n" +
                                    "- 'Corriente supera el limite'\n  = subir el valor de la R.\n\n" +
                                    "La telemetria (V/I/P/ADC) del\nmonitor confirma cada arreglo."
                    }
                };

            default:
                return System.Array.Empty<Pagina>();   // Reto 1: solo las 3 paginas base
        }
    }

    ManualPage GetManualPage(LevelType level)
    {
        if (manualPages == null || manualPages.Length == 0) return null;

        // Buscar por LevelType primero
        foreach (var p in manualPages)
            if (p != null && p.levelType == level) return p;

        // Fallback: indice directo
        int idx = (int)level;
        return idx < manualPages.Length ? manualPages[idx] : null;
    }

    /// <summary>Muestra la página actual en los TMPs del libro.</summary>
    void MostrarPagina(int index)
    {
        if (_paginas == null || index < 0 || index >= _paginas.Length) return;

        var p = _paginas[index];
        Set(txtPaginaIzquierda, p.izquierda);
        Set(txtPaginaDerecha,   p.derecha);
        Set(txtNumeroPagina,    $"Pag {index + 1} / {_paginas.Length}");

        // Diagrama en página 2
        if (imgDiagrama != null && diagramas != null)
        {
            int idx = (int)(gameManager?.currentLevel ?? 0);
            if (index == 1 && idx < diagramas.Length && diagramas[idx] != null)
                imgDiagrama.sprite = diagramas[idx];
        }

        // Botones de navegación
        if (btnPaginaAnterior  != null) btnPaginaAnterior.interactable  = index > 0;
        if (btnPaginaSiguiente != null) btnPaginaSiguiente.interactable = index < _paginas.Length - 1;
    }

    // ─────────────────────────────────────────────
    //  Navegación
    // ─────────────────────────────────────────────

    public void PaginaSiguiente()
    {
        if (_paginas == null) return;
        if (_paginaActual < _paginas.Length - 1)
        {
            _paginaActual++;
            MostrarPagina(_paginaActual);
        }
    }

    public void PaginaAnterior()
    {
        if (_paginaActual > 0)
        {
            _paginaActual--;
            MostrarPagina(_paginaActual);
        }
    }

    /// <summary>Ir directamente a una página específica.</summary>
    public void IrAPagina(int index) => MostrarPagina(_paginaActual = index);

    // ─────────────────────────────────────────────
    //  Contenido adicional
    // ─────────────────────────────────────────────

    string BuildComponentValues()
    {
        if (gameManager == null) return "—";

        return gameManager.currentLevel switch
        {
            LevelType.OhmLaw   => "VALORES DEL RETO 1:\n\nFuente: 9V\nR correcta: 850 Ohm\nLED R interna: 50 Ohm\nI objetivo: 10 mA",
            LevelType.Parallel => "VALORES DEL RETO 2:\n\nFuente: 9V\nR normal por rama: 50 Ohm\nRama rota: 9999 Ohm\nI por rama: 180 mA",
            LevelType.Mixed    => "VALORES DEL RETO 3:\n\nR serie incorrecta: 470 Ohm\nR correcta: 220 Ohm\nLED: polaridad invertida\nCap: polaridad invertida",
            LevelType.Arduino  => "SANDBOX RETO 4:\n\nFuente: 5V (TTL)\nPines libres: D2-D13\nR minima: 100 Ohm\nR recomendada: 330 Ohm\nI max LED: 20 mA",
            _ => "—"
        };
    }

    string BuildColorCodes() =>
        "CODIGO DE COLORES:\n\n" +
        "Negro=0   Marron=1  Rojo=2\n" +
        "Naranja=3 Amarillo=4 Verde=5\n" +
        "Azul=6    Violeta=7  Gris=8\n" +
        "Blanco=9\n\n" +
        "Tolerancia: Oro=5% Plata=10%\n\n" +
        "850 Ohm = Gris-Verde-Marron-Oro\n" +
        "220 Ohm = Rojo-Rojo-Marron-Oro\n" +
        "330 Ohm = Naranja-Naranja-Marron-Oro\n" +
        "470 Ohm = Amarillo-Violeta-Marron-Oro";

    void Set(TMP_Text t, string s) { if (t != null) t.text = s; }
}
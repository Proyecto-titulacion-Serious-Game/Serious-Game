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

    [Header("Imagen del diagrama (opcional) — se muestra/oculta por página, ver Pagina.imagen")]
    public Image    imgDiagrama;
    public RectTransform imgDiagramaRect;      // para reposicionarla por página (Pagina.imagenOffsetY/imagenTamano)
    public TMP_Text txtImagenTitulo;           // etiqueta arriba de la imagen — se recoloca sola pegada al borde superior de la imagen
    public RectTransform txtImagenTituloRect;
    public Vector2 imgDiagramaPosBase     = new Vector2(330, 0);
    public Vector2 imgDiagramaTamanoBase  = new Vector2(520, 600);   // tamaño si la página no especifica imagenTamano
    public float   tituloSeparacion       = 26f;                    // espacio entre el borde superior de la imagen y su título
    [Header("Reto 1 — imágenes específicas")]
    public Sprite codigoColoresReto1;     // tabla de bandas de color de la resistencia
    [Header("Reto 2 — imágenes específicas")]
    public Sprite diagramaRamasReto2;     // esquema Rama 1 / Rama 2 (VCC-R-LED-GND)
    public Sprite fotoProtoboardReto2;    // captura real de la protoboard armada
    [Header("Reto 3 — imágenes específicas")]
    public Sprite codigoColoresReto3;     // tabla de bandas de color de la resistencia
    public Sprite capacitorPolaridadReto3; // simbolo + foto del capacitor (franja = negativo)
    [Header("Reto 4 — imágenes específicas")]
    public Sprite arduinoPinoutReto4;     // mapa de pines del Arduino UNO (D0-D13, A0-A5)
    public Sprite fotoProtoboardReto4;    // captura real de la protoboard armada
    public Sprite resistenciaMultimetroReto4; // multimetro en modo OHMS midiendo un resistor
    [Header("INFO — página fija de referencia (todos los retos)")]
    [Tooltip("Assets/Imagenes/reto1/Multimetro.jpg — foto/diagrama del multímetro con sus 2 puntas.")]
    public Sprite imagenMultimetro;

    // ─────────────────────────────────────────────
    //  Páginas del manual
    // ─────────────────────────────────────────────

    /// <summary>Cada página tiene contenido izquierdo y derecho, y opcionalmente una imagen (se
    /// muestra a la derecha, en imgDiagrama, solo mientras esta página esté visible) con su propio
    /// título arriba (txtImagenTitulo). imagenOffsetY mueve la imagen verticalmente (el título se
    /// recoloca solo, pegado a su borde superior). imagenTamano fija el tamaño de la caja de la
    /// imagen (con PreserveAspect) — cada foto tiene su propia proporción (ancha, cuadrada, etc.),
    /// así se aprovecha el espacio de la página en vez de dejarla diminuta dentro de una caja fija.
    /// Si queda en (0,0) se usa imgDiagramaTamanoBase.</summary>
    private struct Pagina
    {
        public string izquierda;
        public string derecha;
        public Sprite imagen;
        public string imagenTitulo;
        public float  imagenOffsetY;
        public Vector2 imagenTamano;
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
                // Si el ScriptableObject no trae tabla de colores, usar la estándar (con la
                // regla de lectura) — el código de colores es habilidad núcleo del curso.
                derecha   = !string.IsNullOrEmpty(page.codigoColores) ? page.codigoColores : BuildColorCodes()
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
    /// Páginas adicionales por reto — aquí vive la diferencia de grosor del manual (más la
    /// página fija INFO del multímetro que BuildPages() agrega siempre al final, ver
    /// <see cref="PaginaInfoMultimetro"/>): Reto 1 = 2 extra (6 en total con INFO: código de
    /// colores + diagnóstico) · Reto 2 = 4 extra (8) · Reto 3 = 3 extra (7) · Reto 4 = 10 extra:
    /// código Arduino, cableado del protoboard, diagnóstico graduado (Comprobar), panel en vivo
    /// (Reto4DiagnosticoReporter) y requisito de modo OHMS (14). Cada página "DIAGNOSTICO — RETO
    /// N" debe citar el vocabulario REAL que produce DiagnosticSystem — si cambia el texto ahí,
    /// actualizar la página correspondiente para que no describa algo que el panel ya no dice.
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
                        izquierda = "RAMA 1 y RAMA 2:\n\n" +
                                    "El circuito tiene DOS ramas EN\nPARALELO — comparten el mismo\n" +
                                    "riel VCC y el mismo riel GND.\n\n" +
                                    "Cada rama, por separado, va:\n" +
                                    "  VCC -> resistencia -> LED -> GND\n\n" +
                                    "Si una rama no enciende, la OTRA\nno se afecta — revisa esa rama\n" +
                                    "sola (su cable, su LED).\n\n" +
                                    "OJO: una de las 2 ramas trae el LED\nDANADO (no sirve voltearlo a mano).\n" +
                                    "Esa pieza hay que REEMPLAZARLA:\nenvíasela al Explorador desde tu\n" +
                                    "panel (ver pagina de Diagnostico).\n\n" +
                                    "Diagrama de referencia -->",
                        derecha   = "",
                        imagen    = diagramaRamasReto2,
                        imagenTitulo = "Diagrama: como se ve en la protoboard real",
                        imagenTamano = new Vector2(580, 360)
                    },
                    new Pagina
                    {
                        izquierda = "COMO CABLEAR — LA BATERIA:\n\n" +
                                    "La bateria tiene DOS terminales:\n" +
                                    "  +  (positivo)   y   -  (negativo)\n\n" +
                                    "1. Cable de bateria (+) -> riel VCC\n" +
                                    "   (riel ROJO, en el borde de la\n   protoboard)\n" +
                                    "2. Cable de bateria (-) -> riel GND\n" +
                                    "   (riel AZUL, al centro)\n\n" +
                                    "SIN estos 2 cables, TODA la\n" +
                                    "protoboard queda en 0 V, aunque\n" +
                                    "el resto este perfecto — es lo\n" +
                                    "primero que hay que revisar si\n" +
                                    "el Explorador dice 'no prende\n" +
                                    "nada, ni un LED'.",
                        derecha   = "COMO CABLEAR — CADA RAMA:\n\n" +
                                    "Los slots van COLOREADOS como\n" +
                                    "guia: ROJO=VCC · NEGRO=GND ·\n" +
                                    "AZUL=entrada R · AMARILLO=salida\n" +
                                    "R · VERDE=anodo del LED.\n\n" +
                                    "3. Cable: ROJO (VCC) -> AZUL\n" +
                                    "   (entrada de la resistencia)\n" +
                                    "4. Cable: AMARILLO (salida R) ->\n" +
                                    "   VERDE (anodo del LED). Cada\n" +
                                    "   columna solo une sus PROPIOS\n" +
                                    "   agujeros — este cable ES la\n" +
                                    "   union en serie R->LED, igual\n" +
                                    "   que en una protoboard real.\n" +
                                    "5. El catodo del LED ya queda en\n" +
                                    "   el riel GND al colocarlo (su\n" +
                                    "   propia pata, SIN cable extra).\n\n" +
                                    "Repetir 3-4 para la OTRA rama.\n" +
                                    "Total: 6 cables (2 bateria +\n" +
                                    "2 por rama x 2 ramas)."
                    },
                    new Pagina
                    {
                        izquierda = "PROTOBOARD REAL:\n\n" +
                                    "Asi se ve la protoboard armada\n" +
                                    "del Reto 2 desde arriba.\n\n" +
                                    "Rieles rojos (+) en los bordes,\n" +
                                    "rieles azules (-) al centro.\n\n" +
                                    "Usa esta imagen de referencia\npara guiar al Explorador: 'el\n" +
                                    "slot verde de la izquierda',\n'el segundo desde el borde', etc.",
                        derecha   = "",
                        imagen    = fotoProtoboardReto2,
                        imagenTitulo = "Foto real de la protoboard armada",
                        imagenTamano = new Vector2(580, 308)
                    },
                    new Pagina
                    {
                        izquierda = "DIAGNOSTICO — RETO 2:\n\n" +
                                    "El panel de diagnostico (clipboard)\n" +
                                    "te dice esto EN VIVO, en su propio\nvocabulario:\n\n" +
                                    "- 'Rama N: [OK]/[!]' = si ESA rama\n  ya prende de forma segura.\n" +
                                    "- 'Ramas correctas: X/Y' = cuantas\n  ramas ya estan listas.\n" +
                                    "- 'CABLES FISICOS' lista cada\n  jumper por nombre: [OK] conectado\n  en ambas puntas, o [!] si le\n  falta una o las dos.\n" +
                                    "- 'Total: X/Y cables cerrando el\n  circuito' = cuantos jumpers YA\n  cierran alguna rama.",
                        derecha   = "GUIA AL EXPLORADOR:\n\n" +
                                    "- Si falta un cable: usa la foto\n  de la protoboard (pagina anterior)\n" +
                                    "  para indicarle CUAL slot.\n" +
                                    "- El LED nuevo va con el ANODO\n  (pata larga) hacia el positivo.\n" +
                                    "- Verde continuo = polaridad OK.\n" +
                                    "- La rama tiene proteccion de\n  470 Ohm: no se quema al probar.\n\n" +
                                    "Victoria: 'Ramas correctas: 2/2'."
                    }
                };

            case LevelType.Mixed:
                return new[]
                {
                    new Pagina
                    {
                        izquierda = "CODIGO DE COLORES (repaso):\n\n" +
                                    "Este reto trae una resistencia\ncon el VALOR incorrecto — hay\n" +
                                    "que leer sus bandas para saber\ncual es.\n\n" +
                                    "1a y 2a banda = digitos\n3a banda = cantidad de ceros\n" +
                                    "4a banda = tolerancia\n\n" +
                                    "Compara contra la tabla de\ncolores de la pagina anterior\ny dicta el valor leido.",
                        derecha   = "",
                        imagen    = codigoColoresReto3,
                        imagenTitulo = "Como leer las bandas de color",
                        imagenTamano = new Vector2(580, 330)
                    },
                    new Pagina
                    {
                        izquierda = "POLARIDAD DEL CAPACITOR:\n\n" +
                                    "El capacitor electrolitico TIENE\npolaridad, igual que el LED.\n\n" +
                                    "La pata LARGA (o el lado SIN\nfranja) es el POSITIVO (+).\n" +
                                    "La franja pintada en el cuerpo\nmarca el NEGATIVO (-).\n\n" +
                                    "Invertido no enciende nada y\npuede danar el componente.",
                        derecha   = "",
                        imagen    = capacitorPolaridadReto3,
                        imagenTitulo = "Simbolo y capacitor real: franja = negativo",
                        imagenTamano = new Vector2(480, 480)
                    },
                    new Pagina
                    {
                        izquierda = "DIAGNOSTICO — RETO 3:\n\n" +
                                    "Este circuito tiene VARIAS fallas\na la vez. Orden recomendado\n" +
                                    "(el mismo del clipboard):\n\n" +
                                    "1. Polaridad del capacitor (franja\n   = negativo) — URGENTE: riesgo\n   de humo.\n" +
                                    "2. Polaridad del LED (pata larga\n   al positivo).\n" +
                                    "3. Valor de la resistencia serie\n   (leer codigo de colores).",
                        derecha   = "PISTAS POR MEDICION:\n\n" +
                                    "- LED apagado con voltaje OK\n  = polaridad invertida.\n" +
                                    "- I muy baja = R serie muy alta\n  (la averiada marca 2200 Ohm;\n  calcula tu la correcta).\n" +
                                    "- Voltaje negativo en el cap\n  = capacitor invertido.\n\n" +
                                    "Corrige TODO antes de validar."
                    }
                };

            case LevelType.Arduino:
                return new[]
                {
                    new Pagina
                    {
                        izquierda = "COMO USAR EL BLINK DEL ARDUINO (PC):\n\n" +
                                    "• [Ctrl + Enter]: Ejecutar / Enviar el código al Arduino.\n" +
                                    "• [Ctrl + L]: Limpiar el código.\n\n" +
                                    "PASOS DEL TÉCNICO:\n\n" +
                                    "1. Clic en el monitor del PC_Arduino.\n" +
                                    "2. Escribe el sketch (reemplaza ___ por tu pin).\n" +
                                    "3. Usa Ctrl+Enter para compilar y subir.",
                        
                        derecha   = "CÓMO ELEGIR / VER EL PIN:\n\n" +
                                    "Los pines van rotulados D2..D13 en la placa\n" +
                                    "(el Explorador los ve en VR).\n\n" +
                                    "El NÚMERO que escribas en el código es el pin\n" +
                                    "que se activa. Ej: Escribe 7 -> se enciende D7.\n\n" +
                                    "¡COMUNICACIÓN CRÍTICA!\n" +
                                    "Avisa al Explorador qué pin elegiste:\n" +
                                    "él debe conectar el LED a ESE pin exacto."
                    },

                    new Pagina
                    {
                        izquierda = "CODIGO ARDUINO — BASICO:\n\n" +
                                    "void setup() {\n  pinMode(13, OUTPUT);\n}\n\n" +
                                    "void loop() {\n  digitalWrite(13, HIGH);\n  delay(500);\n" +
                                    "  digitalWrite(13, LOW);\n  delay(500);\n}\n\n" +
                                    "Este es solo UN ejemplo posible.",
                        derecha   = "FUNCIONES DISPONIBLES:\n\n" +
                                    "pinMode(pin, OUTPUT)\ndigitalWrite(pin, HIGH/LOW)\n" +
                                    "analogWrite(pin, 0-255)  (brillo)\nanalogRead(A0)\ndelay(ms)\nmillis()\n\n" +
                                    "El codigo es LIBRE: no hay un\nunico patron correcto.\n" +
                                    "Ver pag. siguiente: estructuras\nde control y mas ejemplos."
                    },
                    new Pagina
                    {
                        izquierda = "CABLEADO DEL PROTOBOARD:\n\n" +
                                    "Camino obligatorio (por cada pin\nque tu codigo use):\n\n" +
                                    "Pin digital (D2-D13)\n   |\n   cable\n   |\nResistencia\n   |\n" +
                                    "LED (anodo al lado del pin)\n   |\nriel GND de la protoboard\n   |\n" +
                                    "cable APARTE -> GND del Arduino\n\n" +
                                    "El LED es OPCIONAL: una rama\ncon solo resistencia a GND\ntambien es valida.",
                        derecha   = "REGLAS DE ORO:\n\n" +
                                    "- El pin del CODIGO debe ser el\n  MISMO pin cableado.\n" +
                                    "- Sin resistencia el LED explota\n  y hay que pedir otro.\n" +
                                    "- 330 Ohm es el valor recomendado\n  (I aprox. 8-9 mA, segura).\n" +
                                    "- OJO: el riel GND de la protoboard\n  NO viene pre-cableado al Arduino,\n" +
                                    "  solo une sus propios agujeros entre\n  si. Hace falta UN CABLE APARTE\n" +
                                    "  desde ese riel hasta un pin GND\n  fisico del Arduino, o el circuito\n" +
                                    "  queda abierto aunque todo lo demas\n  este bien.\n" +
                                    "- Puedes usar VARIOS pines a la\n  vez, cada uno con su rama."
                    },
                    new Pagina
                    {
                        izquierda = "PROTOBOARD REAL:\n\n" +
                                    "Asi se ve la protoboard del\n" +
                                    "Reto 4 desde arriba.\n\n" +
                                    "Rieles rojos (+) en los bordes,\n" +
                                    "rieles azules (-) al centro —\n" +
                                    "igual que en el Reto 2.\n\n" +
                                    "Usa esta imagen para guiar al\nExplorador: 'el slot verde de\n" +
                                    "la izquierda', etc.",
                        derecha   = "",
                        imagen    = fotoProtoboardReto4,
                        imagenTitulo = "Foto real de la protoboard armada",
                        imagenTamano = new Vector2(580, 308)
                    },
                    new Pagina
                    {
                        izquierda = "ESTRUCTURAS DE CONTROL:\n\n" +
                                    "if / else:\nif (x > 10) {\n  digitalWrite(9, HIGH);\n} else {\n" +
                                    "  digitalWrite(9, LOW);\n}\n\n" +
                                    "for:\nfor (int i=0; i<3; i++) {\n  digitalWrite(pines[i], HIGH);\n" +
                                    "  delay(300);\n}\n\n" +
                                    "while:\nwhile (analogRead(A0) < 500) {\n  delay(50);\n}",
                        derecha   = "FUNCIONES PROPIAS:\n\n" +
                                    "Puedes crear tus propias\nfunciones para no repetir\ncodigo:\n\n" +
                                    "void parpadear(int pin, int ms) {\n  digitalWrite(pin, HIGH);\n" +
                                    "  delay(ms);\n  digitalWrite(pin, LOW);\n  delay(ms);\n}\n\n" +
                                    "void loop() {\n  parpadear(9, 300);\n  parpadear(10, 300);\n}\n\n" +
                                    "Variables: int, long, float,\nbool. Arreglos: int pines[3]\n= {9, 10, 11};"
                    },
                    new Pagina
                    {
                        izquierda = "EJEMPLO: SEMAFORO (3 LEDs):\n\n" +
                                    "int pines[3] = {9, 10, 11};\n\n" +
                                    "void setup() {\n  for (int i=0; i<3; i++)\n" +
                                    "    pinMode(pines[i], OUTPUT);\n}\n\n" +
                                    "void loop() {\n  for (int i=0; i<3; i++) {\n" +
                                    "    digitalWrite(pines[i], HIGH);\n    delay(700);\n" +
                                    "    digitalWrite(pines[i], LOW);\n  }\n}\n\n" +
                                    "Cada pin necesita su propio\nLED + resistencia hasta GND.",
                        derecha   = "PWM (BRILLO GRADUAL):\n\n" +
                                    "void loop() {\n  analogWrite(9, 128);  // 50%\n  delay(1000);\n" +
                                    "  analogWrite(9, 255); // 100%\n  delay(1000);\n}\n\n" +
                                    "SIN LED (corriente continua):\n\n" +
                                    "void setup() {\n  pinMode(9, OUTPUT);\n}\n" +
                                    "void loop() {\n  digitalWrite(9, HIGH);\n}\n\n" +
                                    "Valido si solo hay resistencia\nde pin a GND, sin sobrecarga."
                    },
                    new Pagina
                    {
                        izquierda = "ELECTRONICA DEL LED (teoria):\n\n" +
                                    "El LED es un DIODO: solo conduce\ndel anodo (+) al catodo (-).\n\n" +
                                    "Al conducir, 'consume' un voltaje\nfijo Vf aprox. 2 V. El RESTO del\n" +
                                    "voltaje lo absorbe la resistencia\nen serie.\n\n" +
                                    "Corriente segura tipica: 20 mA.",
                        derecha   = "CALCULO DE LA RESISTENCIA:\n\n" +
                                    "R = (Vpin - Vled) / I\n\n" +
                                    "Con Arduino (5 V) y Vled = 2 V:\n" +
                                    "R = (5 - 2) / 0.02 = 150 Ohm\n(minimo teorico para 20 mA)\n\n" +
                                    "Por eso se recomienda 330 Ohm.\n\n" +
                                    "El monitor muestra el desglose:\n'LED cae X V - R absorbe Y V'."
                    },
                    new Pagina
                    {
                        izquierda = "DIAGNOSTICO — RETO 4:\n\n" +
                                    "El boton Comprobar reporta la\nfalla real de la simulacion:\n\n" +
                                    "- 'Ningun pin activo' = el codigo\n  no escribe HIGH/PWM en loop().\n" +
                                    "- 'No llega a GND' = casi siempre\n  falta el cable APARTE del riel\n" +
                                    "  GND al pin GND fisico del Arduino\n  (el riel solo, sin ese cable,\n" +
                                    "  no hace nada).\n" +
                                    "- 'LED invertido' = el LED esta\n  al reves en su rama.",
                        derecha   = "MAS FALLAS TIPICAS:\n\n" +
                                    "- 'LED no enciende' = corriente\n  insuficiente en esa rama.\n" +
                                    "- 'Demasiada corriente' = sube\n  la resistencia (330 Ohm rec.).\n" +
                                    "- 'Sobrecarga o cortocircuito'\n  (rama sin LED) = sube la R.\n\n" +
                                    "Ya NO hace falta que el LED\nparpadee: HIGH fijo, PWM, o\n" +
                                    "corriente continua sin LED\ntambien son validos si la\nconexion es segura."
                    },
                    new Pagina
                    {
                        izquierda = "PANEL EN VIVO — RETO 4:\n\n" +
                                    "Ademas del mensaje al presionar\nComprobar (pagina anterior), el\n" +
                                    "panel de diagnostico se actualiza\nSOLO cada 2s con el estado real\n" +
                                    "de la protoboard, sin que nadie\ntenga que presionar nada:\n\n" +
                                    "-- SKETCH CARGADO --\nPin activo, Modo, Estado y\nVoltaje del pin que programaste.\n\n" +
                                    "-- PROTOBOARD --\nCorriente y Potencia reales que\ncirculan por la rama ahora mismo.",
                        derecha   = "COMO LEERLO:\n\n" +
                                    "- Corriente en 0 mA = el circuito\n  no esta cerrado todavia.\n" +
                                    "- '[!] SOBRECARGA' = corriente\n  arriba de 25 mA — sube la R.\n" +
                                    "- '[!] CIRCUITO ABIERTO' = falta\n  un cable o el resistor.\n" +
                                    "- 'FALLAS PENDIENTES' lista cada\n  problema por separado; si en\n  vez dice '[OK] Sin fallas',\n  pide al Explorador presionar\n  el boton fisico."
                    },
                    new Pagina
                    {
                        izquierda = "BUENA PRACTICA: MODO OHMS:\n\n" +
                                    "El reto se completa SOLO cuando\nel circuito cumple tu codigo —\n" +
                                    "pero un tecnico real no confia\nen un resistor solo por su\n" +
                                    "color: lo MIDE con el multimetro\nen modo OHMS para confirmar su\n" +
                                    "valor real.\n\n" +
                                    "COMO HACERLO:\n\n" +
                                    "1. El Explorador se acerca al panel\n   del multimetro de esta sala.\n" +
                                    "2. Cambia el modo hasta que diga\n   'RESISTANCE' tocando el boton\n" +
                                    "   fisico de modo del panel (el\n   cuerpo esta fijo, no se agarra).\n" +
                                    "3. Toca con las 2 puntas los\n   extremos del resistor.\n" +
                                    "4. La pantalla muestra los OHMS\n   reales del componente.\n\n" +
                                    "(Si el docente activa el candado\n" +
                                    "'exigir medicion OHMS' en el\n" +
                                    "GameManager, este paso vuelve a\n" +
                                    "ser obligatorio antes de validar.)",
                        derecha   = "",
                        imagen        = resistenciaMultimetroReto4,
                        imagenTitulo  = "Multimetro en modo OHMS midiendo un resistor",
                        imagenTamano  = new Vector2(440, 500)
                    }
                };

            case LevelType.OhmLaw:
                return new[]
                {
                    new Pagina
                    {
                        izquierda = "COMO LEER LA RESISTENCIA:\n\n" +
                                    "Cada banda de color es un digito\n" +
                                    "(o un multiplicador). Leelas de\n" +
                                    "izquierda a derecha, empezando\n" +
                                    "por el lado mas alejado de la\n" +
                                    "banda dorada/plateada (tolerancia).\n\n" +
                                    "Compara el resultado con la tabla\n" +
                                    "de colores de la pagina anterior.",
                        derecha   = "",
                        imagen    = codigoColoresReto1,
                        imagenTitulo = "Bandas de color de la resistencia",
                        imagenTamano = new Vector2(580, 330)
                    },
                    new Pagina
                    {
                        izquierda = "DIAGNOSTICO — RETO 1:\n\n" +
                                    "El panel de diagnostico (clipboard)\n" +
                                    "te dice esto EN VIVO, en su propio\nvocabulario:\n\n" +
                                    "- 'Resistencia colocada: X Ohm\n  [OK]/[!] INCORRECTA' = el valor\n  real que el Explorador puso.\n" +
                                    "- 'Muy BAJA' = pasa demasiada\n  corriente, sobrecarga el LED.\n" +
                                    "- 'Muy ALTA' = pasa poca\n  corriente, no enciende.",
                        derecha   = "GUIA AL EXPLORADOR:\n\n" +
                                    "- 'LED: ENCENDIDO/APAGADO' +\n  voltaje y corriente medidos\n  — te dice si ya casi llega o\n  esta lejos de una zona segura.\n" +
                                    "- Si dice 'Muy BAJA': pide una\n  resistencia de MAYOR valor.\n" +
                                    "- Si dice 'Muy ALTA': pide una\n  de MENOR valor.\n\n" +
                                    // No citar aqui el valor correcto (regla del proyecto: el
                                    // manual diagnostica, no da la respuesta). El texto anterior
                                    // decia '850 Ohm entrega ~7-8 mA', que ademas ya no coincidia
                                    // con lo que imprime DiagnosticSystem.
                                    "Victoria: el panel dice '[OK] Circuito\ncorrecto' cuando la corriente del LED\n" +
                                    "cae en su franja nominal (~8 mA).\n\n" +
                                    "OJO: el LED resta su propio Vf antes\nde repartir el voltaje — no es I=V/R\npuro sobre los 9 V."
                    }
                };

            default:
                return System.Array.Empty<Pagina>();
        }
    }

    /// <summary>
    /// Página de referencia fija sobre el multímetro (misma en los 4 retos) — la abre el botón
    /// "INFO" del panel de glosario vía <see cref="IrAPaginaInfoMultimetro"/>. No revela valores
    /// calculados de ningún reto (solo explica la herramienta), respetando la regla de que el
    /// manual diagnostica pero no da la respuesta.
    /// </summary>
    Pagina PaginaInfoMultimetro() => new Pagina
    {
        izquierda = "EL MULTIMETRO:\n\n" +
                    "Panel fijo en la pared de cada\nsala (uno por reto), con 2\n" +
                    "puntas colgando por cable: ROJA\n(mano derecha) y NEGRA (mano\nizquierda, referencia).\n\n" +
                    "Tiene 3 MODOS — se cambian con\nel boton fisico del panel:\n\n" +
                    "1. VOLTAJE (DC): diferencia de\n   potencial entre las 2 puntas.\n" +
                    "2. CORRIENTE (DC): corriente que\n   atraviesa el componente\n   entre las puntas.\n" +
                    "3. RESISTENCIA (OHMS): valor del\n   componente que tocan las\n   puntas.",
        derecha   = "COMO SE USA:\n\n" +
                    "1. El Explorador agarra el MANGO\n   de cada punta (no hace falta\n   agarrar el panel: esta fijo).\n" +
                    "2. Presiona el boton fisico del\n   panel para elegir el modo\n   (Voltaje / Corriente / Resistencia).\n" +
                    "3. Acerca la punta ROJA (mano\n   derecha) a un nodo o slot —\n   no hace falta tocarlo exacto.\n" +
                    "4. Acerca la punta NEGRA\n   (mano izquierda) a otro nodo.\n" +
                    "5. La pantalla del panel muestra\n   la lectura en vivo.\n\n" +
                    "CUANDO USARLO EN EL JUEGO:\n\n" +
                    "- Reto 1: confirmar el voltaje\n  sobre el LED/resistor.\n" +
                    "- Retos 2 y 3: diagnosticar por\n  que una rama no enciende.\n" +
                    "- Reto 4: medir la RESISTENCIA\n  (modo OHMS) es la buena\n  practica del tecnico real.\n  Cambio de modo: boton fisico\n  del panel, no el cuerpo.",
        imagen        = imagenMultimetro,
        imagenTitulo  = "El multimetro: puntas roja y negra",
        imagenTamano  = new Vector2(480, 480)
    };

    /// <summary>Salta a la página fija de referencia del multímetro — la abre el botón "INFO"
    /// del panel de glosario (<see cref="ManualGlossaryToggle"/>). Es siempre la última página
    /// del reto activo (ver <see cref="BuildPages"/>), así que no depende de cuántas páginas
    /// extra tenga cada reto.</summary>
    public void IrAPaginaInfoMultimetro()
    {
        if (_paginas == null || _paginas.Length == 0) return;
        SetImagenVisible(true);
        IrAPagina(_paginas.Length - 1);
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

        // Imagen + título: SOLO visibles en la página que los trae asignados — antes la imagen
        // quedaba pegada en pantalla al cambiar de página porque nada la ocultaba. La posición
        // (imagenOffsetY) se aplica junto con la visibilidad, para que páginas con más o menos
        // texto puedan correr la imagen/título sin pisar el texto.
        AplicarImagenDePagina(p);

        // Botones de navegación
        if (btnPaginaAnterior  != null) btnPaginaAnterior.interactable  = index > 0;
        if (btnPaginaSiguiente != null) btnPaginaSiguiente.interactable = index < _paginas.Length - 1;
    }

    void AplicarImagenDePagina(Pagina p)
    {
        Vector2 posImagen = imgDiagramaPosBase + new Vector2(0, p.imagenOffsetY);

        if (imgDiagrama != null)
        {
            if (p.imagen != null)
            {
                Vector2 tamano = p.imagenTamano != Vector2.zero ? p.imagenTamano : imgDiagramaTamanoBase;
                imgDiagrama.sprite = p.imagen;
                imgDiagrama.gameObject.SetActive(true);
                if (imgDiagramaRect != null)
                {
                    imgDiagramaRect.sizeDelta = tamano;
                    imgDiagramaRect.anchoredPosition = posImagen;
                }
            }
            else
            {
                imgDiagrama.gameObject.SetActive(false);
            }
        }

        if (txtImagenTitulo != null)
        {
            bool hayTitulo = p.imagen != null && !string.IsNullOrEmpty(p.imagenTitulo);
            txtImagenTitulo.gameObject.SetActive(hayTitulo);
            if (hayTitulo)
            {
                txtImagenTitulo.text = p.imagenTitulo;
                if (txtImagenTituloRect != null)
                {
                    // Pegado al borde superior REAL de la imagen (según su tamaño de esta
                    // página), no a una posición fija — así nunca queda flotando lejos de
                    // una imagen chica ni encimado sobre una imagen grande.
                    Vector2 tamano = p.imagenTamano != Vector2.zero ? p.imagenTamano : imgDiagramaTamanoBase;
                    float bordeSuperior = posImagen.y + tamano.y / 2f;
                    txtImagenTituloRect.anchoredPosition = new Vector2(posImagen.x, bordeSuperior + tituloSeparacion);
                }
            }
        }
    }

    /// <summary>Fuerza ocultar/restaurar la imagen (y su título) de la página SIN cambiar de
    /// página — lo usa <see cref="ManualGlossaryToggle"/> para taparlos mientras el glosario está
    /// abierto encima. Al restaurar (visible=true), respeta si la página actual trae imagen o no.</summary>
    public void SetImagenVisible(bool visible)
    {
        if (!visible)
        {
            if (imgDiagrama       != null) imgDiagrama.gameObject.SetActive(false);
            if (txtImagenTitulo   != null) txtImagenTitulo.gameObject.SetActive(false);
            return;
        }

        if (_paginas != null && _paginaActual >= 0 && _paginaActual < _paginas.Length)
            AplicarImagenDePagina(_paginas[_paginaActual]);
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
            // REGLA PERMANENTE del proyecto: el manual da DIAGNÓSTICO (qué está mal, qué se mide,
            // qué hace falta), nunca el VALOR CALCULADO — eso es lo que el Técnico tiene que resolver
            // con V=I×R a partir de lo que le dicte el Explorador. "R correcta: 850/470 Ohm" y las
            // filas de código de colores que apuntaban a esos mismos valores en BuildColorCodes()
            // regalaban la respuesta del Reto 1 y del Reto 3 directamente — quitadas.
            // La corriente nominal (8 mA, franja 7,5-8,5) es el dato que hace que el calculo caiga
            // dentro de la tolerancia real de aceptacion (+-12% en Resistor.IsValueCorrect). Dar
            // solo "5-20 mA" hacia que el jugador calculara un valor que el juego rechazaba.
            // Sigue SIN decir la resistencia correcta: eso lo resuelve el Tecnico.
            LevelType.OhmLaw   => "VALORES DEL RETO 1:\n\nFuente: 9V\nVf del LED: ~2V (se resta antes de aplicar V=I×R)\nLED R interna: 50 Ohm\nCorriente nominal del LED: 8 mA (7,5 a 8,5 mA)\n\nR = (V_fuente - Vf) / I_objetivo, con el voltaje que te dicte el Explorador.\nNo uses 10-20 mA: son de un LED generico, no de este.",
            LevelType.Parallel => "VALORES DEL RETO 2:\n\nFuente: 9V\nProteccion de rama: 470 Ohm\nRama rota: circuito abierto\nI segura por rama: ~13 mA",
            LevelType.Mixed    => "VALORES DEL RETO 3:\n\nR serie incorrecta: 2200 Ohm\nLED: polaridad invertida\nCap: polaridad invertida\n\nCalcula la R correcta con V=I×R usando lo que te dicte el Explorador.",
            LevelType.Arduino  => "SANDBOX RETO 4:\n\nFuente: 5V (TTL)\nPines libres: D2-D13\nR minima: 100 Ohm\nI max LED: 20 mA",
            _ => "—"
        };
    }

    string BuildColorCodes() =>
        "CODIGO DE COLORES:\n\n" +
        "COMO LEERLO (3 bandas + tolerancia):\n" +
        "1a banda = primer digito\n" +
        "2a banda = segundo digito\n" +
        "3a banda = cantidad de CEROS\n" +
        "Ej: Rojo-Rojo-Rojo = 2-2-00 = 2200 Ohm\n\n" +
        "Negro=0   Marron=1  Rojo=2\n" +
        "Naranja=3 Amarillo=4 Verde=5\n" +
        "Azul=6    Violeta=7  Gris=8\n" +
        "Blanco=9\n\n" +
        "Tolerancia: Oro=5% Plata=10%\n\n" +
        "Ejemplo (no es la respuesta de ningun reto):\n" +
        "100 Ohm = Marron-Negro-Marron-Oro\n" +
        "1000 Ohm = Marron-Negro-Rojo-Oro";

    void Set(TMP_Text t, string s) { if (t != null) t.text = s; }
}
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
                        izquierda = "RAMA 1 y RAMA 2:\n\n" +
                                    "El circuito tiene DOS ramas EN\nPARALELO — comparten el mismo\n" +
                                    "riel VCC y el mismo riel GND.\n\n" +
                                    "Cada rama, por separado, va:\n" +
                                    "  VCC -> resistencia -> LED -> GND\n\n" +
                                    "Si una rama no enciende, la OTRA\nno se afecta — revisa esa rama\n" +
                                    "sola (su cable, su LED).\n\n" +
                                    "Diagrama de referencia -->",
                        derecha   = "",
                        imagen    = diagramaRamasReto2,
                        imagenTitulo = "Diagrama: como se ve en la protoboard real",
                        imagenTamano = new Vector2(580, 360)
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
                                    "El panel de diagnostico (pantalla)\n" +
                                    "te dice esto EN VIVO, en su propio\nvocabulario:\n\n" +
                                    "- 'Cables: X/Y conectados' = cuantos\n  jumpers YA cierran una rama.\n" +
                                    "- 'Rama N: [OK]/[!]' = si ESA rama\n  ya prende de forma segura.\n" +
                                    "- 'Ramas correctas: X/2' = cuantas\n  ramas ya estan listas.",
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
                                    "Compara contra la tabla de la\npagina anterior (850/220/330/\n470 Ohm).",
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
                                    "Este es solo UN ejemplo posible.",
                        derecha   = "FUNCIONES DISPONIBLES:\n\n" +
                                    "pinMode(pin, OUTPUT)\ndigitalWrite(pin, HIGH/LOW)\n" +
                                    "analogWrite(pin, 0-255)  (brillo)\nanalogRead(A0)\ndelay(ms)\nmillis()\n\n" +
                                    "El codigo es LIBRE: no hay un\nunico patron correcto.\n\n" +
                                    "Ver pag. siguiente: estructuras\nde control y mas ejemplos."
                    },
                    new Pagina
                    {
                        izquierda = "MAPA DE PINES:\n\n" +
                                    "Cada pin digital tiene un NUMERO\n" +
                                    "(el que usas en pinMode/\n" +
                                    "digitalWrite). Los marcados con\n" +
                                    "'~' tambien sirven para PWM\n" +
                                    "(analogWrite).\n\n" +
                                    "A0-A5 son ENTRADAS analogicas\n" +
                                    "(analogRead), no de salida.\n\n" +
                                    "GND aparece en varios lugares:\ncualquiera sirve.",
                        derecha   = "",
                        imagen    = arduinoPinoutReto4,
                        imagenTitulo = "Arduino UNO — pines etiquetados",
                        imagenTamano = new Vector2(580, 387)
                    },
                    new Pagina
                    {
                        izquierda = "CABLEADO DEL PROTOBOARD:\n\n" +
                                    "Camino obligatorio (por cada pin\nque tu codigo use):\n\n" +
                                    "Pin digital (D2-D13)\n   |\n   cable\n   |\nResistencia\n   |\n" +
                                    "LED (anodo al lado del pin)\n   |\nGND del Arduino\n\n" +
                                    "El LED es OPCIONAL: una rama\ncon solo resistencia a GND\ntambien es valida.",
                        derecha   = "REGLAS DE ORO:\n\n" +
                                    "- El pin del CODIGO debe ser el\n  MISMO pin cableado.\n" +
                                    "- Sin resistencia el LED explota\n  y hay que pedir otro.\n" +
                                    "- 330 Ohm es el valor recomendado\n  (I aprox. 9 mA, segura).\n" +
                                    "- El circuito debe CERRAR en GND;\n  un extremo suelto = abierto.\n" +
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
                                    "- 'No llega a GND' = falta cerrar\n  el circuito con un cable.\n" +
                                    "- 'LED invertido' = el LED esta\n  al reves en su rama.",
                        derecha   = "MAS FALLAS TIPICAS:\n\n" +
                                    "- 'LED no enciende' = corriente\n  insuficiente en esa rama.\n" +
                                    "- 'Demasiada corriente' = sube\n  la resistencia (330 Ohm rec.).\n" +
                                    "- 'Sobrecarga o cortocircuito'\n  (rama sin LED) = sube la R.\n\n" +
                                    "Ya NO hace falta que el LED\nparpadee: HIGH fijo, PWM, o\n" +
                                    "corriente continua sin LED\ntambien son validos si la\nconexion es segura."
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
                                    "de la pagina anterior (850 Ohm).",
                        derecha   = "",
                        imagen    = codigoColoresReto1,
                        imagenTitulo = "Bandas de color de la resistencia",
                        imagenTamano = new Vector2(580, 330)
                    }
                };

            default:
                return System.Array.Empty<Pagina>();
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
            LevelType.OhmLaw   => "VALORES DEL RETO 1:\n\nFuente: 9V\nR correcta: 850 Ohm\nLED R interna: 50 Ohm\nI objetivo: 10 mA",
            LevelType.Parallel => "VALORES DEL RETO 2:\n\nFuente: 9V\nProteccion de rama: 470 Ohm\nRama rota: circuito abierto\nI segura por rama: ~13 mA",
            LevelType.Mixed    => "VALORES DEL RETO 3:\n\nR serie incorrecta: 470 Ohm\nR correcta: 220 Ohm\nLED: polaridad invertida\nCap: polaridad invertida",
            LevelType.Arduino  => "SANDBOX RETO 4:\n\nFuente: 5V (TTL)\nPines libres: D2-D13\nR minima: 100 Ohm\nR recomendada: 330 Ohm\nI max LED: 20 mA",
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
        "850 Ohm = Gris-Verde-Marron-Oro\n" +
        "220 Ohm = Rojo-Rojo-Marron-Oro\n" +
        "330 Ohm = Naranja-Naranja-Marron-Oro\n" +
        "470 Ohm = Amarillo-Violeta-Marron-Oro";

    void Set(TMP_Text t, string s) { if (t != null) t.text = s; }
}
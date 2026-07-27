using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// TÉCNICO: muestra en el Clipboard_Canvas (Technician_Workstation.prefab) el diagnóstico del
/// reto actual que el Explorador envía por red vía <see cref="GameSession.OnDiagnosticoRetoActualizado"/>.
/// Así ambos saben qué falta, sin que el Técnico vea el circuito 3D (regla asimétrica del diseño).
///
/// Vive en el MISMO Clipboard_Canvas que ya trae los encabezados fijos "DIAGNÓSTICO" /
/// "SIGUIENTE ACCIÓN" (TMP_HeaderDiag / TMP_HeaderAccion) — antes existía un "Diagnostico_Canvas"
/// aparte, superpuesto físicamente sobre este mismo clipboard, mostrando lo mismo dos veces.
/// Se eliminó: ahora este único componente escribe directo en TMP_Diagnostico/TMP_AccionSiguiente.
/// </summary>
public class TecnicoDiagnosticoUI : MonoBehaviour
{
    [Tooltip("TMP_Diagnostico — la parte 'qué está mal' del resumen.")]
    public TMP_Text texto;

    [Tooltip("TMP_AccionSiguiente — la parte 'qué hacer ahora' del resumen. Opcional: si el " +
             "resumen recibido no trae una acción separada (Reto 4), se deja el texto de espera.")]
    public TMP_Text textoAccion;

    [Tooltip("Texto cuando aún no llega ningún diagnóstico.")]
    public string textoEspera = "—";

    [Header("Paginación de 'texto' (opcional)")]
    [Tooltip("Algunos retos (p.ej. Reto 2: cada rama + cada cable físico) generan un diagnóstico " +
             "más largo de lo que el recuadro puede mostrar aun con la fuente reducida al mínimo. " +
             "Si se asignan estos botones, 'texto' pasa a modo Página de TMP: primero se achica, y " +
             "si igual no entra, el resto pasa a una 2ª/3ª página en vez de cortarse o desbordar. " +
             "'textoAccion' no pagina (su contenido es siempre 1-2 frases cortas).")]
    public Button btnPaginaAnterior;
    public Button btnPaginaSiguiente;
    [Tooltip("Muestra 'Pág X/Y'. Se oculta solo si el diagnóstico entero cabe en 1 página.")]
    public TMP_Text txtNumeroPagina;

    // RetoDiagnosticoReporter (Retos 1/3) y Reto2CircuitGuard (Reto 2) arman el resumen como
    // "{diagnóstico}\n\n> {próxima acción}" — lo partimos para llenar los 2 recuadros ya
    // diseñados en el clipboard en vez de amontonar todo en uno solo. Reto4DiagnosticoReporter
    // usa el mismo formato; GameManager (feedback graduado de Reto 4 al presionar "Comprobar")
    // manda un único mensaje sin separador — en ese caso todo va al recuadro de diagnóstico.
    const string Separador = "\n\n> ";

    void Awake()
    {
        PrepararAutoSize(texto);
        PrepararAutoSize(textoAccion);
        if (texto != null)
        {
            texto.text = textoEspera;
            // Solo activar el modo Página si hay cómo pasar de página — si no, el resto del
            // texto quedaría inalcanzable (peor que dejarlo en Overflow normal).
            if (btnPaginaAnterior != null || btnPaginaSiguiente != null)
                texto.overflowMode = TextOverflowModes.Page;
        }
        if (textoAccion != null) textoAccion.text = textoEspera;

        if (btnPaginaAnterior  != null) btnPaginaAnterior.onClick.AddListener(PaginaAnterior);
        if (btnPaginaSiguiente != null) btnPaginaSiguiente.onClick.AddListener(PaginaSiguiente);
    }

    // El recuadro del clipboard es chico y la longitud del diagnóstico varía mucho (síntoma breve
    // vs. diagnóstico explícito de nivel 3, o el detalle por rama+cable del Reto 2) — mejor que el
    // texto se achique primero a que se corte o se desborde encima de otro elemento. Nunca crece
    // más allá del tamaño de diseño original; ver también la paginación arriba como 2ª línea de
    // defensa cuando ni siquiera al tamaño mínimo entra todo.
    static void PrepararAutoSize(TMP_Text t)
    {
        if (t == null) return;
        float tamanoDiseno = t.fontSize > 0 ? t.fontSize : 7f;
        t.enableAutoSizing = true;
        t.fontSizeMax = tamanoDiseno;
        t.fontSizeMin = Mathf.Min(4f, tamanoDiseno);
    }

    GameManager _gameManager;

    void OnEnable()
    {
        GameSession.OnDiagnosticoRetoActualizado += OnDiag;
        GameManager.OnLevelLoaded                += OnLevel;
        Debug.Log("[TecnicoDiagnosticoUI] OnEnable — suscrito a GameSession.OnDiagnosticoRetoActualizado.");

        if (_gameManager == null) _gameManager = FindAnyObjectByType<GameManager>();
        // Mostrar lo último conocido DEL RETO ACTUAL (por si el panel se activó después de que
        // ya hubiera llegado un diagnóstico) — antes buscaba "el reto más alto con algo
        // cacheado" sin importar en qué reto se está realmente, lo cual podía mostrar un
        // diagnóstico viejo de OTRO reto.
        MostrarUltimoDelRetoActual();
    }

    void OnDisable()
    {
        GameSession.OnDiagnosticoRetoActualizado -= OnDiag;
        GameManager.OnLevelLoaded                -= OnLevel;
    }

    void OnDiag(int reto, string resumen)
    {
        // Solo pintar si el reporte es del reto ACTUAL: en el cambio de reto puede llegar (por
        // latencia de red) un último resumen rezagado del reto anterior y pisar el clipboard.
        // No se pierde nada: GameSession cachea por reto y OnLevel repinta el que corresponde.
        if (_gameManager == null) _gameManager = FindAnyObjectByType<GameManager>();
        if (_gameManager != null && reto != (int)_gameManager.currentLevel + 1) return;
        Mostrar(resumen);
    }

    /// <summary>
    /// Al cambiar de reto (incluye F1-F4 debug skip): el clipboard NO debe seguir mostrando el
    /// diagnóstico del reto anterior mientras llega el primero del nuevo — antes se quedaba
    /// "pegado" ahí porque nada limpiaba el texto ni escuchaba OnLevelLoaded. Los reporters
    /// (RetoDiagnosticoReporter/Reto2CircuitGuard/Reto4DiagnosticoReporter) ya envían de
    /// inmediato al entrar al reto, pero igual hay una vuelta de red — mientras tanto, esperando.
    /// </summary>
    void OnLevel(LevelType lt)
    {
        SetTexto(texto,       textoEspera);
        SetTexto(textoAccion, textoEspera);
        if (texto != null && texto.overflowMode == TextOverflowModes.Page)
        {
            texto.pageToDisplay = 1;
            texto.ForceMeshUpdate();
        }
        ActualizarPaginacion();

        MostrarUltimoDelRetoActual();
    }

    void MostrarUltimoDelRetoActual()
    {
        var gs = GameSession.Instance;
        if (gs == null || _gameManager == null) return;
        int reto = (int)_gameManager.currentLevel + 1;
        var d = gs.UltimoDiagnosticoReto(reto);
        if (!string.IsNullOrEmpty(d)) Mostrar(d);
    }

    void Mostrar(string resumen)
    {
        Debug.Log($"[TecnicoDiagnosticoUI] Mostrar() largo={resumen?.Length ?? 0} " +
                  $"inicio=\"{(resumen != null && resumen.Length > 0 ? resumen.Substring(0, Mathf.Min(40, resumen.Length)) : "")}\"");
        int idx = resumen.IndexOf(Separador, System.StringComparison.Ordinal);
        if (idx >= 0)
        {
            SetTexto(texto,       resumen.Substring(0, idx));
            SetTexto(textoAccion, resumen.Substring(idx + Separador.Length));
        }
        else
        {
            SetTexto(texto,       resumen);
            SetTexto(textoAccion, textoEspera);
        }

        if (texto != null && texto.overflowMode == TextOverflowModes.Page)
        {
            // Un diagnóstico nuevo siempre arranca en la página 1 — evita quedar mostrando la
            // página 2 de un texto viejo cuando llega uno nuevo más corto que ya no la tiene.
            texto.pageToDisplay = 1;
            // ForceMeshUpdate recalcula textInfo.pageCount YA; si no, pageCount queda con el
            // valor del texto anterior hasta el próximo render y los botones se desincronizan.
            texto.ForceMeshUpdate();
        }
        ActualizarPaginacion();
    }

    void PaginaAnterior()
    {
        if (texto == null || texto.overflowMode != TextOverflowModes.Page) return;
        if (texto.pageToDisplay > 1) texto.pageToDisplay--;
        ActualizarPaginacion();
    }

    void PaginaSiguiente()
    {
        if (texto == null || texto.overflowMode != TextOverflowModes.Page) return;
        if (texto.pageToDisplay < texto.textInfo.pageCount) texto.pageToDisplay++;
        ActualizarPaginacion();
    }

    void ActualizarPaginacion()
    {
        if (texto == null || texto.overflowMode != TextOverflowModes.Page)
        {
            if (btnPaginaAnterior  != null) btnPaginaAnterior.gameObject.SetActive(false);
            if (btnPaginaSiguiente != null) btnPaginaSiguiente.gameObject.SetActive(false);
            if (txtNumeroPagina    != null) txtNumeroPagina.gameObject.SetActive(false);
            return;
        }

        int total  = Mathf.Max(1, texto.textInfo.pageCount);
        int actual = Mathf.Clamp(texto.pageToDisplay, 1, total);

        if (btnPaginaAnterior != null)
        {
            btnPaginaAnterior.gameObject.SetActive(total > 1);
            btnPaginaAnterior.interactable = actual > 1;
        }
        if (btnPaginaSiguiente != null)
        {
            btnPaginaSiguiente.gameObject.SetActive(total > 1);
            btnPaginaSiguiente.interactable = actual < total;
        }
        if (txtNumeroPagina != null)
        {
            txtNumeroPagina.gameObject.SetActive(total > 1);
            txtNumeroPagina.text = $"Pág {actual}/{total}";
        }
    }

    void SetTexto(TMP_Text t, string s) { if (t != null) t.text = s; }
}

using UnityEngine;
using TMPro;

/// <summary>
/// TÉCNICO: muestra en su clipboard/HUD el RESUMEN de diagnóstico del reto actual, que el Explorador
/// envía por red vía <see cref="GameSession.OnDiagnosticoRetoActualizado"/>. Así ambos saben qué falta.
///
/// Respeta la asimetría (Reto 4): es un resumen 2D (LEDs, qué falta), no el circuito 3D.
/// Lo cablea el tool  Tools → TITA → Reto 2 → Diagnóstico en clipboard del Técnico.
/// </summary>
public class TecnicoDiagnosticoUI : MonoBehaviour
{
    [Tooltip("Texto donde se muestra el diagnóstico (TMP). Si es null se busca en los hijos.")]
    public TMP_Text texto;

    [Tooltip("Encabezado antes del resumen.")]
    public string encabezado = "DIAGNÓSTICO DEL CIRCUITO";

    [Tooltip("Texto cuando aún no llega ningún diagnóstico.")]
    public string textoEspera = "Esperando estado del circuito…";

    void Awake()
    {
        if (texto == null) texto = GetComponentInChildren<TMP_Text>(true);
        if (texto != null && string.IsNullOrEmpty(texto.text)) texto.text = textoEspera;
    }

    void OnEnable()
    {
        GameSession.OnDiagnosticoRetoActualizado += OnDiag;

        // Mostrar lo último conocido (por si llegó antes de abrir el clipboard).
        var gs = GameSession.Instance;
        if (gs != null)
            for (int r = 4; r >= 1; r--)
            {
                var d = gs.UltimoDiagnosticoReto(r);
                if (!string.IsNullOrEmpty(d)) { Mostrar(r, d); return; }
            }
    }

    void OnDisable() => GameSession.OnDiagnosticoRetoActualizado -= OnDiag;

    void OnDiag(int reto, string resumen) => Mostrar(reto, resumen);

    void Mostrar(int reto, string resumen)
    {
        if (texto == null) return;
        texto.text = string.IsNullOrEmpty(encabezado)
            ? $"[Reto {reto}]\n{resumen}"
            : $"{encabezado}\n<size=80%>Reto {reto}</size>\n{resumen}";
    }
}

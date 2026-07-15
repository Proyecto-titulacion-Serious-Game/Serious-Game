using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Aviso temporal en pantalla para el Técnico cuando se completa (o falla) un reto — aparece
/// unos segundos y se oculta solo, igual que el mensaje del Explorador (PlayerFeedbackUI).
/// El Técnico no tenía NINGÚN aviso de esto antes (ni éxito ni fallo).
/// </summary>
public class TechnicianLevelNotice : MonoBehaviour
{
    [Tooltip("Panel que se activa/desactiva (contiene los dos textos).")]
    public GameObject panelMensaje;
    public TMP_Text   txtTitulo;
    public TMP_Text   txtDetalle;

    [Tooltip("Segundos que el aviso queda visible antes de ocultarse solo.")]
    public float duracionSegundos = 5f;

    Coroutine _ocultarCo;

    void OnEnable()  => GameManager.OnLevelCompleted += OnLevelCompleted;
    void OnDisable() => GameManager.OnLevelCompleted -= OnLevelCompleted;

    void Start()
    {
        if (panelMensaje != null) panelMensaje.SetActive(false);
    }

    void OnLevelCompleted(LevelType level, bool success)
    {
        string titulo = success ? "¡FELICIDADES!" : $"Reto {(int)level + 1} — intenta mejor";
        string detalle = success
            ? $"El equipo completó el Reto {(int)level + 1}.\n¡Listo para el nuevo reto!"
            : "Revisen el procedimiento con el Explorador.";

        if (txtTitulo  != null) txtTitulo.text  = titulo;
        if (txtDetalle != null) txtDetalle.text = detalle;
        if (panelMensaje != null) panelMensaje.SetActive(true);

        if (_ocultarCo != null) StopCoroutine(_ocultarCo);
        _ocultarCo = StartCoroutine(OcultarTrasDelay());
    }

    IEnumerator OcultarTrasDelay()
    {
        yield return new WaitForSeconds(duracionSegundos);
        if (panelMensaje != null) panelMensaje.SetActive(false);
        _ocultarCo = null;
    }
}

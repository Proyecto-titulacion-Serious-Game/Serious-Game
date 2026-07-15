using UnityEngine;

/// <summary>
/// Alterna el panel de glosario de componentes ("que es cada componente")
/// dentro del Manual_Overlay del Técnico. El botón Info lo abre/cierra
/// (toggle); su propio botón X también lo cierra.
///
/// Mientras el glosario está abierto, tapa la imagen del diagrama de la página (si la hay) —
/// al cerrar, la restaura solo si la página actual realmente trae una.
/// </summary>
public class ManualGlossaryToggle : MonoBehaviour
{
    public GameObject panelGlosario;

    [Tooltip("Para ocultar/restaurar la imagen del diagrama mientras el glosario tapa la página.")]
    public TechnicianManualDisplay manualDisplay;

    /// <summary>Enganchado al Button_Info: abre si está cerrado, cierra si está abierto.</summary>
    public void ToggleGlosario()
    {
        if (panelGlosario == null) return;
        bool abrir = !panelGlosario.activeSelf;
        panelGlosario.SetActive(abrir);
        manualDisplay?.SetImagenVisible(!abrir);
    }

    public void AbrirGlosario()
    {
        if (panelGlosario != null) panelGlosario.SetActive(true);
        manualDisplay?.SetImagenVisible(false);
    }

    public void CerrarGlosario()
    {
        if (panelGlosario != null) panelGlosario.SetActive(false);
        manualDisplay?.SetImagenVisible(true);
    }
}

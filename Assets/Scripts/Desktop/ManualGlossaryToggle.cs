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
    
    // NUEVA VARIABLE: Arrastra aquí el panel interno del multímetro que creaste en el Paso 1
    public GameObject subPanelInfoMultimetro; 

    public TechnicianManualDisplay manualDisplay;

    public void ToggleGlosario()
    {
        if (panelGlosario == null) return;
        bool abrir = !panelGlosario.activeSelf;
        panelGlosario.SetActive(abrir);
        manualDisplay?.SetImagenVisible(!abrir);
        
        // Si cerramos el glosario principal, asegurarnos de ocultar el subpanel interno también
        if (!abrir && subPanelInfoMultimetro != null) 
            subPanelInfoMultimetro.SetActive(false);
    }

    public void AbrirGlosario()
    {
        if (panelGlosario != null) panelGlosario.SetActive(true);
        manualDisplay?.SetImagenVisible(false);
    }

    public void CerrarGlosario()
    {
        if (panelGlosario != null) panelGlosario.SetActive(false);
        if (subPanelInfoMultimetro != null) subPanelInfoMultimetro.SetActive(false);
        manualDisplay?.SetImagenVisible(true);
    }

    // MODIFICADO: Ya no cierra el glosario ni cambia la página del manual
    public void AbrirInfoMultimetro()
    {
        if (subPanelInfoMultimetro != null)
        {
            // Activa la información dentro del propio glosario
            subPanelInfoMultimetro.SetActive(true); 
        }
    }
}
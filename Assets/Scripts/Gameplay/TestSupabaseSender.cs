using UnityEngine;
using UnityEngine.InputSystem; // ← Importante para el nuevo Input System

public class TestSupabaseSender : MonoBehaviour
{
    void Update()
    {
        // Usamos el nuevo Input System para verificar si se presionó la tecla T
        var kb = Keyboard.current;
        if (kb != null && kb.tKey.wasPressedThisFrame)
        {
            EnviarDatosPrueba();
        }
    }

    public void EnviarDatosPrueba()
    {
        if (AnalyticsManager.Instance == null)
        {
            Debug.LogError("[TestSupabaseSender] No se encontró el AnalyticsManager en la escena.");
            return;
        }

        var payloadPrueba = new AnalyticsManager.TelemetriaPayload
        {

            nombre_clase = "Electrónica Básica - Test Unitario",
            grupo_estudiantes = "Grupo Prueba (Dev)",
            reto_id = 4,
            
            tiempo_resolucion_seg = 125,
            completado = true,
            nota_autograder = 9.50f,
            
            cant_cortocircuitos = 0,
            cant_sobrecorriente = 1,
            cant_polaridad_invertida = 0,
            
            fallos_compilacion_ide = 1,
            desconexiones_logica_fisica = 0,
            rechazos_componentes = 0
        };

        Debug.Log("[TestSupabaseSender] Disparando datos de prueba hacia Supabase...");
        AnalyticsManager.Instance.EnviarMetricas(payloadPrueba);
    }
}
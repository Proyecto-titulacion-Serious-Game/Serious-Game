#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Pedido explícito del usuario (2026-07-24, continuación de "Panel de Medición"): los 4 paneles
/// creados por <see cref="MultimeterPanelConversionTool"/> quedaron en una posición PLACEHOLDER
/// (centro de la zona + 1.4m, sin relación con el circuito real) porque no hay forma de detectar
/// "la pared" desde el YAML. En vez de eso, reposiciona cada panel cerca de los NODOS reales de su
/// reto (los mismos <see cref="NodeInteractable"/> que las puntas tocan para medir) — así el panel
/// queda usable de entrada (las puntas alcanzan con el largo de cable por defecto, 0.85m) aunque
/// después el usuario lo reubique a mano contra la pared visual de cada sala.
///
/// Cálculo: centroide de todos los NodeInteractable de la zona, + 1.1m de altura (mesa → altura de
/// panel de pared típica), sin offset horizontal (no hay forma de saber hacia dónde da la pared).
///
/// Menú: Tools → TITA → Multímetro → Reubicar paneles cerca de los nodos (auto)
/// </summary>
public static class MultimeterPanelSmartPlacement
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";
    const float  PanelHeightAboveNodes = 1.1f;

    [MenuItem("Tools/TITA/Multímetro/Reubicar paneles cerca de los nodos (auto)")]
    public static void Reposition()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>();
        if (gm == null)
        {
            Debug.LogError("[MultimeterPanelSmartPlacement] No hay GameManager en la escena.");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        var zonas = new (string label, GameObject zone)[]
        {
            ("Reto1", gm.reto1Zone),
            ("Reto2", gm.reto2Zone),
            ("Reto3", gm.reto3Zone),
            ("Reto4", gm.reto4Zone),
        };

        int moved = 0;
        foreach (var (label, zone) in zonas)
        {
            if (zone == null) continue;

            var panel = zone.transform.Find($"Multimeter_Panel_{label}");
            if (panel == null)
            {
                Debug.LogWarning($"[MultimeterPanelSmartPlacement] No existe 'Multimeter_Panel_{label}' en {label} " +
                                  "— corre primero 'Convertir a Panel de Pared'.");
                continue;
            }

            var nodos = zone.GetComponentsInChildren<NodeInteractable>(true)
                .Select(n => n.transform.position)
                .ToArray();

            if (nodos.Length == 0)
            {
                Debug.LogWarning($"[MultimeterPanelSmartPlacement] {label}: no se encontraron NodeInteractable " +
                                  "dentro de la zona — se deja la posición actual del panel.");
                continue;
            }

            Vector3 centroide = Vector3.zero;
            foreach (var p in nodos) centroide += p;
            centroide /= nodos.Length;

            Vector3 nuevaPos = centroide + Vector3.up * PanelHeightAboveNodes;
            panel.SetPositionAndRotation(nuevaPos, Quaternion.identity);

            float maxDist = nodos.Max(p => Vector3.Distance(p, nuevaPos));

            // El largo de cable por defecto (0.85m) alcanza para un multímetro portátil pegado al
            // circuito, pero un panel centrado sobre TODOS los nodos de la zona puede quedar más
            // lejos de los extremos — sin esto, la punta físicamente no llega y el jugador no puede
            // medir. Margen 20% sobre la distancia máxima real para no dejarlo al límite exacto.
            float cableNecesario = Mathf.Max(0.85f, maxDist * 1.2f);
            int cablesAjustados = 0;
            foreach (var cable in panel.GetComponentsInChildren<MultimeterCable>(true))
            {
                if (cable.maxCableLength < cableNecesario)
                {
                    cable.maxCableLength = cableNecesario;
                    cablesAjustados++;
                }
            }

            Debug.Log($"[MultimeterPanelSmartPlacement] '{panel.name}': reubicado sobre el centroide de " +
                      $"{nodos.Length} nodo(s), distancia máxima punta↔panel={maxDist:F2}m — " +
                      $"maxCableLength → {cableNecesario:F2}m en {cablesAjustados}/2 cables.");
            moved++;
        }

        var activeScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);

        Debug.Log($"[MultimeterPanelSmartPlacement] ✓ {moved}/4 paneles reubicados. Sigue siendo una posición " +
                  "automática, no una pared real — ajustar a mano si hace falta.");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
#endif

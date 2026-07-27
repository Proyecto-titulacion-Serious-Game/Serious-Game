using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// El componente Reto4BreadboardMode en escena tenía escalaResistor=(10,10,10) — un placeholder
/// isotrópico que NO coincide con el valor por defecto calibrado a mano en el propio código fuente
/// ((13.8603153, 22.1765041, 10.2368851): un número con 7 decimales, claramente medido, no escrito
/// a ojo). Esa escala anisotrópica es la que hace que las patas del resistor entregado en el Reto 4
/// alcancen huecos DISTINTOS del bareboard (ver comentario en Reto4BreadboardMode.cs) — con la escala
/// isotrópica más chica, el resistor queda más pequeño Y el eje de patas que elige
/// ProtoboardConnector.EnsureLeads() (el más largo del bounding box) puede no ser el que se diseñó.
///
/// Restaura el valor calibrado del código. La ORIENTACIÓN final aún depende de la inclinación real
/// del tablero — falta confirmar visualmente en VR que el resistor queda horizontal como en el Reto 2.
///
/// Ejecutar: Tools → TITA → Reto 4 → Restaurar escala calibrada del resistor
/// </summary>
public static class Reto4ResistorScaleFix
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";
    static readonly Vector3 EscalaCalibrada = new Vector3(13.8603153f, 22.1765041f, 10.2368851f);

    [MenuItem("Tools/TITA/Reto 4/Restaurar escala calibrada del resistor")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var mode = Object.FindAnyObjectByType<Reto4BreadboardMode>(FindObjectsInactive.Include);
        if (mode == null)
        {
            Debug.LogError("[Reto4ResistorScaleFix] No encontré Reto4BreadboardMode en la escena.");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        Vector3 antes = mode.escalaResistor;
        Undo.RecordObject(mode, "Restaurar escala resistor Reto4");
        mode.escalaResistor = EscalaCalibrada;
        EditorUtility.SetDirty(mode);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[Reto4ResistorScaleFix] escalaResistor: {antes} -> {mode.escalaResistor}. " +
                  "Aplica al PRÓXIMO resistor que entregue el Técnico (ExplorerComponentReceiver lee " +
                  "Reto4BreadboardMode.ResistorScaleReto4 al configurar la pieza).");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}

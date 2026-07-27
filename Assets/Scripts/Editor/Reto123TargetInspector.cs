using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Diagnóstico de SOLO LECTURA: lee los valores objetivo REALES de los Retos 1-3 en la escena
/// (correctResistance del resistor con falla, qué LED necesita polaridad, qué capacitor necesita
/// polaridad) para poder armar un test de "juego completo" con los valores correctos, sin adivinar.
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod Reto123TargetInspector.Run -logFile -
/// </summary>
public static class Reto123TargetInspector
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Reto123 - Inspeccionar valores objetivo (solo lectura)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var gm = Object.FindAnyObjectByType<GameManager>();
        Debug.Log($"[Target] GameManager encontrado={gm != null}");

        void Zona(string nombreCampo, GameObject zona)
        {
            if (zona == null) { Debug.LogWarning($"  {nombreCampo}: NULL"); return; }
            Debug.Log($"  {nombreCampo}=\"{zona.name}\" activeSelf={zona.activeSelf}");

            foreach (var r in zona.GetComponentsInChildren<Resistor>(true))
                Debug.Log($"##R## zona={nombreCampo} name=\"{r.name}\" resistance={r.resistance} " +
                          $"correctResistance={r.correctResistance} faultyResistance={r.faultyResistance} " +
                          $"hasFault={r.hasFault} tolerancePercent={r.tolerancePercent} " +
                          $"wired={(r.nodeA != null && r.nodeB != null)}");

            foreach (var l in zona.GetComponentsInChildren<LED>(true))
                Debug.Log($"##LED## zona={nombreCampo} name=\"{l.name}\" polarityInverted={l.polarityInverted} " +
                          $"resistance={l.resistance} forwardVoltage={l.forwardVoltage} isOpenCircuit={l.isOpenCircuit} " +
                          $"wired={(l.nodeA != null && l.nodeB != null)}");

            foreach (var c in zona.GetComponentsInChildren<Capacitor>(true))
                Debug.Log($"##CAP## zona={nombreCampo} name=\"{c.name}\" polarityInverted={c.polarityInverted} " +
                          $"wired={(c.nodeA != null && c.nodeB != null)}");
        }

        if (gm == null) return;

        var so = new SerializedObject(gm);
        GameObject GetZona(string prop) => (so.FindProperty(prop)?.objectReferenceValue as GameObject);

        Zona("reto1Zone", GetZona("reto1Zone"));
        Zona("reto2Zone", GetZona("reto2Zone"));
        Zona("reto3Zone", GetZona("reto3Zone"));
        Zona("reto4Zone", GetZona("reto4Zone"));
    }
}

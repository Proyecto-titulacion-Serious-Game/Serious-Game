using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Verifica: (1) ProtoboardSimulator.SepararacionMinimaEntreNetsDistintos() da el mismo
/// número medido a mano antes (~17.7cm), y (2) ExplorerComponentReceiver.AplicarEscalaResistorReto4
/// produce una escala real y sensata sobre el prefab real del resistor entregado.</summary>
public static class Reto4VerificarEscalaResistorDinamica
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";
    const string ResistorPrefabPath = "Assets/Prefabs/Delivered/Delivered_Resistor.prefab";

    [MenuItem("Tools/TITA/Reto 4/Verificar escala dinamica del resistor (headless)")]
    public static void Run()
    {
        int fails = 0;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var gm = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        var sim = gm.protoSim;

        var buildNodeMap = typeof(ProtoboardSimulator).GetMethod("BuildNodeMap", BindingFlags.NonPublic | BindingFlags.Instance);
        buildNodeMap.Invoke(sim, null);

        float span = sim.SepararacionMinimaEntreNetsDistintos();
        Debug.Log($"[VerifEscala] SepararacionMinimaEntreNetsDistintos() = {span * 100f:F3} cm (esperado ~17.7cm)");
        if (span < 0.10f || span > 0.30f) { fails++; Debug.LogError("[VerifEscala] ✗ Fuera del rango esperado."); }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ResistorPrefabPath);
        var instancia = Object.Instantiate(prefab);

        var receiverGo = new GameObject("Test_Receiver");
        var receiver = receiverGo.AddComponent<ExplorerComponentReceiver>();
        var gmField = typeof(ExplorerComponentReceiver).GetField("_gm", BindingFlags.NonPublic | BindingFlags.Instance);
        gmField.SetValue(receiver, gm);

        var metodo = typeof(ExplorerComponentReceiver).GetMethod("AplicarEscalaResistorReto4", BindingFlags.NonPublic | BindingFlags.Instance);
        Vector3 antes = instancia.transform.localScale;
        metodo.Invoke(receiver, new object[] { instancia });
        Vector3 despues = instancia.transform.localScale;

        var rend = instancia.GetComponentInChildren<Renderer>();
        Debug.Log($"[VerifEscala] localScale antes={antes} despues={despues}  bounds.size resultante={rend.bounds.size} " +
                  $"(el eje más largo debe ≈ {span * 100f:F1} cm)");

        float ejeMasLargo = Mathf.Max(rend.bounds.size.x, rend.bounds.size.y, rend.bounds.size.z);
        bool ok = Mathf.Abs(ejeMasLargo - span) < 0.005f; // tolerancia 5mm
        if (!ok) { fails++; Debug.LogError($"[VerifEscala] ✗ El eje más largo del mesh resultante ({ejeMasLargo*100f:F2}cm) no coincide con la separación real medida."); }
        else Debug.Log("[VerifEscala] ✓ El resistor entregado ahora mide exactamente lo que separa 2 slots reales de nets distintas.");

        Object.DestroyImmediate(instancia);
        Object.DestroyImmediate(receiverGo);

        Debug.Log(fails == 0 ? "\n[VerifEscala] ===== RESULTADO: ✓ OK =====" : $"\n[VerifEscala] ===== RESULTADO: ✗ {fails} fallo(s) =====");
        if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
    }
}

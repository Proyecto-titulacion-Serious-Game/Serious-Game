using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerificarPrefabsLED
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Verificar prefabs LED del receptor real")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var receptores = Object.FindObjectsByType<ExplorerComponentReceiver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var receptor = receptores.FirstOrDefault(r => r.gameObject.activeInHierarchy);
        if (receptor == null) { Debug.LogError("[VerifLED] No hay receptor activo."); if (Application.isBatchMode) EditorApplication.Exit(1); return; }

        Debug.Log($"[VerifLED] Receptor activo: '{receptor.name}'");
        Debug.Log($"[VerifLED] ledPrefab (base)={(receptor.ledPrefab != null ? receptor.ledPrefab.name : "NULL")}");
        Debug.Log($"[VerifLED] ledRedPrefab={(receptor.ledRedPrefab != null ? receptor.ledRedPrefab.name : "NULL")}");
        Debug.Log($"[VerifLED] ledYellowPrefab={(receptor.ledYellowPrefab != null ? receptor.ledYellowPrefab.name : "NULL")}");
        Debug.Log($"[VerifLED] ledGreenPrefab={(receptor.ledGreenPrefab != null ? receptor.ledGreenPrefab.name : "NULL")}");

        // Inspeccionar cada prefab del LED por variante: color real del material y polarityInverted default.
        void Inspeccionar(string nombre, GameObject prefab)
        {
            if (prefab == null) { Debug.Log($"[VerifLED]   {nombre}: prefab NULL"); return; }
            var led = prefab.GetComponentInChildren<LED>(true);
            var rends = prefab.GetComponentsInChildren<Renderer>(true);
            Debug.Log($"[VerifLED]   {nombre}: prefab='{prefab.name}' " +
                      $"polarityInverted={(led != null ? led.polarityInverted.ToString() : "sin LED")} " +
                      $"forwardVoltage={(led != null ? led.forwardVoltage.ToString("F2") : "-")} " +
                      $"resistance={(led != null ? led.resistance.ToString("F1") : "-")} " +
                      $"maxSafeCurrent={(led != null ? led.maxSafeCurrent.ToString("F3") : "-")} " +
                      $"cristalTint={(led != null ? $"RGBA({led.cristalTint.r:F2},{led.cristalTint.g:F2},{led.cristalTint.b:F2},{led.cristalTint.a:F2})" : "-")} " +
                      $"renderers={rends.Length}");
            foreach (var r in rends)
                Debug.Log($"[VerifLED]     renderer '{r.name}' localPos={r.transform.localPosition} bounds.size={r.bounds.size} bounds.center={r.bounds.center}");
            var rootRb = prefab.GetComponent<Rigidbody>();
            var rootCol = prefab.GetComponent<Collider>();
            var grab = prefab.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(true);
            var connector = prefab.GetComponentInChildren<ProtoboardConnector>(true);
            Debug.Log($"[VerifLED]     root: hasRigidbody={rootRb != null} hasCollider={rootCol != null} hasXRGrab={grab != null} hasProtoboardConnector={connector != null} " +
                      $"root.localScale={prefab.transform.localScale}");
        }
        Inspeccionar("Red", receptor.ledRedPrefab);
        Inspeccionar("Yellow", receptor.ledYellowPrefab);
        Inspeccionar("Green", receptor.ledGreenPrefab);

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}

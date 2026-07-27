using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class VerificarMultimetroInteraccion
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";

    [MenuItem("Tools/TITA/Verificar interaccion del multimetro")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        var root = all.FirstOrDefault(t => t.name == "Multimeter_VR_Art");
        if (root == null) { Debug.LogError("[VerifMMInt] No until Multimeter_VR_Art."); if (Application.isBatchMode) EditorApplication.Exit(1); return; }

        void InspeccionarProbe(string nombre, Transform t)
        {
            if (t == null) { Debug.LogError($"[VerifMMInt] {nombre}: NO ENCONTRADO"); return; }
            var grab = t.GetComponent<XRGrabInteractable>();
            var cols = t.GetComponents<Collider>();
            var probe = t.GetComponent<MultimeterProbe>();
            var rb = t.GetComponent<Rigidbody>();
            Debug.Log($"[VerifMMInt] {nombre} ('{t.name}'):");
            Debug.Log(grab != null
                ? $"[VerifMMInt]   XRGrabInteractable: enabled={grab.enabled} interactionLayers.value={grab.interactionLayers.value} layer(GO)={t.gameObject.layer} " +
                  $"colliders.Count={grab.colliders.Count} movementType={grab.movementType}"
                : $"[VerifMMInt]   XRGrabInteractable: NINGUNO (no agarrable — el agarre ahora vive en Rod_Visual) layer(GO)={t.gameObject.layer}");
            foreach (var c in cols)
                Debug.Log($"[VerifMMInt]   Collider '{c.GetType().Name}' enabled={c.enabled} isTrigger={c.isTrigger}");
            // Nota: en Unity 6, `rb?.isKinematic` con rb==null puede lanzar MissingComponentException
            // (el operador ?. no protege de forma confiable el acceso nativo) — chequeo explícito.
            Debug.Log(rb != null
                ? $"[VerifMMInt]   Rigidbody: isKinematic={rb.isKinematic} useGravity={rb.useGravity}"
                : "[VerifMMInt]   Rigidbody: NINGUNO (sensor de proximidad puro)");
            Debug.Log($"[VerifMMInt]   MultimeterProbe: probeType={probe?.probeType} controllerNode={probe?.controllerNode} multimeter(asignado)={(probe?.multimeter != null ? probe.multimeter.name : "NULL(auto-busca en Awake)")}");
        }

        InspeccionarProbe("Probe_Red_Tip", FindDeep(root, "Probe_Red_Tip"));
        InspeccionarProbe("Probe_Black_Tip", FindDeep(root, "Probe_Black_Tip"));

        void InspeccionarRod(string nombre, Transform t)
        {
            if (t == null) { Debug.LogError($"[VerifMMInt] {nombre}: NO ENCONTRADO"); return; }
            var grab = t.GetComponent<XRGrabInteractable>();
            var rb   = t.GetComponent<Rigidbody>();
            var col  = t.GetComponent<Collider>();
            Debug.Log(grab != null && rb != null && col != null
                ? $"[VerifMMInt] {nombre}: OK — Collider={col.GetType().Name}(isTrigger={col.isTrigger}) " +
                  $"Rigidbody(isKinematic={rb.isKinematic}) XRGrabInteractable(colliders.Count={grab.colliders.Count}, movementType={grab.movementType})"
                : $"[VerifMMInt] {nombre}: INCOMPLETO — grab={(grab != null)} rb={(rb != null)} col={(col != null)}");
        }

        InspeccionarRod("Rod_Visual (rojo)", FindDeep(root, "Probe_Red_Tip")?.parent);
        InspeccionarRod("Rod_Visual (negro)", FindDeep(root, "Probe_Black_Tip")?.parent);

        var modeBtn = FindDeep(root, "Mode_Button");
        if (modeBtn != null)
        {
            var xsi = modeBtn.GetComponent<XRSimpleInteractable>();
            var col = modeBtn.GetComponent<Collider>();
            var mmb = modeBtn.GetComponent<MultimeterModeButton>();
            Debug.Log($"[VerifMMInt] Mode_Button: XRSimpleInteractable.enabled={xsi?.enabled} interactionLayers.value={xsi?.interactionLayers.value} layer(GO)={modeBtn.gameObject.layer} " +
                      $"collider.enabled={col?.enabled} multimeter(asignado)={(mmb?.multimeter != null ? mmb.multimeter.name : "NULL(auto-busca en Awake)")}");
        }
        else Debug.LogError("[VerifMMInt] No until Mode_Button.");

        // Comparar interactionLayers del multímetro ROOT (el grab principal) contra las puntas —
        // si el layer mask del jugador no incluye el layer de un objeto, selectEntered nunca dispara.
        var rootGrab = root.GetComponent<XRGrabInteractable>();
        Debug.Log($"[VerifMMInt] Multimeter_VR_Art (root) XRGrabInteractable.interactionLayers.value={rootGrab?.interactionLayers.value} layer(GO)={root.gameObject.layer}");

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}

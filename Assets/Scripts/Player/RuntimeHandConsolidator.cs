using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

/// <summary>
/// Consolida las manos VR del Explorador en runtime, poniendo la mano procedural EXACTAMENTE
/// en el GameObject del controlador que el usuario ve y usa: el que tiene el interactor
/// Near-Far (o Poke). Esos son los controladores stock del XR Origin (con TrackedPoseDriver),
/// así que la mano queda pegada a la posición real del mando.
///
/// La escena tenía varios HandModelController en GOs paralelos (con XRDirectInteractor) que NO
/// coincidían con los Near-Far/Poke → la mano salía en otro sitio. Aquí:
///   1) Se elige UN GO por lado: el que tiene NearFarInteractor (preferido) o XRPokeInteractor.
///   2) Se asegura el HandModelController en ESE GO (la mano se construye ahí, sobre el mando).
///   3) Se desactiva cualquier otro HandModelController (duplicados) para no ver manos dobles.
///
/// Añadir a un GO persistente de la escena (ej. XR Origin o GameManager).
/// </summary>
public class RuntimeHandConsolidator : MonoBehaviour
{
    [Header("Modelo de manos (opcional)")]
    [Tooltip("Prefab/FBX de la mano. Si se asigna, se usa en vez de la mano procedural de primitivas. " +
             "Déjalo vacío para las manos de siempre. (Asignar AQUÍ, no en la escena: la mano se crea en runtime " +
             "sobre el mando real y este componente le pasa el prefab.)")]
    public GameObject handPrefab;
    [Tooltip("Prefab específico para la mano DERECHA. Si lo dejas vacío, usa 'handPrefab' espejado en X.")]
    public GameObject handPrefabDerecha;
    [Tooltip("Rotación local del modelo (grados). Ajusta si la mano sale girada respecto al mando.")]
    public Vector3 rotacionPrefab = Vector3.zero;
    [Tooltip("Escala uniforme del modelo.")]
    public float escalaPrefab = 1f;

    [Header("Rayo de agarre desde la PALMA")]
    [Tooltip("Mueve el origen del rayo/línea de agarre del controlador (que sale por la muñeca) a la palma de la mano.")]
    public bool rayoDesdeLaPalma = true;
    [Tooltip("Desplazamiento del origen del rayo respecto al controlador, en metros (X=lateral, Y=arriba/abajo, " +
             "Z=adelante hacia los dedos). Ajústalo EN PLAY hasta que la línea salga de la palma.")]
    public Vector3 offsetOrigenPalma = new Vector3(0f, -0.02f, 0.05f);
    [Tooltip("Giro del rayo respecto al controlador, en grados. (0,0,0) = apunta igual que ahora pero desde la palma. " +
             "X negativo lo inclina hacia donde apuntan los dedos.")]
    public Vector3 rotacionRayoPalma = Vector3.zero;
    [Tooltip("Mover también el agarre CERCANO (esfera de toque directo) a la palma, no solo la línea lejana.")]
    public bool moverAgarreCercano = true;

    // Anclas creadas en runtime (una por mano) para poder reajustar los offsets en vivo desde el Inspector.
    Transform _anchorL, _anchorR;

    void Awake()
    {
        // ── 1) Elegir el GO de cada mano por su interactor Near-Far / Poke ──
        GameObject left  = FindControllerGO(HandModelController.HandSide.Left)  ?? FindHandGOByName("Left");
        GameObject right = FindControllerGO(HandModelController.HandSide.Right) ?? FindHandGOByName("Right");

        if (left == null && right == null)
            Debug.LogWarning("[HandConsolidator] No se encontró ningún controlador (Near-Far/Poke/nombre).");

        // ¿Se usan las manos del PROPIO avatar (RobotKyle) en vez de las procedurales?
        var rig = FindAnyObjectByType<VRRig>(FindObjectsInactive.Include);
        bool manosAvatar = rig != null && rig.usarManosDelAvatar;

        if (manosAvatar)
        {
            // Manos del robot: NO construir procedurales; apagar todas; el VRRig mueve las del avatar.
            DisableHandsExcept(new List<GameObject>());
            Debug.Log("[HandConsolidator] Modo MANOS DEL AVATAR → manos procedurales desactivadas; " +
                      "el VRRig mueve los brazos/dedos del robot hacia los mandos.");
        }
        else
        {
            // ── Manos PROCEDURALES sobre el Near-Far/Poke ──
            var elegidos = new List<GameObject>();
            if (left  != null) { EnsureHand(left,  HandModelController.HandSide.Left);  elegidos.Add(left);  }
            if (right != null) { EnsureHand(right, HandModelController.HandSide.Right); elegidos.Add(right); }
            DisableHandsExcept(elegidos);
        }

        // ── Yema táctil en el GO del CONTROL (independiente del modo de manos) ──
        //    Permite PRESIONAR botones físicos (VRValidationButton) por toque: ni las manos
        //    procedurales ni las del robot traen colliders, así que sin esto OnTriggerEnter
        //    jamás dispara. Trigger pequeño + RB kinemático, no empuja ni interfiere el agarre.
        AsegurarYemaTactil(left);
        AsegurarYemaTactil(right);

        // ── Alinear los targets de IK del VRRig a ESTOS controladores (Near-Far/Poke) ──
        //    Así el brazo del robot apunta EXACTAMENTE a donde está el mando.
        AlinearTargetsVRRig(left, right);

        // ── Mover el origen del rayo/línea de agarre de la muñeca a la palma ──
        //    Se difiere un frame para que los casters (CurveInteractionCaster) ya estén inicializados
        //    y no pisen nuestro castOrigin con el del propio controlador.
        if (rayoDesdeLaPalma && isActiveAndEnabled)
            StartCoroutine(ConfigurarRayosDesdePalma(left, right));
    }

    IEnumerator ConfigurarRayosDesdePalma(GameObject left, GameObject right)
    {
        yield return null;   // esperar a que los casters terminen de inicializarse
        _anchorL = RetargetRayoDesdePalma(left);
        _anchorR = RetargetRayoDesdePalma(right);
    }

    /// <summary>
    /// Reancla el rayo de agarre (y su línea visible) de un controlador a un punto en la PALMA.
    /// Crea un hijo "PalmRayOrigin" en el GO del NearFarInteractor y lo asigna como origen del
    /// caster lejano (la línea), opcionalmente del cercano (toque directo), del XRRayInteractor
    /// clásico (si lo hubiera) y del XRInteractorLineVisual (para que la línea SALGA de ahí).
    /// </summary>
    Transform RetargetRayoDesdePalma(GameObject go)
    {
        if (go == null) return null;

        var nf = go.GetComponentInChildren<NearFarInteractor>(true);
        Transform parent = nf != null ? nf.transform : go.transform;

        Transform anchor = parent.Find("PalmRayOrigin");
        if (anchor == null)
        {
            anchor = new GameObject("PalmRayOrigin").transform;
            anchor.SetParent(parent, false);
        }
        anchor.localPosition = offsetOrigenPalma;
        anchor.localRotation = Quaternion.Euler(rotacionRayoPalma);

        if (nf != null)
        {
            if (nf.farInteractionCaster != null)
                nf.farInteractionCaster.castOrigin = anchor;
            if (moverAgarreCercano && nf.nearInteractionCaster != null)
                nf.nearInteractionCaster.castOrigin = anchor;
        }

        // XRRayInteractor clásico (por si alguna mano lo usa en vez del Near-Far).
        var ray = go.GetComponentInChildren<XRRayInteractor>(true);
        if (ray != null) ray.rayOriginTransform = anchor;

        // La línea visible: forzar su origen al ancla de la palma.
        foreach (var lv in go.GetComponentsInChildren<XRInteractorLineVisual>(true))
        {
            lv.overrideInteractorLineOrigin = true;
            lv.lineOriginTransform = anchor;
        }

        Debug.Log($"[HandConsolidator] Rayo de agarre reanclado a la PALMA en '{NombreRuta(parent)}' " +
                  $"(offset {offsetOrigenPalma}).");
        return anchor;
    }

    void Update()
    {
        // Permite ajustar los offsets EN VIVO desde el Inspector y ver la línea moverse al instante.
        if (!rayoDesdeLaPalma) return;
        var rot = Quaternion.Euler(rotacionRayoPalma);
        if (_anchorL != null) { _anchorL.localPosition = offsetOrigenPalma; _anchorL.localRotation = rot; }
        if (_anchorR != null) { _anchorR.localPosition = offsetOrigenPalma; _anchorR.localRotation = rot; }
    }

    static void AsegurarYemaTactil(GameObject go)
    {
        if (go == null || go.transform.Find("Hand_TouchTip_Ctrl") != null) return;
        var tip = new GameObject("Hand_TouchTip_Ctrl");   // "hand" en el nombre → lo reconocen los detectores de toque
        tip.transform.SetParent(go.transform, false);
        tip.transform.localPosition = new Vector3(0f, -0.02f, 0.06f);   // zona de la palma frente al mando
        var sc = tip.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius    = 0.02f;
        var rb = tip.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;
    }

    void AlinearTargetsVRRig(GameObject left, GameObject right)
    {
        var rig = FindAnyObjectByType<VRRig>(FindObjectsInactive.Include);
        if (rig == null) return;
        if (left  != null) rig.leftHandTarget  = left.transform;
        if (right != null) rig.rightHandTarget = right.transform;
        Debug.Log("[HandConsolidator] Targets de IK del VRRig alineados a los controladores Near-Far/Poke " +
                  $"(L='{(left ? left.name : "—")}', R='{(right ? right.name : "—")}').");
    }

    /// <summary>
    /// GO del controlador de un lado: prefiere el que tiene NearFarInteractor; si no, el Poke.
    /// Estos son los mandos stock (con TrackedPoseDriver) → la mano queda sobre el Poke/Near-Far.
    /// </summary>
    GameObject FindControllerGO(HandModelController.HandSide side)
    {
        // Preferir Near-Far
        foreach (var nf in FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include))
            if (DetectSide(nf.transform) == side) return nf.gameObject;

        // Si no hay Near-Far, usar Poke
        foreach (var pk in FindObjectsByType<XRPokeInteractor>(FindObjectsInactive.Include))
            if (DetectSide(pk.transform) == side) return pk.gameObject;

        return null;
    }

    /// <summary>Detecta el lado subiendo por la jerarquía buscando "left"/"right" en los nombres.</summary>
    static HandModelController.HandSide? DetectSide(Transform t)
    {
        for (var cur = t; cur != null; cur = cur.parent)
        {
            string n = cur.name.ToLowerInvariant();
            if (n.Contains("left")  || n.Contains("izq")) return HandModelController.HandSide.Left;
            if (n.Contains("right") || n.Contains("der")) return HandModelController.HandSide.Right;
        }
        return null;
    }

    /// <summary>Asegura un HandModelController activo del lado correcto en el GO indicado.</summary>
    void EnsureHand(GameObject go, HandModelController.HandSide side)
    {
        // Limpiar placeholders heredados del prefab.
        for (int i = go.transform.childCount - 1; i >= 0; i--)
            if (go.transform.GetChild(i).name.StartsWith("HandPlaceholder"))
                Destroy(go.transform.GetChild(i).gameObject);

        var hmc = go.GetComponent<HandModelController>();
        bool nuevo = hmc == null;
        if (nuevo)
        {
            hmc = go.AddComponent<HandModelController>();
            hmc.hand = side;
        }
        else
        {
            hmc.hand    = side;
            hmc.enabled = true;
        }

        // Pasar el modelo de manos (si hay uno asignado en este consolidador). La mano se crea
        // aquí en runtime, así que el prefab debe venir de este componente, no de la escena.
        ConfigurarModelo(hmc, side);

        if (nuevo)
        {
            Debug.Log($"[HandConsolidator] Mano AÑADIDA en '{NombreRuta(go.transform)}' (lado {side}) → sobre el Near-Far/Poke.");
        }
        else
        {
            // Reconstruir para reflejar el modelo configurado; si no, reactivar la geometría existente.
            if (handPrefab != null || handPrefabDerecha != null)
            {
                hmc.RebuildInEditor();
            }
            else
            {
                for (int i = go.transform.childCount - 1; i >= 0; i--)
                    if (go.transform.GetChild(i).name.StartsWith("Hand_"))
                        go.transform.GetChild(i).gameObject.SetActive(true);
            }
            Debug.Log($"[HandConsolidator] Mano HABILITADA en '{NombreRuta(go.transform)}' (lado {side}).");
        }
    }

    /// <summary>Copia el prefab/orientación de manos de este consolidador al HandModelController.</summary>
    void ConfigurarModelo(HandModelController hmc, HandModelController.HandSide side)
    {
        var prefab = side == HandModelController.HandSide.Right && handPrefabDerecha != null
                   ? handPrefabDerecha : handPrefab;
        hmc.handPrefab = prefab;
        // Solo espejar si reusamos un único prefab para ambas manos (no hay uno dedicado de la derecha).
        hmc.espejarParaDerecha = handPrefabDerecha == null;
        hmc.rotacionPrefab = rotacionPrefab;
        hmc.escalaPrefab   = escalaPrefab;
    }

    /// <summary>Desactiva todas las manos procedurales que NO sean las elegidas (duplicados).</summary>
    void DisableHandsExcept(List<GameObject> elegidos)
    {
        foreach (var hmc in FindObjectsByType<HandModelController>(FindObjectsInactive.Include))
        {
            if (elegidos.Contains(hmc.gameObject)) continue;

            // Ocultar la geometría ya construida y apagar el componente.
            for (int i = hmc.transform.childCount - 1; i >= 0; i--)
                if (hmc.transform.GetChild(i).name.StartsWith("Hand_"))
                    hmc.transform.GetChild(i).gameObject.SetActive(false);

            hmc.enabled = false;
            Debug.Log($"[HandConsolidator] Mano duplicada DESACTIVADA en '{NombreRuta(hmc.transform)}'.");
        }
    }

    // Prueba nombres estándar de XRI 3.x (fallback si no hay Near-Far/Poke).
    static GameObject FindHandGOByName(string side)
    {
        string[] candidates = {
            $"{side} Controller", $"{side}Hand Controller", $"{side}Hand_Controller",
            $"{side}Hand", $"XRController ({side})",
        };
        foreach (var name in candidates)
        {
            var go = GameObject.Find(name);
            if (go != null) return go;
        }
        return null;
    }

    static string NombreRuta(Transform t)
    {
        string r = t.name;
        for (var p = t.parent; p != null; p = p.parent) r = p.name + "/" + r;
        return r;
    }
}

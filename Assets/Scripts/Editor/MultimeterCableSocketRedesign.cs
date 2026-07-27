using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Rediseño "piezas de Lego" del multímetro (pedido por el usuario 2026-07-24): en vez de que las
/// puntas cuelguen de un ancla fija dentro del cuerpo (SpringJoint contra el propio multímetro),
/// cada cable termina en un CONECTOR (Jack_Nub) independiente, agarrable y con física real, que el
/// jugador debe enchufar a mano en un socket físico (VΩmA para la roja, COM para la negra) del
/// cuerpo del multímetro. Sin ambos conectados, la pantalla muestra "CONECTAR CABLES".
///
/// Pasos (sobre el prefab real, vía API — no YAML a mano, para no romper referencias cruzadas):
///   1. Jack_Nub_Red/Black pasan a ser hijos DIRECTOS de la raíz del prefab (no de Cable_Anchor_*)
///      y ganan Rigidbody (no-kinematic), Collider y XRGrabInteractable — piezas físicas libres.
///   2. MultimeterCable.anchorPoint de cada cable pasa a apuntar al Jack_Nub correspondiente (ya
///      no al Cable_Anchor_* vacío) — el resorte ahora une la punta con el CONECTOR, no con el
///      cuerpo del multímetro.
///   3. Se crean Socket_VmA y Socket_COM (XRSocketInteractor + SphereCollider trigger) en las
///      posiciones donde estaban los jacks originales (Cable_Anchor_Red/Black) — el "agujero"
///      físico real del multímetro.
///   4. Multimeter.puertoVmA / puertoCOM quedan conectados a esos sockets.
///
/// Menú: Tools → TITA → Multímetro → Fix 4 — jacks desacoplados + sockets VmA/COM (headless-safe)
/// </summary>
public static class MultimeterCableSocketRedesign
{
    const string PREFAB_PATH = "Assets/Prefabs/Multimeter_VR_Art.prefab";

    // Radio LOCAL del collider de agarre del jack (su localScale es ~0.003-0.004 → mundo ≈ 1.4-1.6cm).
    const float JACK_GRAB_RADIUS   = 4f;
    // Radio del trigger del socket, en unidades de MUNDO (el socket vive en un GO nuevo, escala 1).
    const float SOCKET_TRIGGER_RADIUS = 0.02f;

    [MenuItem("Tools/TITA/Multímetro/Fix 4 — jacks desacoplados + sockets VmA-COM (headless-safe)")]
    public static void Apply()
    {
        var go = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
        if (go == null) { Debug.LogError($"[MultimeterCableSocketRedesign] No se pudo cargar {PREFAB_PATH}"); return; }

        int jacksOk = 0;
        Transform anchorRed = null, anchorBlack = null;

        foreach (var colorName in new[] { "Red", "Black" })
        {
            var cableT  = go.transform.Find($"Cable_{colorName}");
            var anchorT = cableT != null ? cableT.Find($"Cable_Anchor_{colorName}") : null;
            var jackT   = anchorT != null ? anchorT.Find($"Jack_Nub_{colorName}") : null;

            if (cableT == null || anchorT == null || jackT == null)
            {
                Debug.LogWarning($"[MultimeterCableSocketRedesign] Faltan piezas de '{colorName}' " +
                                  $"(cable={cableT != null}, anchor={anchorT != null}, jack={jackT != null}).");
                continue;
            }

            if (colorName == "Red") anchorRed = anchorT; else anchorBlack = anchorT;

            // ── 1) Jack_Nub → hijo directo de la raíz, física libre ──────────────────────
            jackT.SetParent(go.transform, worldPositionStays: true);
            var jackGO = jackT.gameObject;

            var rb = jackGO.GetComponent<Rigidbody>();
            if (rb == null) rb = Undo.AddComponent<Rigidbody>(jackGO);
            rb.isKinematic = false;
            rb.useGravity  = true;

            var col = jackGO.GetComponent<Collider>();
            if (col == null)
            {
                var sc = Undo.AddComponent<SphereCollider>(jackGO);
                sc.radius = JACK_GRAB_RADIUS;
                col = sc;
            }

            var grab = jackGO.GetComponent<XRGrabInteractable>();
            if (grab == null) grab = Undo.AddComponent<XRGrabInteractable>(jackGO);

            // ── 2) El cable ahora ancla en el CONECTOR, no en el punto fijo del cuerpo ───
            var cable = cableT.GetComponent<MultimeterCable>();
            if (cable != null) cable.anchorPoint = jackT;

            jacksOk++;
            Debug.Log($"[MultimeterCableSocketRedesign] Jack_Nub_{colorName}: reparentado a la raíz, " +
                      $"Rigidbody no-kinematico, Collider r={JACK_GRAB_RADIUS}, XRGrabInteractable. " +
                      $"MultimeterCable.anchorPoint → Jack_Nub_{colorName}.");
        }

        // ── 3) Sockets VmA / COM en la posición de los jacks originales ──────────────────
        var socketVmA = CreateSocket(go.transform, "Socket_VmA", anchorRed);
        var socketCOM = CreateSocket(go.transform, "Socket_COM", anchorBlack);

        // ── 4) Conectar el script Multimeter ─────────────────────────────────────────────
        // OBSOLETO desde el rediseño a panel fijo (2026-07-24, "Panel de Medición"): Multimeter
        // ya no tiene puertoVmA/puertoCOM (los sockets se eliminaron del panel — jacks soldados,
        // siempre "conectados"). Esta herramienta ya cumplió su propósito histórico; este paso
        // queda como no-op para que el archivo siga compilando.
        var multimeter = go.GetComponent<Multimeter>();
        if (multimeter == null)
            Debug.LogError("[MultimeterCableSocketRedesign] No encontré el componente Multimeter en la raíz del prefab.");

        PrefabUtility.SaveAsPrefabAsset(go, PREFAB_PATH);
        PrefabUtility.UnloadPrefabContents(go);
        AssetDatabase.Refresh();

        Debug.Log($"[MultimeterCableSocketRedesign] ✓ Listo. Jacks configurados={jacksOk}/2, " +
                  $"sockets creados={(socketVmA != null ? 1 : 0) + (socketCOM != null ? 1 : 0)}/2.");

        if (Application.isBatchMode)
            EditorApplication.Exit(jacksOk == 2 && socketVmA != null && socketCOM != null ? 0 : 1);
    }

    static XRSocketInteractor CreateSocket(Transform root, string name, Transform posicionDeReferencia)
    {
        if (posicionDeReferencia == null)
        {
            Debug.LogWarning($"[MultimeterCableSocketRedesign] Sin referencia de posición para '{name}' — se omite.");
            return null;
        }

        var existing = root.Find(name);
        var socketGO = existing != null ? existing.gameObject : new GameObject(name);
        if (existing == null) Undo.RegisterCreatedObjectUndo(socketGO, $"Crear {name}");

        socketGO.transform.SetParent(root, worldPositionStays: false);
        socketGO.transform.position = posicionDeReferencia.position;
        socketGO.transform.rotation = posicionDeReferencia.rotation;

        var col = socketGO.GetComponent<SphereCollider>();
        if (col == null) col = Undo.AddComponent<SphereCollider>(socketGO);
        col.isTrigger = true;
        col.radius    = SOCKET_TRIGGER_RADIUS;

        var socket = socketGO.GetComponent<XRSocketInteractor>();
        if (socket == null) socket = Undo.AddComponent<XRSocketInteractor>(socketGO);
        socket.showInteractableHoverMeshes = false;   // sin material de hover asignado — activarlo a mano si se quiere el brillo

        Debug.Log($"[MultimeterCableSocketRedesign] '{name}' creado/actualizado en {posicionDeReferencia.position} " +
                  $"(trigger r={SOCKET_TRIGGER_RADIUS}).");

        return socket;
    }
}

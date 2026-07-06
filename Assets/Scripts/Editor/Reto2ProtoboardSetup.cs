using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ETAPA 1 del piloto de protoboard libre para el Reto 2. Provisiona, bajo Reto2_Zone y de forma
/// NO DESTRUCTIVA (el Reto 2 actual sigue intacto), un breadboard alimentado por batería:
///   • cuerpo del breadboard (placa),
///   • riel VCC (arriba) y riel GND (abajo) — slots con mismo railId = misma tira de cobre,
///   • columnas de amarre (COL_n) para colocar componentes entre los rieles,
///   • una VoltageSource de 9V cuyos nodeA/nodeB apuntan a los nodos de los rieles VCC/GND
///     (BuildNodeMap del ProtoboardSimulator REUTILIZA el ElectricalNode que ya está en el slot),
///   • ProtoboardSimulator con todosLosSlots poblado.
///
/// El motor (SimulateSingleSource + MNA) ya resuelve este caso batería. Etapa 2 = piezas agarrables
/// con ProtoboardConnector; Etapa 3 = victoria + cortar el sistema viejo.
///
/// Ejecutar: Tools → TITA → Reto 2 → Etapa 1 - Provisionar breadboard
///           (headless: -executeMethod Reto2ProtoboardSetup.SetupBatch)
/// </summary>
public static class Reto2ProtoboardSetup
{
    const string ScenePath = "Assets/Scenes/Explorador.unity";
    const string ZoneName  = "Reto2_Zone";
    const string BoardName = "Protoboard_Reto2";

    // Posición/tamaño local dentro de Reto2_Zone. AJUSTABLE en VR (el usuario calibra).
    static readonly Vector3 BoardLocalPos = new Vector3(0.65f, 0.10f, 0f);
    const float BoardYOffset = 0.03f;   // altura del grid de slots sobre el bareboard real
    // El tamaño de piezas y cables se ATA a la escala del Bareboard: escala-mundo de la pieza =
    // (escala promedio del Bareboard) × BoardSizeFactor. Así todo queda proporcional al bareboard que
    // calibraste; si reescalas el Bareboard y re-corres, las piezas se reajustan. SUBIR/BAJAR si hace falta.
    const float BoardSizeFactor = 8f;

    static bool EsDescendienteDe(Transform t, Transform ancestor)
    {
        for (var p = t; p != null; p = p.parent)
            if (p == ancestor) return true;
        return false;
    }
    const int   Cols       = 6;        // columnas (bareboard 6×4: 6 columnas × 4 filas VCC/COL/COL/GND)
    const float ColSpacing = 0.025f;   // separación entre columnas (m)
    const float RailZ      = 0.06f;    // z del riel VCC (+) y GND (−)
    const float TieZ       = 0.02f;    // z de las filas de amarre
    const float SlotTopY   = 0.012f;   // altura de los slots sobre la placa

    [MenuItem("Tools/TITA/Reto 2/Etapa 1 - Provisionar breadboard")]
    public static void SetupMenu()
    {
        int n = Setup();
        EditorUtility.DisplayDialog("Reto 2 — Etapa 1 Protoboard",
            n > 0 ? "Breadboard + fuente 9V provisionados en Reto2_Zone. Míralo/posiciónalo en VR y calíbralo."
                  : "No se pudo (¿falta Reto2_Zone?). Revisa la consola.", "OK");
    }

    public static void SetupBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int n = Setup();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(n > 0 ? "[Reto2ProtoboardSetup] Etapa 1 lista y guardada."
                        : "[Reto2ProtoboardSetup] FALLÓ: no se encontró Reto2_Zone.");
    }

    public static void SetupAllBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int n1 = Setup();
        int n2 = n1 > 0 ? Stage2() : 0;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Reto2ProtoboardSetup] SetupAll: board={n1}, tokens={n2}. Guardado.");
    }

    /// <summary>RECONSTRUCCIÓN COMPLETA del Reto 2 (grid 6×4): regenera board+slots+fuente sobre el
    /// bareboard, apaga el sistema viejo (cutover) y arma el circuito visible (R+LED ×2 + cables).
    /// Reemplaza por completo el diseño anterior del Reto 2. (Backup de la escena aparte.)</summary>
    public static void FullRebuildBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int board  = Setup();            // Etapa 1: board 6×4 + slots + fuente (destruye el board previo)
        int cut    = board > 0 ? Stage3() : 0;          // Etapa 3: apaga LEDs fijos + CircuitManager viejos
        int pieces = board > 0 ? PopulateCircuit() : 0; // Etapa 4: circuito visible + cables sobre los slots
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Reto2ProtoboardSetup] FULL REBUILD 6×4: board={board}, cutover={cut}, piezas={pieces}. Guardado.");
    }

    static int Setup()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t))
            .ToArray();

        Transform zone = all.FirstOrDefault(t => t.name == ZoneName);
        if (zone == null) { Debug.LogError("[Reto2ProtoboardSetup] No encontré Reto2_Zone."); return 0; }

        // Idempotente: borrar un board previo.
        foreach (var old in all.Where(t => t != null && t.name == BoardName).ToArray())
            Object.DestroyImmediate(old.gameObject);

        // Re-consultar tras destruir: las refs a hijos destruidos en 'all' lanzarían
        // MissingReferenceException al leer t.name más abajo.
        all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t))
            .ToArray();

        // ── Usar el BAREBOARD REAL del Reto 2 como visual (si existe bajo la zona) ──
        // El board root se coloca ENCIMA del bareboard; sus slots overlayean la placa real.
        Transform bareboard = all.FirstOrDefault(t => t != null && t.name == "Bareboard" && EsDescendienteDe(t, zone));
        Vector3 rootLocalPos = bareboard != null
            ? bareboard.localPosition + new Vector3(0f, BoardYOffset, 0f)
            : BoardLocalPos;

        // ── Board root (escala 1; los slots cuelgan de aquí con posiciones reales) ──
        var board = new GameObject(BoardName);
        Undo.RegisterCreatedObjectUndo(board, "Provisionar breadboard Reto 2");
        board.transform.SetParent(zone, false);
        board.transform.localPosition = rootLocalPos;
        board.transform.localRotation = Quaternion.identity;
        board.transform.localScale    = Vector3.one;

        // Cuerpo visual: usar el bareboard real; solo crear el cubo placeholder si NO hay bareboard.
        if (bareboard == null)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(board.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale    = new Vector3(Cols * ColSpacing + 0.06f, 0.02f, 0.16f);
        }

        var sim   = board.AddComponent<ProtoboardSimulator>();
        var slots = new List<ProtoboardSlot>();

        float x0 = -(Cols - 1) * ColSpacing * 0.5f;

        // ── Riel VCC (el PRIMER slot lleva el ElectricalNode que será el nodo del riel) ──
        ElectricalNode vccNode = null, gndNode = null;
        for (int c = 0; c < Cols; c++)
        {
            var s = CreateSlot(board.transform, "VCC", new Vector3(x0 + c * ColSpacing, SlotTopY, RailZ), 0, c, slots);
            if (c == 0) vccNode = s.gameObject.AddComponent<ElectricalNode>();
        }
        // ── Riel GND ──
        for (int c = 0; c < Cols; c++)
        {
            var s = CreateSlot(board.transform, "GND", new Vector3(x0 + c * ColSpacing, SlotTopY, -RailZ), 0, c, slots);
            if (c == 0) gndNode = s.gameObject.AddComponent<ElectricalNode>();
        }
        // ── Columnas de amarre (cada columna es su propio nodo: railId COL_c) ──
        for (int c = 0; c < Cols; c++)
        {
            CreateSlot(board.transform, $"COL_{c}", new Vector3(x0 + c * ColSpacing, SlotTopY,  TieZ), 1, c, slots);
            CreateSlot(board.transform, $"COL_{c}", new Vector3(x0 + c * ColSpacing, SlotTopY, -TieZ), 2, c, slots);
        }

        sim.todosLosSlots = slots;

        // ── Fuente 9V: nodeA=riel VCC, nodeB=riel GND (BuildNodeMap reutiliza esos nodos) ──
        var srcGO = new GameObject("Fuente_9V");
        srcGO.transform.SetParent(board.transform, false);
        srcGO.transform.localPosition = new Vector3(x0 - 0.05f, SlotTopY, 0f);
        var srcBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
        srcBody.name = "Visual";
        srcBody.transform.SetParent(srcGO.transform, false);
        srcBody.transform.localScale = new Vector3(0.025f, 0.02f, 0.05f);
        var src = srcGO.AddComponent<VoltageSource>();
        src.voltage = 9f;
        src.nodeA   = vccNode;
        src.nodeB   = gndNode;

        Debug.Log($"[Reto2ProtoboardSetup] Board '{BoardName}' bajo Reto2_Zone: {slots.Count} slots " +
                  $"(rieles VCC/GND + {Cols} columnas), fuente 9V VCC→GND. Pos local {BoardLocalPos}.");
        return 1;
    }

    static ProtoboardSlot CreateSlot(Transform parent, string railId, Vector3 localPos,
                                     int row, int col, List<ProtoboardSlot> slots)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = $"Slot_{railId}_{col}";
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = new Vector3(0.008f, 0.003f, 0.008f);

        // Slots como marcadores: sin collider (el conector usa posiciones, no física).
        var col0 = go.GetComponent<Collider>();
        if (col0 != null) Object.DestroyImmediate(col0);

        var slot = go.AddComponent<ProtoboardSlot>();
        slot.railId = railId;
        slot.row    = row;
        slot.col    = col;
        slots.Add(slot);
        return slot;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FIJAR RIELES — cablea las piezas EXISTENTES por railId, SIN MOVERLAS.
    //  Arregla piezas "en corto" (2 patas en el mismo riel) sin recolocarlas en VR.
    //  Topología: R1 VCC→COL_0, LED1 COL_0→GND, R2 VCC→COL_1, LED2 COL_1→GND (dañado=invertido).
    //  Con la batería desacoplada, cada rama enciende al cablear batería +→VCC y −→GND.
    // ═══════════════════════════════════════════════════════════════════════
    [MenuItem("Tools/TITA/Reto 2/Fijar rieles del circuito (sin mover piezas)")]
    public static void FijarRielesMenu()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        Transform board = all.FirstOrDefault(t => t.name == BoardName);
        if (board == null) { EditorUtility.DisplayDialog("Reto 2", "No encontré Protoboard_Reto2.", "OK"); return; }

        int n = 0;
        n += FijarPieza(all, "Circuit_R1",          "VCC",   "COL_0", false);
        n += FijarPieza(all, "Circuit_LED1_ok",     "COL_0", "GND",   false);
        n += FijarPieza(all, "Circuit_R2",          "VCC",   "COL_1", false);
        n += FijarPieza(all, "Circuit_LED2_damaged","COL_1", "GND",   true);   // dañado = invertido

        EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
        EditorUtility.DisplayDialog("Reto 2 — Fijar rieles",
            $"{n}/4 piezas cableadas por riel SIN moverlas:\n" +
            "R1 VCC→COL_0 · LED1 COL_0→GND · R2 VCC→COL_1 · LED2 COL_1→GND (dañado).\n\n" +
            "Ahora corre 'Cables físicos - Eléctrico' y en Play conecta batería +→VCC y −→GND.", "OK");
    }

    static int FijarPieza(Transform[] all, string nombre, string railA, string railB, bool damaged)
    {
        var t = all.FirstOrDefault(x => x != null && x.name == nombre);
        if (t == null) { Debug.LogWarning($"[FijarRieles] No encontré '{nombre}'."); return 0; }
        var comp = t.GetComponent<ElectricalComponent>() ?? t.GetComponentInChildren<ElectricalComponent>(true);
        if (comp == null) { Debug.LogWarning($"[FijarRieles] '{nombre}' sin ElectricalComponent."); return 0; }

        var conn = comp.GetComponent<ProtoboardConnector>();
        if (conn == null) conn = comp.gameObject.AddComponent<ProtoboardConnector>();
        conn.lockNodes = true;
        conn.lockRailA = railA;
        conn.lockRailB = railB;
        if (comp is LED led) led.polarityInverted = damaged;
        EditorUtility.SetDirty(conn);
        Debug.Log($"[FijarRieles] '{nombre}' → {railA}→{railB}" + (damaged ? " (dañado/invertido)" : ""));
        return 1;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ARMAR PUZZLE COMPLETO — RECOLOCA las piezas y arma un circuito 100% jumpers
    //  (estilo protoboard real): TODA conexión de potencia/entre-piezas es un cable.
    //    Rama 1: R1 COL_0→COL_1 ; LED1 COL_2→GND
    //            jumpers: VCC→COL_0 (pata1 R1) · COL_1→COL_2 (pata2 R1 → ánodo LED1)
    //    Rama 2: R2 COL_3→COL_4 ; LED2 COL_5→GND (DAÑADO)
    //            jumpers: VCC→COL_3 · COL_4→COL_5
    //  + batería +→VCC, −→GND ; y reemplazar el LED2 dañado. El "dañado" lo FUERZA
    //  el guard (Reto2CircuitGuard) en runtime (robusto a reseteos de polaridad).
    // ═══════════════════════════════════════════════════════════════════════
    [MenuItem("Tools/TITA/Reto 2/Armar puzzle completo (recolocar piezas)")]
    public static void ArmarPuzzleMenu()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        Transform board = all.FirstOrDefault(t => t.name == BoardName);
        if (board == null) { EditorUtility.DisplayDialog("Reto 2", "No encontré Protoboard_Reto2.", "OK"); return; }
        var slots = board.GetComponentsInChildren<ProtoboardSlot>(true);

        ProtoboardSlot Col(int c) => slots.FirstOrDefault(s => s != null && s.railId == $"COL_{c}");
        var gndSlots = slots.Where(s => s != null && s.railId == "GND").ToList();
        ProtoboardSlot Gnd(int i) => gndSlots.Count == 0 ? null : gndSlots[Mathf.Min(i, gndSlots.Count - 1)];

        // Topología que cabe en 4 columnas (COL_0..3, que sí existen tras tu reorganización).
        // Cada resistor comparte su columna de salida con el ánodo del LED (como en un protoboard real);
        // el jugador cablea VCC→pata1 de cada resistor y las 2 puntas de la batería a los rieles.
        int n = 0;
        n += Recolocar(all, "Circuit_R1",           Col(0), Col(1), "COL_0", "COL_1", false);
        n += Recolocar(all, "Circuit_LED1_ok",      Col(1), Gnd(0), "COL_1", "GND",   false);
        n += Recolocar(all, "Circuit_R2",           Col(2), Col(3), "COL_2", "COL_3", false);
        n += Recolocar(all, "Circuit_LED2_damaged", Col(3), Gnd(1), "COL_3", "GND",   true);

        EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
        EditorUtility.DisplayDialog("Reto 2 — Puzzle (cabe en COL_0..3)",
            $"{n}/4 piezas fijadas:\n" +
            "R1 COL_0→COL_1 · LED1 COL_1→GND · R2 COL_2→COL_3 · LED2 COL_3→GND (dañado).\n\n" +
            "Jugador (4 cables): batería +→VCC, −→GND ; VCC→COL_0 (pata1 R1) ; VCC→COL_2 (pata1 R2) ;\n" +
            "y reemplaza el LED2. (R y LED comparten columna = como protoboard real.)", "OK");
    }

    /// <summary>Reposiciona una pieza EXISTENTE al punto medio de 2 slots (visual) y la fija por riel
    /// (eléctrico). No la re-instancia — conserva la pieza, solo la mueve.</summary>
    static int Recolocar(Transform[] all, string nombre, ProtoboardSlot slotA, ProtoboardSlot slotB,
                         string railA, string railB, bool damaged)
    {
        var t = all.FirstOrDefault(x => x != null && x.name == nombre);
        if (t == null) { Debug.LogWarning($"[ArmarPuzzle] No encontré '{nombre}'."); return 0; }
        var comp = t.GetComponent<ElectricalComponent>() ?? t.GetComponentInChildren<ElectricalComponent>(true);
        if (comp == null) { Debug.LogWarning($"[ArmarPuzzle] '{nombre}' sin ElectricalComponent."); return 0; }

        var conn = comp.GetComponent<ProtoboardConnector>();
        if (conn == null) conn = comp.gameObject.AddComponent<ProtoboardConnector>();

        // ELÉCTRICO: fijar por riel SIEMPRE (por string, no depende del slot object → sin patas sueltas).
        conn.lockNodes = true; conn.lockRailA = railA; conn.lockRailB = railB;
        if (comp is LED led) led.polarityInverted = damaged;

        // VISUAL: reposicionar al punto medio de los 2 slots SOLO si ambos existen.
        if (slotA != null && slotB != null)
        {
            Vector3 pa = slotA.transform.position, pb = slotB.transform.position;
            t.position = (pa + pb) * 0.5f;   // sin rotar (evita distorsión bajo la escala de la zona)
            var la = conn.leadA != null ? conn.leadA : new GameObject("LeadA").transform;
            la.SetParent(comp.transform, true); la.position = pa; conn.leadA = la;
            var lb = conn.leadB != null ? conn.leadB : new GameObject("LeadB").transform;
            lb.SetParent(comp.transform, true); lb.position = pb; conn.leadB = lb;
        }
        else Debug.LogWarning($"[ArmarPuzzle] '{nombre}' fijado a {railA}→{railB} pero faltan slots para reposicionar (ajústalo a mano).");

        EditorUtility.SetDirty(conn); EditorUtility.SetDirty(comp); EditorUtility.SetDirty(t);
        Debug.Log($"[ArmarPuzzle] '{nombre}' → {railA}→{railB}" + (damaged ? " (dañado)" : ""));
        return 1;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PUZZLE B — cable EXPLÍCITO entre resistor y LED, SIN mover las piezas.
    //  Solo cambia los locks (por riel) + agrega la columna COL_5 que falta para
    //  el ánodo del LED2. Cada rama con hueco R→LED que el jugador cierra con cable:
    //    Rama 1: R1 COL_0→COL_1 · LED1 COL_2→GND   (cable COL_1→COL_2)
    //    Rama 2: R2 COL_3→COL_4 · LED2 COL_5→GND   (cable COL_4→COL_5)  [dañado]
    // ═══════════════════════════════════════════════════════════════════════
    [MenuItem("Tools/TITA/Reto 2/Armar puzzle B (cable R→LED, sin mover piezas)")]
    public static void ArmarPuzzleBMenu()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        Transform board = all.FirstOrDefault(t => t.name == BoardName);
        if (board == null) { EditorUtility.DisplayDialog("Reto 2", "No encontré Protoboard_Reto2.", "OK"); return; }
        var sim = board.GetComponent<ProtoboardSimulator>() ?? board.GetComponentInChildren<ProtoboardSimulator>(true);
        if (sim == null) { EditorUtility.DisplayDialog("Reto 2", "El board no tiene ProtoboardSimulator.", "OK"); return; }

        // 1. Asegurar la columna COL_5 (ánodo del LED2). Si falta, clonar una columna existente.
        if (!sim.todosLosSlots.Any(s => s != null && s.railId == "COL_5"))
        {
            var nueva = CrearColumnaExtra(sim, "COL_5", "COL_4", "COL_3");
            if (nueva == null) { EditorUtility.DisplayDialog("Reto 2", "No pude crear COL_5 (falta COL_4 para clonar).", "OK"); return; }
        }

        // 2. Fijar por riel con HUECO R→LED (sin mover piezas: FijarPieza solo cambia los locks).
        all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        int n = 0;
        n += FijarPieza(all, "Circuit_R1",           "COL_0", "COL_1", false);
        n += FijarPieza(all, "Circuit_LED1_ok",      "COL_2", "GND",   false);
        n += FijarPieza(all, "Circuit_R2",           "COL_3", "COL_4", false);
        n += FijarPieza(all, "Circuit_LED2_damaged", "COL_5", "GND",   true);

        EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
        EditorUtility.DisplayDialog("Reto 2 — Puzzle B (cable R→LED)",
            $"{n}/4 piezas fijadas (sin moverlas):\n" +
            "R1 COL_0→COL_1 · LED1 COL_2→GND · R2 COL_3→COL_4 · LED2 COL_5→GND (dañado).\n\n" +
            "Jugador (6 cables): batería +→VCC, −→GND ; VCC→COL_0 ; COL_1→COL_2 (R1→LED1) ;\n" +
            "VCC→COL_3 ; COL_4→COL_5 (R2→LED2) ; y reemplaza el LED2.", "OK");
    }

    /// <summary>Crea una columna extra clonando una existente (para tener el nodo COL_5). No mueve
    /// componentes. La nueva columna se coloca al lado de la clonada (misma separación entre columnas).</summary>
    static ProtoboardSlot CrearColumnaExtra(ProtoboardSimulator sim, string nuevoRail, string clonarDe, string refPrev)
    {
        var modelo = sim.todosLosSlots.FirstOrDefault(s => s != null && s.railId == clonarDe);
        if (modelo == null) return null;
        var prev = sim.todosLosSlots.FirstOrDefault(s => s != null && s.railId == refPrev);
        Vector3 offset = prev != null ? (modelo.transform.position - prev.transform.position) : modelo.transform.right * 0.03f;

        var go = Object.Instantiate(modelo.gameObject, modelo.transform.parent);
        go.name = $"Slot_{nuevoRail}";
        go.transform.position = modelo.transform.position + offset;
        var slot = go.GetComponent<ProtoboardSlot>();
        slot.railId = nuevoRail;
        slot.assignedNode = null;                          // BuildNodeMap le asignará un nodo propio
        var node = go.GetComponent<ElectricalNode>();
        if (node != null) Object.DestroyImmediate(node);   // que no herede el nodo del clon
        sim.todosLosSlots.Add(slot);
        EditorUtility.SetDirty(sim);
        Debug.Log($"[ArmarPuzzleB] Columna '{nuevoRail}' creada (clon de {clonarDe}) — sin tocar componentes.");
        return slot;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  REORDENAR SLOTS — asigna Row/Col a un grid limpio SEGÚN LA POSICIÓN FÍSICA
    //  actual. NO mueve nada, NO cambia railId ni diseño. Solo los índices Row/Col
    //  del Inspector (organización): Row 0=VCC (arriba), 1=COL (medio), 2=GND (abajo);
    //  Col 0,1,2… de izquierda a derecha por la X local.
    // ═══════════════════════════════════════════════════════════════════════
    [MenuItem("Tools/TITA/Reto 2/Reordenar slots a grid limpio (solo row/col)")]
    public static void ReordenarSlotsMenu()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        Transform board = all.FirstOrDefault(t => t.name == BoardName);
        if (board == null) { EditorUtility.DisplayDialog("Reto 2", "No encontré Protoboard_Reto2.", "OK"); return; }

        var slots = board.GetComponentsInChildren<ProtoboardSlot>(true).Where(s => s != null).ToArray();
        if (slots.Length == 0) { EditorUtility.DisplayDialog("Reto 2", "El board no tiene ProtoboardSlots.", "OK"); return; }

        // Clusterizar por X LOCAL (relativa al board) → columnas de izquierda a derecha.
        float Xl(ProtoboardSlot s) => board.InverseTransformPoint(s.transform.position).x;
        const float tol = 0.012f;   // ~media separación entre columnas
        var centros = new System.Collections.Generic.List<float>();
        foreach (var x in slots.Select(Xl).OrderBy(v => v))
            if (centros.Count == 0 || x - centros[centros.Count - 1] > tol) centros.Add(x);

        int ColDe(float x)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < centros.Count; i++) { float d = Mathf.Abs(x - centros[i]); if (d < bd) { bd = d; best = i; } }
            return best;
        }

        foreach (var s in slots)
        {
            int row = s.railId == "VCC" ? 0 : (s.railId == "GND" ? 2 : 1);   // VCC arriba, COL medio, GND abajo
            int col = ColDe(Xl(s));
            s.row = row;                 // SOLO row/col (no toco railId ni transform)
            s.col = col;
            EditorUtility.SetDirty(s);
        }

        EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
        EditorUtility.DisplayDialog("Reto 2 — Reordenar slots",
            $"{slots.Length} slots re-etiquetados a grid limpio (Row/Col), sin mover nada:\n" +
            $"• {centros.Count} columnas detectadas (Col 0..{centros.Count - 1}).\n" +
            "• Row 0=VCC · 1=COL · 2=GND.\n\n" +
            "(Solo cambió Row/Col en el Inspector; posición, railId y diseño intactos.)", "OK");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  REASIGNAR railId — deriva railId + Row/Col de la POSICIÓN física de cada
    //  slot, sin moverlos. Fila superior (Z máx) = VCC ; fila inferior (Z mín) = GND ;
    //  filas del medio = COL_0..COL_n (una por columna). Arregla los railId
    //  inconsistentes (COL_0×1, COL_2×1…) a un grid limpio. NO cambia posición/diseño.
    // ═══════════════════════════════════════════════════════════════════════
    [MenuItem("Tools/TITA/Reto 2/Reasignar railId a grid limpio (por posición)")]
    public static void ReasignarRailIdMenu()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        Transform board = all.FirstOrDefault(t => t.name == BoardName);
        if (board == null) { EditorUtility.DisplayDialog("Reto 2", "No encontré Protoboard_Reto2.", "OK"); return; }
        var slots = board.GetComponentsInChildren<ProtoboardSlot>(true).Where(s => s != null).ToArray();
        if (slots.Length == 0) { EditorUtility.DisplayDialog("Reto 2", "El board no tiene ProtoboardSlots.", "OK"); return; }

        Vector3 L(ProtoboardSlot s) => board.InverseTransformPoint(s.transform.position);

        // Filas por Z local (banda superior = VCC, inferior = GND, resto = COL).
        const float zTol = 0.015f;
        var zC = new System.Collections.Generic.List<float>();
        foreach (var z in slots.Select(s => L(s).z).OrderBy(v => v))
            if (zC.Count == 0 || z - zC[zC.Count - 1] > zTol) zC.Add(z);
        float zTop = zC[zC.Count - 1], zBot = zC[0];

        // Columnas por X local.
        const float xTol = 0.012f;
        var xC = new System.Collections.Generic.List<float>();
        foreach (var x in slots.Select(s => L(s).x).OrderBy(v => v))
            if (xC.Count == 0 || x - xC[xC.Count - 1] > xTol) xC.Add(x);
        int ColDe(float x) { int b = 0; float bd = float.MaxValue; for (int i = 0; i < xC.Count; i++) { float d = Mathf.Abs(x - xC[i]); if (d < bd) { bd = d; b = i; } } return b; }

        int nV = 0, nG = 0, nCol = 0;
        foreach (var s in slots)
        {
            var lp = L(s);
            int col = ColDe(lp.x);
            string rail; int row;
            if (Mathf.Abs(lp.z - zTop) <= zTol) { rail = "VCC"; row = 0; nV++; }
            else if (Mathf.Abs(lp.z - zBot) <= zTol) { rail = "GND"; row = 2; nG++; }
            else { rail = $"COL_{col}"; row = 1; nCol++; }

            s.railId = rail; s.row = row; s.col = col; s.assignedNode = null;
            var node = s.GetComponent<ElectricalNode>();
            if (node != null) Object.DestroyImmediate(node);   // BuildNodeMap recrea 1 nodo por railId
            EditorUtility.SetDirty(s);
        }

        EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
        EditorUtility.DisplayDialog("Reto 2 — Reasignar railId",
            $"{slots.Length} slots a grid limpio (sin mover nada):\n" +
            $"• {nV} VCC (arriba) · {nCol} COL (medio, {xC.Count} columnas) · {nG} GND (abajo).\n\n" +
            "railId + Row/Col corregidos por posición. Ahora corre 'Armar puzzle B'.", "OK");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  REASIGNAR railId POR COLUMNAS — layout ESPEJO F1-F6 del BreadBoard_low:
    //  Rama 1 = VCC,COL,GND · Rama 2 = GND,COL,VCC → patrón [VCC,COL,GND,GND,COL,VCC]:
    //  VCC en los EXTREMOS, GND en el CENTRO, y las 2 columnas de en medio = COL_0/COL_1.
    //  Deriva de la X local; NO mueve nada. Row = índice vertical en la columna,
    //  Col = índice de columna. VCC (ambos lados) = 1 nodo ; GND (centro) = 1 nodo.
    // ═══════════════════════════════════════════════════════════════════════
    [MenuItem("Tools/TITA/Reto 2/Reasignar railId por COLUMNAS (espejo VCC/COL/GND)")]
    public static void ReasignarRailIdColumnasMenu()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        Transform board = all.FirstOrDefault(t => t.name == BoardName);
        if (board == null) { EditorUtility.DisplayDialog("Reto 2", "No encontré Protoboard_Reto2.", "OK"); return; }
        var slots = board.GetComponentsInChildren<ProtoboardSlot>(true).Where(s => s != null).ToArray();
        if (slots.Length == 0) { EditorUtility.DisplayDialog("Reto 2", "El board no tiene ProtoboardSlots.", "OK"); return; }

        Vector3 L(ProtoboardSlot s) => board.InverseTransformPoint(s.transform.position);

        // Columnas por X local.
        const float xTol = 0.012f;
        var xC = new System.Collections.Generic.List<float>();
        foreach (var x in slots.Select(s => L(s).x).OrderBy(v => v))
            if (xC.Count == 0 || x - xC[xC.Count - 1] > xTol) xC.Add(x);
        int total = xC.Count;
        int ColDe(float x) { int b = 0; float bd = float.MaxValue; for (int i = 0; i < total; i++) { float d = Mathf.Abs(x - xC[i]); if (d < bd) { bd = d; b = i; } } return b; }

        // railId por columna: layout ESPEJO. Rama 1 = VCC,COL,GND · Rama 2 = GND,COL,VCC.
        // → extremos = VCC ; par central = GND ; las 2 de en medio (una por lado) = COL_0, COL_1.
        var railPorCol = new string[total];
        int cL = (total - 1) / 2, cR = total / 2;   // par central = GND
        for (int c = 0; c < total; c++)
            railPorCol[c] = (c == 0 || c == total - 1) ? "VCC" : (c == cL || c == cR) ? "GND" : null;
        int colN = 0;
        for (int c = 0; c < total; c++) if (railPorCol[c] == null) railPorCol[c] = $"COL_{colN++}";

        // Asignar railId + col; row = índice vertical dentro de la columna.
        int nV = 0, nG = 0, nCol = 0;
        for (int c = 0; c < total; c++)
        {
            var enCol = slots.Where(s => ColDe(L(s).x) == c).OrderByDescending(s => L(s).z).ToList();
            for (int r = 0; r < enCol.Count; r++)
            {
                var s = enCol[r];
                s.railId = railPorCol[c]; s.col = c; s.row = r; s.assignedNode = null;
                var node = s.GetComponent<ElectricalNode>();
                if (node != null) Object.DestroyImmediate(node);
                EditorUtility.SetDirty(s);
            }
            if (railPorCol[c] == "VCC") nV += enCol.Count; else if (railPorCol[c] == "GND") nG += enCol.Count; else nCol += enCol.Count;
        }

        EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
        EditorUtility.DisplayDialog("Reto 2 — railId por columnas (VCC→COL→GND)",
            $"{total} columnas → {colN} ramas: {nV} slots VCC · {nCol} COL (COL_0..COL_{colN - 1}) · {nG} GND. Sin mover nada.\n\n" +
            $"Diseño SIMPLE ({colN} ramas): en cada rama R va VCC→COL_n y el LED COL_n→GND.\n" +
            "Ahora corre 'Fijar rieles del circuito (sin mover piezas)'.", "OK");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PUZZLE 4 CABLES — divide cada columna COL en 2 mitades (A=arriba, B=abajo)
    //  → hace falta un cable para unirlas, así el jugador SÍ cablea R→LED. SIN mover
    //  piezas (solo railId de los slots + locks). Topología:
    //    R1 VCC→COL_0A · LED1 COL_0B→GND   (cable COL_0A→COL_0B)
    //    R2 VCC→COL_1A · LED2 COL_1B→GND   (cable COL_1A→COL_1B)  [dañado]
    //  → 4 cables: batería +→VCC, −→GND, COL_0A→COL_0B, COL_1A→COL_1B + reemplazar LED2.
    // ═══════════════════════════════════════════════════════════════════════
    [MenuItem("Tools/TITA/Reto 2/Armar puzzle 4 cables (cable R→LED, split COL)")]
    public static void ArmarPuzzle4CablesMenu()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        Transform board = all.FirstOrDefault(t => t.name == BoardName);
        if (board == null) { EditorUtility.DisplayDialog("Reto 2", "No encontré Protoboard_Reto2.", "OK"); return; }
        var slots = board.GetComponentsInChildren<ProtoboardSlot>(true).Where(s => s != null).ToArray();
        Vector3 L(ProtoboardSlot s) => board.InverseTransformPoint(s.transform.position);

        // Dividir cada COL_n (que aún no sea A/B) en mitad superior (A) e inferior (B) por Z.
        int splits = 0;
        var grupos = slots.Where(s => s.railId != null && s.railId.StartsWith("COL_")
                                      && !s.railId.EndsWith("A") && !s.railId.EndsWith("B"))
                          .GroupBy(s => s.railId).ToList();
        foreach (var g in grupos)
        {
            var ordenados = g.OrderByDescending(s => L(s).z).ToList();   // arriba primero
            int mitad = Mathf.Max(1, ordenados.Count / 2);
            for (int i = 0; i < ordenados.Count; i++)
            {
                ordenados[i].railId = g.Key + (i < mitad ? "A" : "B");
                ordenados[i].assignedNode = null;
                var node = ordenados[i].GetComponent<ElectricalNode>();
                if (node != null) Object.DestroyImmediate(node);
                EditorUtility.SetDirty(ordenados[i]);
            }
            splits++;
        }

        // Lock con HUECO A↔B (cable obligatorio entre R y LED).
        all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        int n = 0;
        n += FijarPieza(all, "Circuit_R1",           "VCC",    "COL_0A", false);
        n += FijarPieza(all, "Circuit_LED1_ok",      "COL_0B", "GND",    false);
        n += FijarPieza(all, "Circuit_R2",           "VCC",    "COL_1A", false);
        n += FijarPieza(all, "Circuit_LED2_damaged", "COL_1B", "GND",    true);

        EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
        EditorUtility.DisplayDialog("Reto 2 — Puzzle 4 cables",
            $"{splits} columnas COL divididas en A/B · {n}/4 piezas fijadas:\n" +
            "R1 VCC→COL_0A · LED1 COL_0B→GND · R2 VCC→COL_1A · LED2 COL_1B→GND (dañado).\n\n" +
            "4 cables: batería +→VCC, −→GND, COL_0A→COL_0B (R1→LED1), COL_1A→COL_1B (R2→LED2) + reemplazar LED2.", "OK");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PUZZLE REALISTA (6 cables) — parte cada columna COL en 3 nodos:
    //    A = entrada del resistor · B = salida del resistor · C = entrada del LED.
    //  Así el jugador cablea VCC→R (pata1) y R→LED (pata2), como protoboard real.
    //  SIN mover piezas (solo railId de slots + locks). Topología por rama:
    //    R  COL_nA→COL_nB   ·   LED  COL_nC→GND
    //  6 cables: batería +→VCC, −→GND, VCC→COL_0A, COL_0B→COL_0C, VCC→COL_1A, COL_1B→COL_1C
    //  + reemplazar el LED2 dañado.
    // ═══════════════════════════════════════════════════════════════════════
    [MenuItem("Tools/TITA/Reto 2/Armar puzzle REALISTA (6 cables: VCC→R→LED)")]
    public static void ArmarPuzzleRealistaMenu()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        Transform board = all.FirstOrDefault(t => t.name == BoardName);
        if (board == null) { EditorUtility.DisplayDialog("Reto 2", "No encontré Protoboard_Reto2.", "OK"); return; }
        var slots = board.GetComponentsInChildren<ProtoboardSlot>(true).Where(s => s != null).ToArray();
        Vector3 L(ProtoboardSlot s) => board.InverseTransformPoint(s.transform.position);

        // Nombre base de una columna (quita el sufijo A/B/C si ya estaba partida).
        string Base(string r)
        {
            if (r == null || !r.StartsWith("COL_")) return r;
            char last = r[r.Length - 1];
            return (last == 'A' || last == 'B' || last == 'C') ? r.Substring(0, r.Length - 1) : r;
        }

        // Partir cada columna COL en 3 nodos (A=arriba, B=medio, C=abajo) por Z.
        int splits = 0;
        var grupos = slots.Where(s => s.railId != null && s.railId.StartsWith("COL_")).GroupBy(s => Base(s.railId)).ToList();
        foreach (var g in grupos)
        {
            var ord = g.OrderByDescending(s => L(s).z).ToList();
            int cnt = ord.Count;
            for (int i = 0; i < cnt; i++)
            {
                int grp = i * 3 / cnt;   // 0→A, 1→B, 2→C
                ord[i].railId = g.Key + (grp == 0 ? "A" : grp == 1 ? "B" : "C");
                ord[i].assignedNode = null;
                var node = ord[i].GetComponent<ElectricalNode>();
                if (node != null) Object.DestroyImmediate(node);
                EditorUtility.SetDirty(ord[i]);
            }
            splits++;
        }

        // Locks: R = COL_nA→COL_nB (patas en A y B) · LED = COL_nC→GND.
        all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        int m = 0;
        m += FijarPieza(all, "Circuit_R1",           "COL_0A", "COL_0B", false);
        m += FijarPieza(all, "Circuit_LED1_ok",      "COL_0C", "GND",    false);
        m += FijarPieza(all, "Circuit_R2",           "COL_1A", "COL_1B", false);
        m += FijarPieza(all, "Circuit_LED2_damaged", "COL_1C", "GND",    true);

        EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
        EditorUtility.DisplayDialog("Reto 2 — Puzzle realista (6 cables)",
            $"{splits} columnas COL partidas en A/B/C · {m}/4 piezas fijadas:\n" +
            "R1 COL_0A→COL_0B · LED1 COL_0C→GND · R2 COL_1A→COL_1B · LED2 COL_1C→GND (dañado).\n\n" +
            "6 cables: batería +→VCC, −→GND ; VCC→COL_0A ; COL_0B→COL_0C ; VCC→COL_1A ; COL_1B→COL_1C\n" +
            "+ reemplazar LED2.", "OK");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  COLOREAR SLOTS — guía visual para cablear: cada tipo de slot un color.
    //  VCC=rojo · GND=negro · COL_xA=azul · COL_xB=amarillo · COL_xC=verde.
    //  Así el jugador ve de un vistazo dónde va cada cable. No cambia nada eléctrico.
    // ═══════════════════════════════════════════════════════════════════════
    [MenuItem("Tools/TITA/Reto 2/Colorear slots (guía visual de cableado)")]
    public static void ColorearSlotsMenu()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        Transform board = all.FirstOrDefault(t => t.name == BoardName);
        if (board == null) { EditorUtility.DisplayDialog("Reto 2", "No encontré Protoboard_Reto2.", "OK"); return; }

        int n = 0;
        foreach (var slot in board.GetComponentsInChildren<ProtoboardSlot>(true))
        {
            if (slot == null) continue;
            var r = slot.GetComponent<Renderer>() ?? slot.GetComponentInChildren<Renderer>(true);
            if (r == null) continue;
            r.sharedMaterial = MatGuia(ColorDeRiel(slot.railId), NombreColor(slot.railId));
            EditorUtility.SetDirty(r);
            n++;
        }
        EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
        EditorUtility.DisplayDialog("Reto 2 — Colorear slots",
            $"{n} slots coloreados:\nVCC=ROJO · GND=NEGRO · COL_xA=AZUL · COL_xB=AMARILLO · COL_xC=VERDE.\n\n" +
            "Cablea: ROJO→AZUL, AMARILLO→VERDE, y batería a ROJO/NEGRO.", "OK");
    }

    static Color ColorDeRiel(string r)
    {
        if (string.IsNullOrEmpty(r)) return Color.grey;
        if (r == "VCC") return new Color(0.9f, 0.15f, 0.12f);   // rojo
        if (r == "GND") return new Color(0.10f, 0.10f, 0.10f);  // negro
        if (r.EndsWith("A")) return new Color(0.20f, 0.45f, 1f); // azul
        if (r.EndsWith("B")) return new Color(1f, 0.82f, 0.15f); // amarillo
        if (r.EndsWith("C")) return new Color(0.25f, 1f, 0.40f); // verde
        return Color.grey;
    }

    static string NombreColor(string r)
    {
        if (r == "VCC") return "VCC_rojo"; if (r == "GND") return "GND_negro";
        if (r != null && r.EndsWith("A")) return "A_azul";
        if (r != null && r.EndsWith("B")) return "B_amarillo";
        if (r != null && r.EndsWith("C")) return "C_verde";
        return "gris";
    }

    static Material MatGuia(Color c, string name)
    {
        string path = $"Assets/Materials/SlotGuia_{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        m = new Material(sh) { name = "SlotGuia_" + name };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.3f);
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ETAPA 2 — piezas agarrables (LED + resistencia) con ProtoboardConnector
    // ═══════════════════════════════════════════════════════════════════════
    const string ResPrefabPath = "Assets/Prefabs/Delivered/Delivered_Resistor.prefab";
    const string LedPrefabPath = "Assets/Prefabs/Delivered/Delivered_LED.prefab";

    [MenuItem("Tools/TITA/Reto 2/Etapa 2 - Piezas agarrables")]
    public static void Stage2Menu()
    {
        int n = Stage2();
        EditorUtility.DisplayDialog("Reto 2 — Etapa 2",
            n > 0 ? "LED + resistencia agarrables colocados junto al breadboard. Pruébalos en VR: agarra y encájalos entre los rieles."
                  : "No se pudo (¿corriste la Etapa 1?). Revisa la consola.", "OK");
    }

    public static void Stage2Batch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int n = Stage2();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(n > 0 ? "[Reto2ProtoboardSetup] Etapa 2 lista y guardada."
                        : "[Reto2ProtoboardSetup] Etapa 2 FALLÓ (¿falta el board de la Etapa 1?).");
    }

    static int Stage2()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t))
            .ToArray();

        Transform board = all.FirstOrDefault(t => t.name == BoardName);
        if (board == null) { Debug.LogError("[Reto2ProtoboardSetup] Falta Protoboard_Reto2 (corre la Etapa 1 primero)."); return 0; }

        // Idempotente: borrar tokens previos.
        foreach (var t in all.Where(t => t.name.StartsWith("Token_")).ToArray())
            Object.DestroyImmediate(t.gameObject);

        var resPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ResPrefabPath);
        var ledPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LedPrefabPath);
        if (resPrefab == null || ledPrefab == null)
        { Debug.LogError("[Reto2ProtoboardSetup] No cargué los prefabs Delivered_Resistor/LED."); return 0; }

        // Reposan JUNTO al board (un poco arriba, para no auto-engancharse hasta que el jugador
        // los coloque). El jugador los agarra y bridgea: resistencia VCC→columna, LED columna→GND.
        var res = (GameObject)PrefabUtility.InstantiatePrefab(resPrefab, board);
        res.name = "Token_Resistor";
        res.transform.localPosition = new Vector3(-0.04f, 0.06f, 0.10f);
        ConfigToken(res, 470f);

        var led = (GameObject)PrefabUtility.InstantiatePrefab(ledPrefab, board);
        led.name = "Token_LED";
        led.transform.localPosition = new Vector3(0.04f, 0.06f, 0.10f);
        ConfigToken(led, 0f);

        Debug.Log("[Reto2ProtoboardSetup] Etapa 2: Token_Resistor (470Ω) + Token_LED con ProtoboardConnector, " +
                  "reposando junto al breadboard. El modo protoboard (span de patas) se activa en Reto 2.");
        return 2;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ETAPA 3 — cutover: apagar el sistema VIEJO del Reto 2 (piezas fijas +
    //  CircuitManager). La victoria pasa al ProtoboardSimulator (MNA). Reversible
    //  (desactiva, no borra). CORRER CON UNITY CERRADO.
    // ═══════════════════════════════════════════════════════════════════════
    static readonly string[] OldFixedLeds = { "LED_RamaA", "LED_RamaB_Faulty" };

    // Prefijos de la basura del diseño VIEJO del Reto 2 (cables, nodos, wires, batería, pcb...).
    static readonly string[] OldClutterPrefixes =
        { "w2_", "NP_R2_", "DW_", "Node_R2_", "PCB_Board", "Transistor_Junction",
          "LED_RamaA", "LED_RamaB_Faulty", "Battery_9V" };

    [MenuItem("Tools/TITA/Reto 2/Limpiar basura vieja de Reto2_Zone")]
    public static void CleanupMenu()
    {
        int n = CleanupOldReto2();
        EditorUtility.DisplayDialog("Reto 2 — Limpieza",
            $"Borrados {n} objetos viejos de Reto2_Zone. Queda el bareboard + protoboard nuevo + HUD. " +
            "(Hay backup de la escena por si acaso.)", "OK");
    }

    public static void CleanupBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int n = CleanupOldReto2();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Reto2ProtoboardSetup] Limpieza: {n} objetos viejos borrados de Reto2_Zone. Guardado.");
    }

    /// <summary>Borra los GameObjects hijos DIRECTOS de Reto2_Zone del diseño viejo (por nombre).
    /// No toca el Bareboard, el Protoboard_Reto2 ni ZoneHUD_Reto2. Reversible vía backup.</summary>
    static int CleanupOldReto2()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        Transform zone = all.FirstOrDefault(t => t.name == ZoneName);
        if (zone == null) { Debug.LogError("[Cleanup] No encontré Reto2_Zone."); return 0; }

        var toDel = new List<GameObject>();
        foreach (Transform child in zone)
        {
            string nm = child.name;
            if (OldClutterPrefixes.Any(pre => nm.StartsWith(pre)))
                toDel.Add(child.gameObject);
        }
        int n = 0;
        foreach (var g in toDel)
            if (g != null) { Debug.Log($"[Cleanup] borrado: {g.name}"); Object.DestroyImmediate(g); n++; }

        Debug.Log($"[Cleanup] {n} objetos viejos borrados de Reto2_Zone.");
        return n;
    }

    [MenuItem("Tools/TITA/Reto 2/Etapa 3 - Cutover (apagar sistema viejo)")]
    public static void Stage3Menu()
    {
        int n = Stage3();
        EditorUtility.DisplayDialog("Reto 2 — Etapa 3 Cutover",
            $"Apagados {n} elementos del sistema viejo (LEDs fijos + CircuitManager). " +
            "La victoria ahora es por el protoboard (LED encendido correcto donde sea).", "OK");
    }

    public static void Stage3Batch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int n = Stage3();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Reto2ProtoboardSetup] Etapa 3 cutover: {n} elementos viejos apagados. Guardado.");
    }

    static int Stage3()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t))
            .ToArray();

        Transform zone = all.FirstOrDefault(t => t.name == ZoneName);
        if (zone == null) { Debug.LogError("[Reto2ProtoboardSetup] No encontré Reto2_Zone."); return 0; }

        Transform board = all.FirstOrDefault(t => t.name == BoardName);
        int n = 0;

        // 1) LEDs fijos viejos → desactivar (quedan fuera del scan de victoria scene-wide).
        foreach (var t in all)
        {
            if (t == null || !EsDescendienteDe(t, zone)) continue;
            if (OldFixedLeds.Contains(t.name) && t.gameObject.activeSelf)
            { t.gameObject.SetActive(false); n++; Debug.Log($"[Cutover] LED fijo viejo apagado: {t.name}"); }
        }

        // 2) CircuitManager viejo del Reto 2 (motor de piezas fijas) → deshabilitar componente.
        foreach (var cm in Resources.FindObjectsOfTypeAll<CircuitManager>())
        {
            if (cm == null || EditorUtility.IsPersistent(cm)) continue;
            if (!EsDescendienteDe(cm.transform, zone)) continue;
            if (board != null && EsDescendienteDe(cm.transform, board)) continue; // no tocar el del protoboard
            if (cm.enabled) { cm.enabled = false; n++; Debug.Log($"[Cutover] CircuitManager viejo deshabilitado en '{cm.name}'."); }
        }

        // 3) Fuente vieja (VoltageSource fija del Reto 2) → desactivar. El protoboard usa su Fuente_9V.
        foreach (var src in Resources.FindObjectsOfTypeAll<VoltageSource>())
        {
            if (src == null || EditorUtility.IsPersistent(src)) continue;
            if (!EsDescendienteDe(src.transform, zone)) continue;
            if (board != null && EsDescendienteDe(src.transform, board)) continue; // conservar la del protoboard
            if (src.gameObject.activeSelf) { src.gameObject.SetActive(false); n++; Debug.Log($"[Cutover] Fuente vieja apagada: {src.name}"); }
        }

        Debug.Log($"[Reto2ProtoboardSetup] Cutover: {n} elementos viejos apagados. Victoria = protoboard (MNA).");
        return n;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ETAPA 4 — pre-armar el circuito en el protoboard (2 ramas paralelas):
    //  R + LED por rama; LED1 correcto (enciende), LED2 invertido (dañado → el
    //  jugador lo gira 180° para corregir). Se coloca sobre los slots calibrados.
    // ═══════════════════════════════════════════════════════════════════════

    [MenuItem("Tools/TITA/Reto 2/Etapa 3+4 - Cutover + armar circuito")]
    public static void CutoverAndPopulateMenu()
    {
        int c = Stage3();
        int p = PopulateCircuit();
        EditorUtility.DisplayDialog("Reto 2 — Cutover + Circuito",
            $"Sistema viejo apagado ({c}). Circuito armado ({p} piezas): 2 ramas R+LED, " +
            "LED1 OK, LED2 invertido (gíralo para corregir).", "OK");
    }

    public static void CutoverAndPopulateBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int c = Stage3();
        int p = PopulateCircuit();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Reto2ProtoboardSetup] Cutover({c}) + Circuito({p} piezas) armado y guardado.");
    }

    static int PopulateCircuit()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        Transform board = all.FirstOrDefault(t => t.name == BoardName);
        if (board == null) { Debug.LogError("[PopulateCircuit] Falta Protoboard_Reto2."); return 0; }

        var slots = board.GetComponentsInChildren<ProtoboardSlot>(true).ToList();
        ProtoboardSlot Vcc(int i) => slots.Where(s => s.railId == "VCC").ElementAtOrDefault(i);
        ProtoboardSlot Gnd(int i) => slots.Where(s => s.railId == "GND").ElementAtOrDefault(i);
        ProtoboardSlot Col(int c) => slots.FirstOrDefault(s => s.railId == $"COL_{c}");

        var vcc0 = Vcc(0); var vcc1 = Vcc(1); var gnd0 = Gnd(0); var gnd1 = Gnd(1);
        var col0 = Col(0); var col1 = Col(1); var col2 = Col(2);
        if (new[] { vcc0, vcc1, gnd0, gnd1, col0, col1, col2 }.Any(s => s == null))
        { Debug.LogError("[PopulateCircuit] Faltan slots VCC/GND/COL_0/COL_1/COL_2 (railId). Revisa el board."); return 0; }

        // Limpiar tokens sueltos, circuito y cables previos. Se seleccionan los GameObjects y se
        // guarda != null en el loop: destruir un padre puede destruir un hijo aún en la lista, y
        // al llegar a él lanzaría MissingReferenceException.
        var toDestroy = all
            .Where(t => t != null &&
                   (t.name.StartsWith("Token_") || t.name.StartsWith("Circuit_") || t.name.StartsWith("Cable_")))
            .Select(t => t.gameObject).Distinct().ToArray();
        foreach (var g in toDestroy)
            if (g != null) Object.DestroyImmediate(g);

        var resPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ResPrefabPath);
        var ledPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LedPrefabPath);
        if (resPrefab == null || ledPrefab == null) { Debug.LogError("[PopulateCircuit] Faltan prefabs."); return 0; }

        // Tamaño de piezas/cables ATADO a la escala del Bareboard (lo que calibraste). Búsqueda robusta
        // (el prefab-instance a veces no lo pilla Transform.Find): buscamos en 'all' descendiente de la zona.
        Transform bareboard = all.FirstOrDefault(t => t != null && t.name == "Bareboard"
            && board.parent != null && EsDescendienteDe(t, board.parent));
        float boardScale = 1f;
        if (bareboard != null)
        {
            Vector3 bl = bareboard.lossyScale;
            boardScale = (Mathf.Abs(bl.x) + Mathf.Abs(bl.y) + Mathf.Abs(bl.z)) / 3f;
            Debug.Log($"[PopulateCircuit] Bareboard encontrado, lossyScale={bl}");
        }
        else Debug.LogWarning("[PopulateCircuit] No encontré el Bareboard bajo la zona — uso escala 1.");
        float pieceWorld = Mathf.Max(boardScale, 1e-4f) * BoardSizeFactor;   // escala-mundo objetivo
        Debug.Log($"[PopulateCircuit] Bareboard escala≈{boardScale:0.###} → piezas escala-mundo≈{pieceWorld:0.###} (factor {BoardSizeFactor}).");

        int n = 0;
        // Rama 1 (REFERENCIA, pre-cableada completa): R VCC→COL_0 ; LED ánodo=COL_0 → cátodo=GND.
        // Enciende en cuanto los cables de la batería energizan los rieles (indica "rieles vivos").
        PlaceBridge(resPrefab, board, vcc0, col0, "Circuit_R1", 470f, pieceWorld);                    n++;
        PlaceBridge(ledPrefab, board, col0, gnd0, "Circuit_LED1_ok", 0f, pieceWorld);                 n++;
        // Rama 2 (LA TAREA, con HUECO a propósito): R VCC→COL_1 ; LED ánodo=COL_1 → cátodo=COL_2.
        // Falta cerrar COL_2→GND con un JUMPER slot→slot que enchufa el jugador. El LED arranca con
        // polaridad INVERTIDA (dañado); el Técnico envía el LED nuevo (Reto2CircuitGuard lo cablea a
        // COL_1→COL_2 con polaridad correcta). Enciende sólo con: batería en rieles + jumper COL_2→GND.
        PlaceBridge(resPrefab, board, vcc1, col1, "Circuit_R2", 470f, pieceWorld);                    n++;
        PlaceBridge(ledPrefab, board, col1, col2, "Circuit_LED2_damaged", 0f, pieceWorld, damaged: true); n++;

        // Cables (jumpers) VISUALES fuente → rieles, referenciados a los slots. La conexión
        // eléctrica ya existe (los nodos de la fuente SON los rieles); esto es para que se VEA.
        Transform src = board.Find("Fuente_9V");
        Vector3 srcPos = src != null ? src.position : vcc0.transform.position;
        AddJumper(board, srcPos, vcc0.transform.position, "Cable_VCC", new Color(0.90f, 0.20f, 0.15f), pieceWorld);
        AddJumper(board, srcPos, gnd0.transform.position, "Cable_GND", new Color(0.08f, 0.08f, 0.08f), pieceWorld);

        Debug.Log($"[PopulateCircuit] {n} piezas colocadas (2 ramas R+LED; LED2 invertido) + " +
                  "2 cables fuente→VCC/GND. El jugador gira LED2 180° para encenderlo → victoria por MNA.");
        return n;
    }

    const string JumperPrefabPath = "Assets/Prefabs/Cable_Jumper.prefab";

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root) { var r = FindDeep(c, name); if (r != null) return r; }
        return null;
    }

    /// <summary>Cable MOVIBLE: instancia el Cable_Jumper (puntas agarrables Probe_A/Probe_B + VRCableRenderer)
    /// y coloca sus puntas en 'a' y 'b'. El jugador puede agarrar las puntas y ajustarlas al bareboard.</summary>
    static void AddJumper(Transform board, Vector3 a, Vector3 b, string name, Color color, float worldScale)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(JumperPrefabPath);
        if (prefab == null)
        {
            // Fallback: línea estática si falta el prefab.
            var goFb = new GameObject(name);
            goFb.transform.SetParent(board, false);
            var lr = goFb.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.positionCount = 2; lr.SetPosition(0, a); lr.SetPosition(1, b);
            lr.startWidth = lr.endWidth = 0.006f;
            var sh = Shader.Find("Sprites/Default"); if (sh != null) lr.material = new Material(sh);
            lr.startColor = lr.endColor = color;
            return;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, board);
        go.name = name;
        // Escala-MUNDO atada al Bareboard (igual que las piezas), contrarrestando la escala del padre.
        Vector3 ls = go.transform.parent != null ? go.transform.parent.lossyScale : Vector3.one;
        go.transform.localScale = new Vector3(worldScale / Mathf.Max(Mathf.Abs(ls.x), 1e-5f),
                                              worldScale / Mathf.Max(Mathf.Abs(ls.y), 1e-5f),
                                              worldScale / Mathf.Max(Mathf.Abs(ls.z), 1e-5f));
        // Puntas → a los dos extremos (fuente y riel).
        var pa = FindDeep(go.transform, "Probe_A");
        var pb = FindDeep(go.transform, "Probe_B");
        if (pa != null) pa.position = a;
        if (pb != null) pb.position = b;

        // CABLE FIJO: desactivar el agarre (XR interactables) y la física, para que no se pueda
        // mover en VR ni caiga. Sigue siendo visible (VRCableRenderer) y AJUSTABLE en el Editor
        // moviendo Probe_A/Probe_B.
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && (mb.GetType().Name.Contains("Interactable") || mb.GetType().Name.Contains("Grab")))
                mb.enabled = false;
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
            { rb.isKinematic = true; rb.useGravity = false; }
    }

    /// <summary>Coloca una pieza puenteando dos slots: leadA=slotA (ánodo en LED), leadB=slotB.
    /// Fija los nodos por railId (modo DETERMINISTA): la conexión eléctrica NO depende de la
    /// posición física de las patas (inmune al escalado de zona / calibración VR). Si <paramref name="damaged"/>
    /// es true, el LED arranca con polaridad invertida (falla a corregir → el Técnico envía el LED nuevo).</summary>
    static void PlaceBridge(GameObject prefab, Transform board, ProtoboardSlot slotA, ProtoboardSlot slotB,
                            string name, float resistorOhms, float worldScale, bool damaged = false)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, board);
        go.name = name;

        // Los prefabs Delivered_* traen el MeshRenderer DESACTIVADO (se activa al entregarse en
        // runtime). Colocados en escena quedan invisibles → activamos todos sus renderers.
        foreach (var rend in go.GetComponentsInChildren<Renderer>(true)) rend.enabled = true;

        Vector3 pa = slotA.transform.position, pb = slotB.transform.position;
        // Colocar la pieza SOBRE el hueco del slot A (no en el punto medio, que quedaba flotando).
        go.transform.position = pa;
        // NO rotar (LookRotation se distorsiona bajo la escala del padre → "al revés").

        // ESCALA atada al Bareboard: fijamos la escala-MUNDO de la pieza a 'worldScale' contrarrestando
        // la escala del padre. IMPORTANTE: las patas (leadA/leadB) se fijan DESPUÉS de escalar a la
        // posición mundial de los slots → quedan sobre los huecos SIN importar el tamaño.
        Vector3 pls = go.transform.parent != null ? go.transform.parent.lossyScale : Vector3.one;
        go.transform.localScale = new Vector3(worldScale / Mathf.Max(Mathf.Abs(pls.x), 1e-5f),
                                              worldScale / Mathf.Max(Mathf.Abs(pls.y), 1e-5f),
                                              worldScale / Mathf.Max(Mathf.Abs(pls.z), 1e-5f));

        var comp = go.GetComponent<ElectricalComponent>() ?? go.GetComponentInChildren<ElectricalComponent>(true);
        if (comp == null) { Debug.LogWarning($"[PlaceBridge] '{name}' sin ElectricalComponent."); return; }
        if (resistorOhms > 0f && comp is Resistor r) r.resistance = resistorOhms;
        if (comp is LED led) led.polarityInverted = damaged;   // rama dañada = LED invertido (apagado)

        var conn = comp.GetComponent<ProtoboardConnector>() ?? comp.gameObject.AddComponent<ProtoboardConnector>();
        conn.snapRadius = 0.05f;   // 5 cm — tolerante (solo relevante si algún día se desbloquea)

        // Patas VISUALES exactas sobre los slots (para que se VEA que las patas tocan los huecos).
        // SE FIJAN DESPUÉS DE ESCALAR → world = slot.
        var la = new GameObject("LeadA").transform; la.SetParent(comp.transform, worldPositionStays: true); la.position = pa;
        var lb = new GameObject("LeadB").transform; lb.SetParent(comp.transform, worldPositionStays: true); lb.position = pb;
        conn.leadA = la; conn.leadB = lb;

        // ELÉCTRICO DETERMINISTA: fijar los nodos por railId (ánodo=slotA, cátodo=slotB).
        // Bind() ya no reengancha por posición → la rama funciona pase lo que pase con la escala.
        conn.lockNodes = true;
        conn.lockRailA = slotA.railId;
        conn.lockRailB = slotB.railId;
    }

    static void ConfigToken(GameObject go, float resistorOhms)
    {
        var comp = go.GetComponent<ElectricalComponent>() ?? go.GetComponentInChildren<ElectricalComponent>(true);
        if (comp == null) { Debug.LogWarning($"[Reto2ProtoboardSetup] '{go.name}' sin ElectricalComponent."); return; }

        if (resistorOhms > 0f && comp is Resistor r) r.resistance = resistorOhms;

        var conn = comp.GetComponent<ProtoboardConnector>() ?? comp.gameObject.AddComponent<ProtoboardConnector>();
        conn.snapRadius = 0.015f;   // 1.5 cm — tolerante para el primer test
    }
}

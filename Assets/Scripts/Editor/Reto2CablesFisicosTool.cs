using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ETAPA 1 del sistema de cables físicos (Cable-physics de Hrober0) para el Reto 2.
/// Crea/spawnea prefabs de cable físico (PhysicCable con conectores macho) "tirados en el suelo"
/// dentro de Reto2_Zone, para que el jugador los agarre y conecte al protoboard.
///
/// El prefab PhysicCableMM ya trae toda la física (puntos + SpringJoints → se dobla como culebra y
/// se estira al tirar) y un Connector (macho) en cada punta. Aquí solo los instanciamos, los
/// nombramos y los repartimos por el suelo. La conexión eléctrica + el puente XR se hacen en etapas
/// siguientes.
///
/// Ejecutar: Tools → TITA → Reto 2 → Cables físicos - Crear en el suelo
///           (headless: -executeMethod Reto2CablesFisicosTool.SpawnBatch)
/// </summary>
public static class Reto2CablesFisicosTool
{
    const string ScenePath   = "Assets/Scenes/Explorador.unity";
    const string ZoneName    = "Reto2_Zone";
    const string CablePrefab  = "Assets/Scripts/cables/PhysicCalbes/Prefabs/PhysicCableMM.prefab";
    const string ContainerName = "CablesFisicos_Reto2";

    const int   NumCables   = 3;        // cuántos cables tirar en el suelo
    const float ConnectorScaleFactor = 0.5f;  // reduce el tamaño de los polos (Start/End); baja más si siguen grandes
    static readonly Vector3 FloorLocalPos = new Vector3(0f, -0.5f, -0.6f); // punto base "suelo" (local a la zona)
    const float Spread      = 0.35f;    // separación entre cables (m)

    [MenuItem("Tools/TITA/Reto 2/Cables físicos - Crear en el suelo")]
    public static void SpawnMenu()
    {
        int n = Spawn();
        EditorUtility.DisplayDialog("Reto 2 — Cables físicos",
            n > 0 ? $"{n} cables físicos (PhysicCableMM) creados en el suelo de Reto2_Zone. " +
                    "Reposiciónalos/escálalos en el Editor. (Falta: conectores hembra en slots + puente eléctrico.)"
                  : "No se pudo (¿falta el prefab PhysicCableMM o Reto2_Zone?). Revisa la consola.", "OK");
    }

    public static void SpawnBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int n = Spawn();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(n > 0 ? $"[Reto2CablesFisicos] {n} cables físicos creados y guardado."
                        : "[Reto2CablesFisicos] FALLÓ: falta prefab o Reto2_Zone.");
    }

    static int Spawn()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();

        Transform zone = all.FirstOrDefault(t => t.name == ZoneName);
        if (zone == null) { Debug.LogError("[Reto2CablesFisicos] No encontré Reto2_Zone."); return 0; }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CablePrefab);
        if (prefab == null) { Debug.LogError($"[Reto2CablesFisicos] No cargué el prefab {CablePrefab}."); return 0; }

        // Idempotente: borrar un contenedor previo (con != null en el loop por seguridad).
        foreach (var old in all.Where(t => t != null && t.name == ContainerName).Select(t => t.gameObject).ToArray())
            if (old != null) Object.DestroyImmediate(old);

        var container = new GameObject(ContainerName);
        Undo.RegisterCreatedObjectUndo(container, "Crear cables físicos Reto 2");
        container.transform.SetParent(zone, false);
        container.transform.localPosition = Vector3.zero;

        // Colores estilo Arduino (índices del enum Connector.CableColor: White0 Red1 Green2 Yellow3 Blue4 Cyan5 Magenta6)
        int[] arduinoColors = { 1, 3, 2, 4, 6 }; // Red, Yellow, Green, Blue, Magenta

        int n = 0;
        for (int i = 0; i < NumCables; i++)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container.transform);
            go.name = $"CableFisico_{i}";
            // Repartir por el "suelo" (local a la zona). Ajustable en el Editor.
            go.transform.localPosition = FloorLocalPos + new Vector3((i - (NumCables - 1) * 0.5f) * Spread, 0f, 0f);
            go.transform.localRotation = Quaternion.identity;

            AparienciaArduino(go, arduinoColors[i % arduinoColors.Length]);
            n++;
        }

        Debug.Log($"[Reto2CablesFisicos] {n} cables PhysicCableMM (finos + colores Arduino) en '{ContainerName}' " +
                  $"bajo Reto2_Zone (base local {FloorLocalPos}). Reposiciónalos en el Editor.");
        return n;
    }

    /// <summary>Re-aplica la apariencia (puntos/grosor/curva) a los cables YA existentes SIN re-spawn
    /// (conserva su posición). Útil tras cambiar los parámetros del jumper.</summary>
    [MenuItem("Tools/TITA/Reto 2/Cables físicos - Reaplicar apariencia (sin re-spawn)")]
    public static void ReaplicarApariencia()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        var cont = all.FirstOrDefault(t => t.name == ContainerName);
        if (cont == null) { EditorUtility.DisplayDialog("Reto 2 — Cables", "No encontré CablesFisicos_Reto2.", "OK"); return; }

        int[] arduinoColors = { 1, 3, 2, 4, 6 };
        int i = 0, n = 0;
        foreach (Transform cable in cont)
        {
            AparienciaArduino(cable.gameObject, arduinoColors[i % arduinoColors.Length]);
            i++; n++;
        }
        EditorSceneManager.MarkSceneDirty(cont.gameObject.scene);
        Debug.Log($"[Reto2CablesFisicos] Apariencia re-aplicada a {n} cables (10 puntos, curva suave), sin moverlos.");
        EditorUtility.DisplayDialog("Reto 2 — Cables", $"Apariencia re-aplicada a {n} cables (curva suave). No se movieron.", "OK");
    }

    /// <summary>Convierte los cables físicos del Reto 2 a un ARCO PARABÓLICO limpio (VRCableRenderer,
    /// el mismo sistema del Reto 4): dibuja una curva en U entre las 2 puntas → NUNCA se enreda como
    /// nido (es una curva calculada, no una cuerda de física). Oculta los segmentos de la cuerda física
    /// (siguen ahí, invisibles, para el enlace) y deja las 2 puntas agarrables/enchufables visibles.
    /// No mueve nada. Reversible reactivando los renderers ocultos.</summary>
    [MenuItem("Tools/TITA/Reto 2/Cables físicos - Curva limpia (arco parabólico)")]
    public static void CurvaLimpiaMenu()
    {
        int n = CurvaLimpia(recto: false);
        EditorUtility.DisplayDialog("Reto 2 — Cables",
            n > 0 ? $"{n} cables convertidos a arco parabólico (U limpia, sin nido). Las puntas siguen agarrables."
                  : "No encontré cables (¿corriste 'Crear en el suelo'?).", "OK");
    }

    /// <summary>Cables RECTOS: línea directa punta a punta, sin la parábola.</summary>
    [MenuItem("Tools/TITA/Reto 2/Cables físicos - RECTOS (sin curva)")]
    public static void RectosMenu()
    {
        int n = CurvaLimpia(recto: true);
        EditorUtility.DisplayDialog("Reto 2 — Cables",
            n > 0 ? $"{n} cables convertidos a RECTOS (línea directa, sin curva). Las puntas siguen agarrables."
                  : "No encontré cables (¿corriste 'Crear en el suelo'?).", "OK");
    }

    static int CurvaLimpia(bool recto = false)
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        var cont = all.FirstOrDefault(t => t.name == ContainerName);
        if (cont == null) { Debug.LogError("[Reto2CablesFisicos] No encontré CablesFisicos_Reto2."); return 0; }

        int n = 0;
        foreach (Transform cable in cont)
        {
            // Las 2 puntas macho (Connector) = origen/destino del arco.
            var conns = cable.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && c.GetType().Name == "Connector")
                .Select(c => c.transform).ToArray();
            if (conns.Length < 2) { Debug.LogWarning($"[CurvaLimpia] '{cable.name}' no tiene 2 Connectors — omitido."); continue; }
            Transform a = conns[0], b = conns[1];

            // Ocultar los renderers de la cuerda física, MENOS los de las 2 puntas (los plugs que agarras).
            foreach (var rend in cable.GetComponentsInChildren<Renderer>(true))
            {
                if (rend is LineRenderer) continue;
                if (EsDescendiente(rend.transform, a) || EsDescendiente(rend.transform, b)) continue;
                rend.enabled = false;
            }

            // Arco parabólico en un hijo "CableArc" (LineRenderer + VRCableRenderer).
            Transform arcT = cable.Find("CableArc");
            GameObject arc = arcT != null ? arcT.gameObject : new GameObject("CableArc");
            if (arcT == null) arc.transform.SetParent(cable, false);

            // OJO: usar chequeo explícito (== null respeta el null "falso" de Unity); el patrón
            // GetComponent() ?? AddComponent() puede NO añadir el componente y luego reventar.
            var lr = arc.GetComponent<LineRenderer>();
            if (lr == null) lr = arc.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.startWidth = lr.endWidth = 0.012f;   // más grueso = se ve como jumper, no hilo
            lr.numCornerVertices = lr.numCapVertices = 6;
            Color col = LeerColorTint(cable);
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (sh != null) { var m = new Material(sh); m.color = col; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col); lr.sharedMaterial = m; }
            lr.startColor = lr.endColor = col;

            var vr = arc.GetComponent<VRCableRenderer>();
            if (vr == null) vr = arc.AddComponent<VRCableRenderer>();
            vr.origin    = a;
            vr.target    = b;
            vr.arcUpward = true;    // arquea hacia arriba = se levanta del tablero, claro y limpio
            vr.segments  = recto ? 2 : 24;
            vr.straight  = recto;

            // Pintar YA (para verlo al instante en el Editor; luego VRCableRenderer lo mantiene).
            if (recto)
            {
                lr.positionCount = 2;
                lr.SetPosition(0, a.position);
                lr.SetPosition(1, b.position);
            }
            else PintarArcoInicial(lr, a.position, b.position, vr.arcUpward, vr.segments);
            n++;
        }

        EditorSceneManager.MarkSceneDirty(cont.gameObject.scene);
        Debug.Log($"[Reto2CablesFisicos] {n} cables → arco parabólico limpio (U). Sin nido; puntas agarrables intactas.");
        return n;
    }

    static bool EsDescendiente(Transform t, Transform ancestro)
    {
        for (var p = t; p != null; p = p.parent) if (p == ancestro) return true;
        return false;
    }

    /// <summary>Puebla el LineRenderer con la parábola (misma Bézier que VRCableRenderer) para que
    /// se vea al instante en el Editor, sin esperar al primer Update.</summary>
    static void PintarArcoInicial(LineRenderer lr, Vector3 start, Vector3 end, bool arcUpward, int segments)
    {
        if (lr == null) return;
        float dist = Vector3.Distance(start, end);
        float h    = Mathf.Clamp(dist * 0.4f, 0.02f, 0.12f);
        Vector3 ctrl = (start + end) * 0.5f + (arcUpward ? Vector3.up : Vector3.down) * (2f * h);
        lr.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float t = (float)i / (segments - 1);
            float u = 1f - t;
            lr.SetPosition(i, u * u * start + 2f * u * t * ctrl + t * t * end);
        }
    }

    static Color LeerColorTint(Transform cable)
    {
        var tint = cable.GetComponent<CableColorTint>();
        return tint != null ? tint.color : Color.red;
    }

    /// <summary>Añade CableElectricalBridge a TODOS los cables físicos (PhysicCable) de la escena,
    /// encuentre o no el contenedor. Es el puente eléctrico que faltaba (0 CableElectricalBridge).
    /// Sin esto, ningún cable conduce (0 Jumper aunque conectes las 2 puntas).</summary>
    [MenuItem("Tools/TITA/Reto 2/Cables físicos - Añadir puentes eléctricos (robusto)")]
    public static void AnadirPuentesMenu()
    {
        var cables = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t))
            .Where(t => t.GetComponents<Component>().Any(c => c != null && c.GetType().Name == "PhysicCable"))
            .Select(t => t.gameObject).Distinct().ToArray();

        int nuevos = 0, ya = 0;
        foreach (var go in cables)
        {
            if (go.GetComponent<CableElectricalBridge>() != null) { ya++; continue; }
            go.AddComponent<CableElectricalBridge>();
            nuevos++;
        }
        if (cables.Length > 0) EditorSceneManager.MarkSceneDirty(cables[0].scene);

        string msg = $"Cables PhysicCable encontrados: {cables.Length}. Puentes NUEVOS: {nuevos} (ya tenían: {ya}).";
        Debug.Log("[Reto2CablesFisicos] " + msg);
        EditorUtility.DisplayDialog("Reto 2 — Puentes eléctricos",
            msg + (cables.Length == 0 ? "\n\n⚠ No hay cables PhysicCable (corre 'Crear en el suelo')." : ""), "OK");
    }

    /// <summary>Vuelve a la CUERDA FÍSICA real (quita el arco/CableArc, re-muestra el mesh), le pone
    /// parámetros estables y desactiva la auto-colisión (CableSelfCollisionOff) → cae recta al piso y
    /// cuelga en parábola sin enredarse. Luego afinas la forma con springForce/space en el Inspector.</summary>
    [MenuItem("Tools/TITA/Reto 2/Cables físicos - Cuerda estable (revertir arco + sin nido)")]
    public static void CuerdaEstableMenu()
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
        var cont = all.FirstOrDefault(t => t.name == ContainerName);
        if (cont == null) { EditorUtility.DisplayDialog("Reto 2 — Cables", "No encontré CablesFisicos_Reto2.", "OK"); return; }

        int n = 0;
        foreach (Transform cable in cont)
        {
            // 1. Quitar el arco y volver a MOSTRAR la cuerda física.
            var arc = cable.Find("CableArc");
            if (arc != null) Object.DestroyImmediate(arc.gameObject);
            foreach (var rend in cable.GetComponentsInChildren<Renderer>(true))
                if (!(rend is LineRenderer)) rend.enabled = true;

            // 2. Parámetros estables en PhysicCable + regenerar el layout.
            foreach (var comp in cable.GetComponentsInChildren<Component>(true))
            {
                if (comp == null || comp.GetType().Name != "PhysicCable") continue;
                var so = new SerializedObject(comp);
                var pSize   = so.FindProperty("size");
                var pSpace  = so.FindProperty("space");
                var pPoints = so.FindProperty("numberOfPoints");
                var pSpring = so.FindProperty("springForce");
                if (pSize   != null) pSize.floatValue  = 0.008f;
                if (pSpace  != null) pSpace.floatValue  = 0.025f;  // total ≈ 0.025×18 ≈ 45 cm (alcanza lejos)
                if (pPoints != null) pPoints.intValue   = 18;      // más largo + curva suave
                if (pSpring != null) pSpring.floatValue  = 400f;   // más rígido = recto al estirar, menos nido
                so.ApplyModifiedProperties();
                var m = comp.GetType().GetMethod("UpdatePoints", BindingFlags.Instance | BindingFlags.NonPublic);
                if (m != null) { try { m.Invoke(comp, null); } catch (System.Exception e) { Debug.LogWarning("[CablesFisicos] UpdatePoints: " + e.Message); } }
            }

            // 3. Sin AUTO-COLISIÓN (mata el nido; sigue chocando con el piso).
            if (cable.GetComponent<CableSelfCollisionOff>() == null)
                cable.gameObject.AddComponent<CableSelfCollisionOff>();
            n++;
        }

        EditorSceneManager.MarkSceneDirty(cont.gameObject.scene);
        Debug.Log($"[Reto2CablesFisicos] {n} cables → cuerda física estable (arco quitado, sin auto-colisión, springForce 400, 10 puntos).");
        EditorUtility.DisplayDialog("Reto 2 — Cables",
            $"{n} cables restaurados a cuerda física estable (sin nido). Afina la forma con springForce/space en el Inspector.", "OK");
    }

    /// <summary>Apariencia de jumper Arduino: adelgaza el cable (PhysicCable.size) y colorea los
    /// conectores. Vía SerializedObject por nombre → no depende del asmdef de la librería.</summary>
    static void AparienciaArduino(GameObject go, int colorIndex)
    {
        foreach (var comp in go.GetComponentsInChildren<Component>(true))
        {
            if (comp == null) continue;
            string tn = comp.GetType().Name;
            if (tn == "PhysicCable")
            {
                var so = new SerializedObject(comp);
                var pSize   = so.FindProperty("size");
                var pSpace  = so.FindProperty("space");
                var pPoints = so.FindProperty("numberOfPoints");
                var pSpring = so.FindProperty("springForce");
                // Jumper del Reto 2: MUCHOS puntos = curva suave (con 3 se veía como palito/nido);
                // 'space' = largo mundial de cada segmento (total = space×numberOfPoints ≈ 20 cm → con
                // holgura cae en catenaria/U bajo gravedad al conectar las 2 puntas). 'size' = grosor.
                if (pPoints != null) pPoints.intValue   = 18;      // más puntos = más largo + curva suave
                if (pSize   != null) pSize.floatValue    = 0.008f; // un poco más grueso (se veía como hilo)
                if (pSpace  != null) pSpace.floatValue   = 0.025f; // total = 0.025×18 ≈ 0.45 m (alcanza lejos)
                if (pSpring != null) pSpring.floatValue  = 400f;   // rígido = recto al estirar, sin nido
                so.ApplyModifiedProperties();

                // Regenerar los puntos/segmentos para que el layout ESTÁTICO use el nuevo tamaño
                // (si no, la vista de Editor muestra el layout viejo grande). Invoca el botón interno.
                var m = comp.GetType().GetMethod("UpdatePoints", BindingFlags.Instance | BindingFlags.NonPublic);
                if (m != null) { try { m.Invoke(comp, null); } catch (System.Exception e) { Debug.LogWarning("[CablesFisicos] UpdatePoints falló: " + e.Message); } }
            }
            else if (tn == "Connector")
            {
                var so = new SerializedObject(comp);
                var pCol = so.FindProperty("<ConnectionColor>k__BackingField");
                if (pCol != null) { pCol.enumValueIndex = colorIndex; so.ApplyModifiedProperties(); }
                // Reducir el tamaño del polo (Start/End) para que sea proporcional a la hembra del slot.
                comp.transform.localScale *= ConnectorScaleFactor;
                // Puente XR (Etapa 2b): hacer la punta agarrable/enchufable con los mandos de la Quest.
                if (comp.gameObject.GetComponent<XRCableConnector>() == null)
                    comp.gameObject.AddComponent<XRCableConnector>();
            }
        }

        // Teñir TODA la manguera con el color (además del conector) → jumper de color completo.
        var tint = go.GetComponent<CableColorTint>() ?? go.AddComponent<CableColorTint>();
        tint.color = ColorFromIndex(colorIndex);

        // Anti-nido: desactiva la auto-colisión de los puntos de la cuerda (sigue chocando con el piso).
        if (go.GetComponent<CableSelfCollisionOff>() == null)
            go.AddComponent<CableSelfCollisionOff>();
    }

    static Color ColorFromIndex(int i) => i switch
    {
        1 => Color.red, 2 => Color.green, 3 => Color.yellow, 4 => Color.blue,
        5 => Color.cyan, 6 => Color.magenta, _ => Color.white
    };
}

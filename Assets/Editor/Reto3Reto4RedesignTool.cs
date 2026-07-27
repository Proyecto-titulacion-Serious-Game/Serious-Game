#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Agranda y separa los componentes de Reto3_Zone y Reto4_Zone para que el Explorador
/// los observe/manipule mejor en VR, y añade etiquetas de número de PIN sobre el Arduino
/// (Reto 4). El ANCLA de cada zona (posición/rotación del propio Reto3_Zone/Reto4_Zone,
/// que es la posición dentro del cuarto) NO se toca — solo se agrandan y separan sus hijos
/// alrededor del centroide del grupo, así el cuarto no se reubica.
///
/// Menú: Tools → TITA → Reto 3-4 → Rediseñar (agrandar + espaciar + labels PIN)
/// Batch: Reto3Reto4RedesignTool.RedesignBatch()
/// </summary>
public static class Reto3Reto4RedesignTool
{
    const float Reto3ComponentScale = 1.35f;
    const float Reto3Spread = 1.45f;

    const float Reto4ComponentScale = 1.3f;
    const float Reto4CableBoxScale = 1.15f;
    const float Reto4Spread = 1.4f;

    const float PinLabelNetWorldScale = 1.2f; // tamaño de mundo objetivo del texto (cancela la escala acumulada del padre)
    const float PinLabelFontSize = 2.5f;
    const float PinLabelWorldOffset = 0.012f; // metros sobre el nodo

    const string TmpFontGuid = "8f586378b4e144a9851e7b34d9b748ee"; // fuente TMP ya usada en toda la escena

    [MenuItem("Tools/TITA/Reto 3-4/Rediseñar (agrandar + espaciar + labels PIN)")]
    public static void RedesignMenu()
    {
        RedesignBatch();
        EditorUtility.DisplayDialog("Rediseño Reto 3/4",
            "Listo. Revisá en el Editor (posiciones, que nada quede fuera de la mesa/board, orientación de las etiquetas de PIN) y probá en VR antes de dar por bueno.\n\n" +
            "La escena ya quedó guardada.", "OK");
    }

    const string ExploradorScenePath = "Assets/Scenes/Explorador.unity";

    /// <summary>Entry point para -executeMethod: abre Explorador.unity explícitamente (no asume la escena activa del batch).</summary>
    public static void RedesignBatch()
    {
        var scene = EditorSceneManager.OpenScene(ExploradorScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError($"[Reto3Reto4Redesign] No se pudo abrir {ExploradorScenePath}.");
            return;
        }

        RedesignReto3(scene);
        RedesignReto4(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Reto3Reto4Redesign] Completado y escena guardada.");
    }

    // ─────────────────────────────────────────────
    //  Reto 4 — inclinación (atril / pared, ver Reto4TiltDegrees)
    // ─────────────────────────────────────────────

    /// <summary>
    /// Grados de inclinación sobre el eje X local (que en Reto4_Zone coincide con el eje X de mundo,
    /// ya que Reto4_Zone tiene rotación identidad). -90 = vertical completo (pared), valores entre
    /// -30 y -45 = atril inclinado. Negativo o positivo según qué borde deba levantarse hacia el
    /// jugador — NO se pudo verificar visualmente cuál signo es el correcto (no hay forma de
    /// renderizar la escena desde acá). Si queda inclinado para el lado equivocado, cambiar el signo
    /// acá y volver a correr TiltReto4Batch (es idempotente: reajusta el ángulo del grupo existente
    /// en vez de re-crear/re-anidar).
    /// </summary>
    const float Reto4TiltDegrees = -90f;
    const string Reto4TiltGroupName = "Reto4_TiltGroup";

    [MenuItem("Tools/TITA/Reto 3-4/Reto 4: Inclinar Arduino+Protoboard (atril)")]
    public static void TiltReto4Menu()
    {
        TiltReto4Batch();
        EditorUtility.DisplayDialog("Inclinar Reto 4",
            "Listo. Revisá en el Editor: (1) que la inclinación levante el borde correcto hacia el " +
            "jugador (si está al revés, cambiar el signo de Reto4TiltDegrees en el script y volver a " +
            "correr este mismo menú — es seguro re-correrlo), (2) que nada del Arduino/protoboard/caja " +
            "de cables quede atravesando la mesa o fuera de ella, (3) que los cables (arco parabólico) " +
            "no se vean cruzando raro — a esta inclinación moderada deberían verse aceptables, pero no " +
            "se pudo confirmar visualmente desde acá.\n\nLa escena ya quedó guardada.", "OK");
    }

    /// <summary>Entry point para -executeMethod.</summary>
    public static void TiltReto4Batch()
    {
        var scene = EditorSceneManager.OpenScene(ExploradorScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError($"[Reto4Tilt] No se pudo abrir {ExploradorScenePath}.");
            return;
        }

        var zoneGO = FindInScene(scene, "Reto4_Zone");
        if (zoneGO == null) { Debug.LogError("[Reto4Tilt] Reto4_Zone no encontrado."); return; }
        var zone = zoneGO.transform;
        Vector3 anchorBefore = zone.localPosition;

        Transform arduino = zone.Find("Arduino");
        Transform cableBox = zone.Find("CableBox_VR");
        Transform proto = zone.childCount > 2 ? zone.GetChild(2) : null;
        if (arduino == null && zone.childCount > 1) arduino = zone.GetChild(1);
        if (cableBox == null && zone.childCount > 3) cableBox = zone.GetChild(3);

        var existingGroup = zone.Find(Reto4TiltGroupName);

        if (existingGroup != null)
        {
            // Ya se corrió antes: solo reajustar el ángulo, sin volver a reparentar nada.
            Undo.RecordObject(existingGroup, "Reto4 Tilt Angle");
            existingGroup.localRotation = Quaternion.Euler(Reto4TiltDegrees, 0f, 0f);
            EditorUtility.SetDirty(existingGroup.gameObject);
            Debug.Log($"[Reto4Tilt] Grupo existente reajustado a {Reto4TiltDegrees}°.");
        }
        else
        {
            if (arduino == null || proto == null || cableBox == null)
            {
                Debug.LogError("[Reto4Tilt] No se pudieron resolver Arduino/Protoboard/CableBox (por nombre ni por índice). Abortando.");
                return;
            }

            // Pivote = centroide de las 3 piezas (mismo criterio que el resize anterior), así el grupo
            // se inclina alrededor de su propio centro en vez de un borde arbitrario.
            Vector3 pivot = (arduino.localPosition + proto.localPosition + cableBox.localPosition) / 3f;

            var groupGO = new GameObject(Reto4TiltGroupName);
            Undo.RegisterCreatedObjectUndo(groupGO, "Reto4 Tilt Group");
            groupGO.transform.SetParent(zone, false);
            groupGO.transform.localPosition = pivot;
            groupGO.transform.localRotation = Quaternion.identity;
            groupGO.transform.localScale = Vector3.one;

            // Reparentar preservando la pose de MUNDO (nada salta visualmente todavía en este paso).
            Undo.SetTransformParent(arduino, groupGO.transform, "Reto4 Tilt Reparent");
            Undo.SetTransformParent(proto, groupGO.transform, "Reto4 Tilt Reparent");
            Undo.SetTransformParent(cableBox, groupGO.transform, "Reto4 Tilt Reparent");

            // Recién ahora, con los hijos ya reparentados, inclinar el grupo: rota rígidamente a las 3
            // piezas alrededor del pivote. Las 21 etiquetas de PIN (nietas del Arduino, con rotación
            // local ya fija respecto a su Nodo_* padre) heredan la inclinación automáticamente — no
            // hace falta tocarlas. Los arcos de cable (VRCableRenderer) SÍ usan Vector3.up de MUNDO fijo
            // internamente (no relativo a este grupo), así que a esta inclinación moderada se van a ver
            // un poco menos simétricos que a 0°, pero no deberían verse rotos — no se tocó ese script
            // porque lo comparte con el sistema de cables interactivo real de Reto 4 (mayor riesgo).
            groupGO.transform.localRotation = Quaternion.Euler(Reto4TiltDegrees, 0f, 0f);

            EditorUtility.SetDirty(groupGO);
            Debug.Log($"[Reto4Tilt] Grupo '{Reto4TiltGroupName}' creado en pivote local {pivot}, inclinado {Reto4TiltDegrees}° sobre X.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Reto4Tilt] Reto4_Zone.localPosition antes={anchorBefore} después={zone.localPosition} (debe ser igual). Completado.");
    }

    // ─────────────────────────────────────────────
    //  Reto 4 — restaurar ArduinoCore + pines desde un backup
    // ─────────────────────────────────────────────

    /// <summary>
    /// El GameObject "modelo" (ArduinoCore + ArduinoNetworkBridge + Fusion.NetworkObject + los ~21
    /// Nodo_* con las etiquetas de PIN) desapareció de la escena en algún momento entre dos backups de
    /// esta sesión — NO por nada que este tool hiciera a propósito (nunca se tocó ese GameObject), lo
    /// más probable es un efecto de guardar la escena completa en batchmode con un NetworkObject de
    /// Photon Fusion presente (su pipeline de "scene baking" es sensible a cómo se guarda la escena).
    /// Restaura ese GameObject completo MOVIÉNDOLO desde un backup (vía EditorSceneManager, no copiando
    /// YAML a mano) para no arriesgar romper las referencias internas de ArduinoCore.pinNodeMap ni los
    /// campos internos de Fusion.NetworkObject.
    /// </summary>
    const string ArduinoCoreBackupPath = @"C:\Users\holaq\Proyecto-TITA\Serious-Game\Assets\Scenes\Explorador.unity.bak_20260715_220545";

    [MenuItem("Tools/TITA/Reto 3-4/Reto 4: Restaurar ArduinoCore+Pines desde backup")]
    public static void RestoreArduinoCoreMenu()
    {
        RestoreArduinoCoreBatch();
        EditorUtility.DisplayDialog("Restaurar ArduinoCore",
            "Listo (o ver consola si falló). Revisá en el Editor que 'modelo' (con ArduinoCore, " +
            "ArduinoNetworkBridge y los Nodo_*/labels de PIN) aparezca de nuevo como hijo del Arduino, " +
            "alineado con el modelo 3D. La escena ya quedó guardada.", "OK");
    }

    public static void RestoreArduinoCoreBatch()
    {
        if (!System.IO.File.Exists(ArduinoCoreBackupPath))
        {
            Debug.LogError($"[Reto4RestoreCore] No existe el backup {ArduinoCoreBackupPath}.");
            return;
        }

        var mainScene = EditorSceneManager.OpenScene(ExploradorScenePath, OpenSceneMode.Single);
        if (!mainScene.IsValid())
        {
            Debug.LogError($"[Reto4RestoreCore] No se pudo abrir {ExploradorScenePath}.");
            return;
        }

        // Ya restaurado antes (re-corrida idempotente): si ya hay un ArduinoCore en la escena, no hacer nada.
        if (UnityEngine.Object.FindAnyObjectByType<ArduinoCore>() != null)
        {
            Debug.Log("[Reto4RestoreCore] Ya hay un ArduinoCore en la escena — nada que restaurar.");
            return;
        }

        var arduinoGO = FindInScene(mainScene, "Arduino");
        if (arduinoGO == null)
        {
            Debug.LogError("[Reto4RestoreCore] No se encontró el GameObject 'Arduino' en la escena actual. Abortando.");
            return;
        }

        // Copiar el backup a un .unity temporal reconocido por Unity (el archivo .bak_* no es un asset válido).
        const string tempScenePath = "Assets/Scenes/_TempArduinoCoreRestore.unity";
        string tempSceneFullPath = System.IO.Path.Combine(Application.dataPath, "Scenes/_TempArduinoCoreRestore.unity");
        System.IO.File.Copy(ArduinoCoreBackupPath, tempSceneFullPath, true);
        AssetDatabase.ImportAsset(tempScenePath, ImportAssetOptions.ForceSynchronousImport);

        var backupScene = EditorSceneManager.OpenScene(tempScenePath, OpenSceneMode.Additive);
        if (!backupScene.IsValid())
        {
            Debug.LogError("[Reto4RestoreCore] No se pudo abrir la escena backup temporal.");
            AssetDatabase.DeleteAsset(tempScenePath);
            return;
        }

        ArduinoCore coreInBackup = null;
        foreach (var root in backupScene.GetRootGameObjects())
        {
            coreInBackup = root.GetComponentsInChildren<ArduinoCore>(true).FirstOrDefault();
            if (coreInBackup != null) break;
        }

        if (coreInBackup == null)
        {
            Debug.LogError("[Reto4RestoreCore] El backup tampoco tiene ArduinoCore — no se puede restaurar desde ahí.");
            EditorSceneManager.CloseScene(backupScene, true);
            AssetDatabase.DeleteAsset(tempScenePath);
            return;
        }

        GameObject modeloGO = coreInBackup.gameObject;
        int labelCount = modeloGO.GetComponentsInChildren<TMPro.TextMeshPro>(true).Length;

        // Guardar la pose LOCAL de 'modelo' respecto a su Arduino de origen ANTES de tocar nada — es la
        // calibración fina de los pines contra el modelo 3D (ver ArduinoPinNodeCalibrator), independiente
        // de que el Arduino actual ahora esté inclinado/vertical. La queremos reaplicar tal cual.
        Vector3 localPos   = modeloGO.transform.localPosition;
        Quaternion localRot = modeloGO.transform.localRotation;
        Vector3 localScale = modeloGO.transform.localScale;

        // MoveGameObjectToScene exige que el objeto sea RAÍZ en su escena de origen — 'modelo' está
        // anidado bajo Arduino en el backup, así que primero se lo des-parenta ahí (sin mover nada,
        // worldPositionStays:false conserva los valores locales ya guardados arriba de todos modos).
        modeloGO.transform.SetParent(null, false);

        // Mover el GameObject completo (con TODOS sus hijos: Nodo_D2..D13/A0-A5/GND, y las etiquetas de
        // PIN si estaban ahí) de la escena backup a la escena principal. Esto preserva intactas las
        // referencias internas (ArduinoCore.pinNodeMap, Fusion.NetworkObject, etc.) porque Unity mueve
        // el grafo de objetos vivo, no texto YAML.
        EditorSceneManager.MoveGameObjectToScene(modeloGO, mainScene);

        // Reparentar bajo el Arduino ACTUAL con worldPositionStays:false (NO queremos preservar la pose
        // de MUNDO que tenía en el backup —ahí el Arduino estaba plano—, queremos la pose LOCAL relativa
        // al Arduino, que es la calibración) y reforzarla explícitamente por las dudas.
        modeloGO.transform.SetParent(arduinoGO.transform, false);
        modeloGO.transform.localPosition = localPos;
        modeloGO.transform.localRotation = localRot;
        modeloGO.transform.localScale    = localScale;

        EditorSceneManager.CloseScene(backupScene, true);   // true = descartar sin guardar el backup temporal
        AssetDatabase.DeleteAsset(tempScenePath);

        EditorUtility.SetDirty(modeloGO);
        EditorSceneManager.MarkSceneDirty(mainScene);
        EditorSceneManager.SaveScene(mainScene);

        Debug.Log($"[Reto4RestoreCore] 'modelo' restaurado bajo '{arduinoGO.name}' con {labelCount} etiquetas de PIN incluidas. Completado.");
    }

    /// <summary>
    /// Las 21 etiquetas de PIN (creadas originalmente por <see cref="AddPinLabels"/>, dentro de
    /// <see cref="RedesignBatch"/>) tampoco están en NINGÚN backup de hoy — se perdieron incluso antes
    /// de que 'modelo' desapareciera. En vez de intentar recuperarlas de algún lado que no las tiene,
    /// se regeneran directo con la misma lógica ya probada, ahora que 'modelo'/Nodo_* están restaurados.
    /// Llama a <see cref="AddPinLabels"/> SOLO (no vuelve a escalar/separar nada de Reto3 ni Reto4 —
    /// a diferencia de correr RedesignBatch() de nuevo, que NO es idempotente para esa parte).
    /// </summary>
    [MenuItem("Tools/TITA/Reto 3-4/Reto 4: Regenerar labels de PIN")]
    public static void RegeneratePinLabelsMenu()
    {
        RegeneratePinLabelsBatch();
        EditorUtility.DisplayDialog("Regenerar labels de PIN",
            "Listo (o ver consola si falló). La escena ya quedó guardada.", "OK");
    }

    public static void RegeneratePinLabelsBatch()
    {
        var scene = EditorSceneManager.OpenScene(ExploradorScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError($"[Reto4RegenLabels] No se pudo abrir {ExploradorScenePath}.");
            return;
        }

        var arduinoGO = FindInScene(scene, "Arduino");
        if (arduinoGO == null)
        {
            Debug.LogError("[Reto4RegenLabels] No se encontró 'Arduino' en la escena. Abortando.");
            return;
        }

        AddPinLabels(arduinoGO.transform);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Reto4RegenLabels] Completado y escena guardada.");
    }

    static GameObject FindInScene(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
            if (t != null) return t.gameObject;
        }
        return null;
    }

    static void SpreadFromCentroid(Transform[] items, float spreadFactor)
    {
        if (items.Length == 0) return;
        Vector3 centroid = Vector3.zero;
        foreach (var t in items) centroid += t.localPosition;
        centroid /= items.Length;

        foreach (var t in items)
        {
            Vector3 offset = t.localPosition - centroid;
            offset.x *= spreadFactor;
            offset.z *= spreadFactor; // Y (altura sobre la mesa) no se toca
            Undo.RecordObject(t, "Reto3/4 Redesign Spread");
            t.localPosition = centroid + offset;
            EditorUtility.SetDirty(t);
        }
    }

    static void ScaleUp(Transform t, float factor)
    {
        Undo.RecordObject(t, "Reto3/4 Redesign Scale");
        t.localScale *= factor;
        EditorUtility.SetDirty(t);
    }

    static void RedesignReto3(Scene scene)
    {
        var zoneGO = FindInScene(scene, "Reto3_Zone");
        if (zoneGO == null) { Debug.LogError("[Redesign] Reto3_Zone no encontrado."); return; }
        var zone = zoneGO.transform;
        Vector3 anchorBefore = zone.localPosition;

        string[] names =
        {
            "Battery_9V", "Resistor_Serie_Faulty", "LED_Paralelo", "Capacitor_Invertido",
            "Node_R3_VCC", "Node_R3_AfterR", "Node_R3_GND"
        };
        var items = names.Select(n => zone.Find(n)).Where(t => t != null).ToArray();
        if (items.Length != names.Length)
            Debug.LogWarning($"[Redesign] Reto3: solo se encontraron {items.Length}/{names.Length} hijos esperados por nombre.");

        SpreadFromCentroid(items, Reto3Spread);
        foreach (var t in items) ScaleUp(t, Reto3ComponentScale);

        Debug.Log($"[Redesign] Reto3_Zone: {items.Length} piezas agrandadas x{Reto3ComponentScale} y separadas x{Reto3Spread}. " +
                  $"Reto3_Zone.localPosition antes={anchorBefore} después={zone.localPosition} (debe ser igual).");
    }

    static void RedesignReto4(Scene scene)
    {
        var zoneGO = FindInScene(scene, "Reto4_Zone");
        if (zoneGO == null) { Debug.LogError("[Redesign] Reto4_Zone no encontrado."); return; }
        var zone = zoneGO.transform;
        Vector3 anchorBefore = zone.localPosition;

        // Orden conocido de m_Children en escena: 0=Mesa 1=Arduino 2=Protoboard 3=CableBox_VR 4=CircuitPanel(UI) 5=ZoneHUD
        Transform arduino = zone.Find("Arduino");
        Transform cableBox = zone.Find("CableBox_VR");
        Transform proto = zone.childCount > 2 ? zone.GetChild(2) : null;

        if (arduino == null && zone.childCount > 1) arduino = zone.GetChild(1);
        if (cableBox == null && zone.childCount > 3) cableBox = zone.GetChild(3);

        if (arduino == null || proto == null || cableBox == null)
        {
            Debug.LogError("[Redesign] Reto4: no se pudieron resolver Arduino/Protoboard/CableBox (por nombre ni por índice). " +
                            $"childCount={zone.childCount}. Abortando Reto4 (Reto3 sí se aplicó).");
            return;
        }

        SpreadFromCentroid(new[] { arduino, proto, cableBox }, Reto4Spread);
        ScaleUp(arduino, Reto4ComponentScale);
        ScaleUp(proto, Reto4ComponentScale);
        ScaleUp(cableBox, Reto4CableBoxScale);

        Debug.Log($"[Redesign] Reto4_Zone: Arduino='{arduino.name}' Protoboard='{proto.name}' CableBox='{cableBox.name}' " +
                  $"agrandados x{Reto4ComponentScale}/x{Reto4CableBoxScale}, separados x{Reto4Spread}. " +
                  $"Reto4_Zone.localPosition antes={anchorBefore} después={zone.localPosition} (debe ser igual).");

        AddPinLabels(arduino);
    }

    static void AddPinLabels(Transform arduino)
    {
        var core = arduino.GetComponentInChildren<ArduinoCore>(true);
        Transform nodesRoot = core != null ? core.transform : arduino;

        TMP_FontAsset font = null;
        string fontPath = AssetDatabase.GUIDToAssetPath(TmpFontGuid);
        if (!string.IsNullOrEmpty(fontPath))
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);

        var nodes = nodesRoot.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith("Nodo_") && !t.name.Contains("(")) // descarta duplicados "Nodo_GND (1)"/"(2)"
            .ToArray();

        int created = 0;
        foreach (var node in nodes)
        {
            string labelName = "Label_" + node.name;
            if (node.Find(labelName) != null) continue; // idempotente

            string pinText = node.name.Substring("Nodo_".Length);
            if (pinText == "P13") pinText = "D13";

            var labelGO = new GameObject(labelName);
            Undo.RegisterCreatedObjectUndo(labelGO, "Reto4 Pin Label");
            labelGO.transform.SetParent(node, worldPositionStays: false);
            labelGO.transform.localPosition = Vector3.zero;
            labelGO.transform.localRotation = Quaternion.identity;

            // Cancela la escala acumulada del padre (Arduino x ArduinoCore x nodo) para un tamaño de mundo consistente.
            Vector3 lossy = node.lossyScale;
            labelGO.transform.localScale = new Vector3(
                Mathf.Approximately(lossy.x, 0f) ? 1f : PinLabelNetWorldScale / lossy.x,
                Mathf.Approximately(lossy.y, 0f) ? 1f : PinLabelNetWorldScale / lossy.y,
                Mathf.Approximately(lossy.z, 0f) ? 1f : PinLabelNetWorldScale / lossy.z);

            var tmp = labelGO.AddComponent<TextMeshPro>();
            tmp.text = pinText;
            tmp.fontSize = PinLabelFontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.yellow;
            if (font != null) tmp.font = font;

            // Rotación en espacio MUNDO (no local) para que quede plano boca-arriba sin importar
            // la rotación del Arduino (rotY180) — legible desde arriba, estilo serigrafía de PCB.
            labelGO.transform.position += Vector3.up * PinLabelWorldOffset;
            labelGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            EditorUtility.SetDirty(labelGO);
            created++;
        }

        Debug.Log($"[Redesign] Reto4: {created} etiquetas de PIN creadas bajo '{nodesRoot.name}' (fuente TMP {(font != null ? "OK" : "NO ENCONTRADA, usando default")}).");
    }
}
#endif

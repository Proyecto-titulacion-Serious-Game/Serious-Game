using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Instala y cablea el panel docente (DashboardServer + SessionDataExporter) en la escena
/// activa — normalmente la del Técnico (Host), que es quien sirve el dashboard por HTTP.
///
/// Tras ejecutarlo: entrar en Play; la consola imprime la URL (http://&lt;ip&gt;:8080/).
/// El docente abre esa URL en cualquier navegador del laboratorio.
/// </summary>
public static class DashboardSetupTool
{
    const string GO_NAME = "[TeacherDashboard]";

    [MenuItem("Tools/TITA/Red/Setup Dashboard Docente")]
    public static void Setup()
    {
        // ¿Ya existe un DashboardServer en la escena?
        var existing = Object.FindAnyObjectByType<DashboardServer>(FindObjectsInactive.Include);
        GameObject go = existing != null ? existing.gameObject : null;

        if (go == null)
        {
            go = GameObject.Find(GO_NAME) ?? new GameObject(GO_NAME);
            Undo.RegisterCreatedObjectUndo(go, "Crear TeacherDashboard");
        }

        var exporter = go.GetComponent<SessionDataExporter>();
        if (exporter == null) exporter = Undo.AddComponent<SessionDataExporter>(go);

        var server = go.GetComponent<DashboardServer>();
        if (server == null) server = Undo.AddComponent<DashboardServer>(go);

        // Cablear referencia y valores por defecto seguros.
        var so = new SerializedObject(server);
        so.FindProperty("dataExporter").objectReferenceValue = exporter;
        // localhostOnly = true por defecto (sin permisos extra). Para toda la LAN, ponerlo en false.
        so.FindProperty("localhostOnly").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;

        Debug.Log(
            "[DashboardSetupTool] Panel docente instalado en '" + go.name + "' (escena: " + go.scene.name + ").\n" +
            "  • Al entrar en Play, la consola imprime la URL del panel.\n" +
            "  • Para acceso desde otros equipos de la LAN: DashboardServer.localhostOnly = false\n" +
            "    y ejecutar Unity como admin O: netsh http add urlacl url=http://+:8080/ user=Everyone\n" +
            "  • Recomendado: colocarlo SOLO en la escena del Técnico (Host).");
    }
}

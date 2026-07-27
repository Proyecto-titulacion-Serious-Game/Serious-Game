using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Verificación HEADLESS del flujo real del botón INFO del manual del Técnico:
/// Button_Info -> ToggleGlosario() abre Panel_Glosario; Button_InfoMultimetro (dentro del
/// glosario) -> AbrirInfoMultimetro() muestra el subpanel con la imagen del multímetro;
/// Button_Info otra vez -> ToggleGlosario() cierra TODO (glosario + subpanel).
///
/// Instancia el prefab real (no una copia sintética) y llama los mismos métodos que los OnClick
/// del prefab invocan, confirmado por inspección de Technician_Workstation.prefab.
///
/// Ejecutar:
///   Editor:     Tools → TITA → Reto 2 → Test INFO multímetro en manual (headless)
///   Batch mode: -executeMethod ManualInfoMultimetroTest.Run
/// </summary>
public static class ManualInfoMultimetroTest
{
    const string PrefabPath = "Assets/Prefabs/Technician_Workstation.prefab";

    [MenuItem("Tools/TITA/Reto 2/Test INFO multímetro en manual (headless)")]
    public static void Run()
    {
        int fails = 0;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[Test INFO] No pude cargar el prefab en {PrefabPath}");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        var instance = (GameObject)Object.Instantiate(prefab);
        try
        {
            var toggle = instance.GetComponentInChildren<ManualGlossaryToggle>(true);
            if (toggle == null)
            {
                Debug.LogError("[Test INFO] La instancia no tiene ManualGlossaryToggle.");
                fails++;
            }
            else
            {
                var panelGlosario = toggle.panelGlosario;
                var subPanel = toggle.subPanelInfoMultimetro;

                if (panelGlosario == null) { Debug.LogError("[Test INFO] panelGlosario no está asignado."); fails++; }
                if (subPanel == null) { Debug.LogError("[Test INFO] subPanelInfoMultimetro no está asignado."); fails++; }

                if (panelGlosario != null && subPanel != null)
                {
                    // Estado inicial esperado: todo cerrado.
                    bool estadoInicialOk = !panelGlosario.activeSelf && !subPanel.activeSelf;
                    Log("Estado inicial", estadoInicialOk, panelGlosario, subPanel);
                    if (!estadoInicialOk) fails++;

                    // Paso 1: Button_Info -> ToggleGlosario() (abrir).
                    toggle.ToggleGlosario();
                    bool paso1Ok = panelGlosario.activeSelf && !subPanel.activeSelf;
                    Log("Tras Button_Info (abrir)", paso1Ok, panelGlosario, subPanel);
                    if (!paso1Ok) fails++;

                    // Paso 2: Button_InfoMultimetro -> AbrirInfoMultimetro() (mostrar multímetro).
                    toggle.AbrirInfoMultimetro();
                    bool paso2Ok = panelGlosario.activeSelf && subPanel.activeSelf;
                    Log("Tras Button_InfoMultimetro", paso2Ok, panelGlosario, subPanel);
                    if (!paso2Ok) fails++;

                    // Verificar que la imagen del multímetro y el texto realmente están armados.
                    var img = subPanel.transform.Find("Image_Multimetro")?.GetComponent<Image>();
                    bool imgOk = img != null && img.sprite != null;
                    Debug.Log($"[Test INFO] Image_Multimetro: {(imgOk ? "✓ sprite asignado (" + img.sprite.name + ")" : "✗ FALTA sprite")}");
                    if (!imgOk) fails++;

                    var cuerpo = subPanel.transform.Find("TMP_InfoMultimetro")?.GetComponent<TMP_Text>();
                    bool textoOk = cuerpo != null && !string.IsNullOrEmpty(cuerpo.text) &&
                                   cuerpo.text.Contains("VOLTAJE") && cuerpo.text.Contains("RESISTENCIA");
                    Debug.Log($"[Test INFO] TMP_InfoMultimetro: {(textoOk ? "✓ texto con los 3 modos" : "✗ FALTA/incompleto")} " +
                              $"-> \"{Truncar(cuerpo != null ? cuerpo.text : null)}\"");
                    if (!textoOk) fails++;

                    // Paso 3: Button_Info otra vez -> ToggleGlosario() (cerrar TODO, incluido el subpanel).
                    toggle.ToggleGlosario();
                    bool paso3Ok = !panelGlosario.activeSelf && !subPanel.activeSelf;
                    Log("Tras Button_Info otra vez (cerrar)", paso3Ok, panelGlosario, subPanel);
                    if (!paso3Ok) fails++;

                    // Regresión: reabrir, mostrar multímetro, y esta vez cerrar con CerrarGlosario()
                    // (Button_CerrarGlosario) en vez de ToggleGlosario() -- debe limpiar igual.
                    toggle.ToggleGlosario();
                    toggle.AbrirInfoMultimetro();
                    toggle.CerrarGlosario();
                    bool paso4Ok = !panelGlosario.activeSelf && !subPanel.activeSelf;
                    Log("Tras reabrir + Button_CerrarGlosario", paso4Ok, panelGlosario, subPanel);
                    if (!paso4Ok) fails++;
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        Debug.Log(fails == 0
            ? "\n===== RESULTADO: ✓ El botón INFO del manual muestra el multímetro dentro del glosario y lo cierra todo al presionar INFO de nuevo ====="
            : $"\n===== RESULTADO: ✗ {fails} verificación(es) fallaron =====");

        if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
    }

    static void Log(string paso, bool ok, GameObject panelGlosario, GameObject subPanel)
    {
        string linea = $"[Test INFO] {paso}: panelGlosario.active={panelGlosario.activeSelf} subPanel.active={subPanel.activeSelf}";
        if (ok) Debug.Log(linea + "  ✓");
        else Debug.LogError(linea + "  ✗ INESPERADO");
    }

    static string Truncar(string s) => string.IsNullOrEmpty(s) ? "(vacío)" : s.Replace("\n", " \\n ").Substring(0, Mathf.Min(120, s.Length));
}

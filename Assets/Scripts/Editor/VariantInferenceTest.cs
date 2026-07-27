using UnityEditor;
using UnityEngine;

/// <summary>
/// Verifica el fix del bug "solo funciona el LED rojo": DeskComponent.ResolveVariant() infería la
/// variante del NOMBRE del prefab entregable, y "Delivered" contiene "red" ("delive_RED_") → el LED
/// verde viajaba SIEMPRE como variante roja. Instancia las piezas de la mesa REAL
/// (Technician_Workstation.prefab) y comprueba que cada Comp_LED_*/Comp_Cap_* resuelva su variante.
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod VariantInferenceTest.Run -logFile
/// </summary>
public static class VariantInferenceTest
{
    [MenuItem("Tools/TITA/Diag/Test inferencia de variantes (headless)")]
    public static void Run()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Technician_Workstation.prefab");
        if (prefab == null)
        {
            Debug.LogError("##VAR## No encontré Technician_Workstation.prefab");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        var ws = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var esperados = new (string nombre, ComponentVariant esperado)[]
        {
            ("Comp_LED_Red",      ComponentVariant.LedRed),
            ("Comp_LED_Green",    ComponentVariant.LedGreen),
            ("Comp_LED_Yellow",   ComponentVariant.LedYellow),
            ("Comp_Cap_Blue",     ComponentVariant.CapacitorBlue),
            ("Comp_Cap_Black",    ComponentVariant.CapacitorBlack),
            ("Comp_Cap_Orange",   ComponentVariant.CapacitorOrange),
            ("Comp_R_Vertical",   ComponentVariant.ResistorVertical),
        };

        bool allOk = true;
        foreach (var (nombre, esperado) in esperados)
        {
            DeskComponent pieza = null;
            foreach (var d in ws.GetComponentsInChildren<DeskComponent>(true))
                if (d.name == nombre) { pieza = d; break; }

            if (pieza == null)
            {
                Debug.LogWarning($"##VAR## {nombre}: NO existe en la mesa (se omite)");
                continue;
            }

            var real = pieza.ResolveVariant();
            bool ok = real == esperado;
            if (!ok) allOk = false;
            Debug.Log($"##VAR## {nombre}: esperado={esperado} real={real} {(ok ? "OK" : "✗ FALLA")}");
        }

        Object.DestroyImmediate(ws);
        Debug.Log(allOk
            ? "##VAR## ===== RESULTADO: ✓ todas las variantes se infieren correctamente ====="
            : "##VAR## ===== RESULTADO: ✗ hay variantes mal inferidas =====");
        if (Application.isBatchMode) EditorApplication.Exit(allOk ? 0 : 1);
    }
}

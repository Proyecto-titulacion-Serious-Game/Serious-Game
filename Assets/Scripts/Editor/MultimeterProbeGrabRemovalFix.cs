#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// FIX DEFINITIVO del empujón + "no se puede agarrar el nodo positivo": Probe_Red_Tip y
/// Probe_Black_Tip tienen CADA UNO su propio XRGrabInteractable — compitiendo por el foco de
/// agarre con el CUERPO del multímetro (que es lo único que la mano debería sostener). Ese
/// XRGrabInteractable necesita un collider físico (no-trigger) para funcionar, y ESE collider es
/// el que empuja al jugador cuando el cuerpo (kinematic, sigue la mano rápido) arrastra las puntas.
///
/// El proyecto alternó entre 2 fixes contradictorios en sesiones anteriores:
///   · MultimeterProbeGhostFix   → colliders de la punta 100% trigger (sin empuje, pero "rompía
///     el agarre" según su propio historial)
///   · MultimeterUngrabbableFix  → revertía eso a no-trigger para que las puntas se pudieran
///     agarrar de nuevo (trayendo de vuelta el empuje)
///
/// Las puntas NO necesitan ser agarrables: MultimeterProbe.cs ya asigna el nodo por apuntado+
/// trigger (SphereCastAll, sin física) o por contacto físico (OnTriggerEnter, funciona con
/// isTrigger=true). El XRGrabInteractable de la punta roja compitiendo por el foco de interacción
/// es la causa más probable de "no se puede agarrar/seleccionar el nodo positivo" también.
///
/// Fix real: quitar XRGrabInteractable de AMBAS puntas y dejar sus 2 colliders en trigger — sin
/// física de empuje y sin competencia de agarre, en las dos puntas por igual.
///
/// Ejecutar: Unity.exe -batchmode -quit -projectPath . -executeMethod MultimeterProbeGrabRemovalFix.RunBatch -logFile -
///           Editor: Tools → TITA → Multímetro → Fix definitivo — quitar grab de las puntas
/// </summary>
public static class MultimeterProbeGrabRemovalFix
{
    const string PREFAB_PATH = "Assets/Prefabs/Multimeter_VR_Art.prefab";

    [MenuItem("Tools/TITA/Multímetro/Fix definitivo — quitar grab de las puntas (empujón + nodo rojo)")]
    public static void RunMenu()
    {
        bool ok = Run(out string msg);
        EditorUtility.DisplayDialog("Multímetro — fix definitivo", msg, ok ? "OK" : "Cerrar");
    }

    public static void RunBatch()
    {
        bool ok = Run(out string msg);
        Debug.Log($"[MultimeterProbeGrabRemovalFix] {msg}");
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool Run(out string msg)
    {
        var go = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
        if (go == null) { msg = $"No se pudo cargar {PREFAB_PATH}"; return false; }

        int grabsRemoved = 0, collidersFixed = 0;
        int probesFound = 0;

        foreach (var colorName in new[] { "Red", "Black" })
        {
            var cableT = go.transform.Find($"Cable_{colorName}");
            var probeT = cableT != null ? cableT.Find($"Probe_{colorName}_Tip") : null;
            if (probeT == null) continue;
            probesFound++;

            // Quitar el XRGrabInteractable de la punta — no debe competir con el del cuerpo.
            var grab = probeT.GetComponent<XRGrabInteractable>();
            if (grab != null)
            {
                Object.DestroyImmediate(grab, true);
                grabsRemoved++;
            }

            // Sin XRGrabInteractable, ningún collider de la punta necesita ser físico.
            foreach (var col in probeT.GetComponents<SphereCollider>())
            {
                if (!col.isTrigger)
                {
                    col.isTrigger = true;
                    collidersFixed++;
                }
            }
        }

        PrefabUtility.SaveAsPrefabAsset(go, PREFAB_PATH);
        PrefabUtility.UnloadPrefabContents(go);
        AssetDatabase.Refresh();

        msg = $"Puntas encontradas: {probesFound}/2. XRGrabInteractable quitados: {grabsRemoved}. " +
              $"Colliders pasados a trigger: {collidersFixed}.\n\n" +
              "Las puntas ya no compiten por el agarre con el cuerpo del multímetro ni tienen " +
              "colliders físicos que puedan empujar al jugador. MultimeterProbe.cs sigue asignando " +
              "el nodo por apuntado+trigger o por contacto (ambos funcionan con trigger colliders).";
        return probesFound == 2;
    }
}
#endif

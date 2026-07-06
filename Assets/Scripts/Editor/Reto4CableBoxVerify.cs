using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Verifica (y arregla lo básico de) la CAJA de cables del Reto 4 (<see cref="CableBoxSpawner"/>):
/// al pulsar el botón dispensa un cable AL LADO de la caja, parabólico hacia arriba, que se conecta
/// al Arduino y a los slots del protoboard (igual que los cables ya establecidos del Reto 4).
///
/// Reporta: caja(s) en escena, cablePrefab asignado, y si el prefab del cable trae puntas conectables.
/// Si el cablePrefab está sin asignar, ofrece asignar Cable_Jumper.prefab.
///
/// Menú: Tools → TITA → Reto 4 → Verificar caja de cables
/// </summary>
public static class Reto4CableBoxVerify
{
    const string CableJumperPath = "Assets/Prefabs/Cable_Jumper.prefab";

    [MenuItem("Tools/TITA/Reto 4/Verificar caja de cables")]
    public static void Verificar()
    {
        var boxes = Object.FindObjectsByType<CableBoxSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (boxes.Length == 0)
        {
            EditorUtility.DisplayDialog("Reto 4 — Caja de cables",
                "No hay ningún CableBoxSpawner en la escena.\n\n" +
                "Añade el prefab del dispensador (CableBox_VR) a la zona del Reto 4 en Explorador.unity.", "OK");
            return;
        }

        var sb = new StringBuilder();
        int arreglados = 0;
        foreach (var box in boxes)
        {
            sb.AppendLine($"■ Caja '{box.name}':");

            // 1) cablePrefab
            if (box.cablePrefab == null)
            {
                var jumper = AssetDatabase.LoadAssetAtPath<GameObject>(CableJumperPath);
                if (jumper != null)
                {
                    box.cablePrefab = jumper;
                    EditorUtility.SetDirty(box);
                    arreglados++;
                    sb.AppendLine($"   • cablePrefab: estaba VACÍO → asigné '{jumper.name}'. ✔");
                }
                else sb.AppendLine("   • cablePrefab: ❌ VACÍO y no encontré Cable_Jumper.prefab — asígnalo a mano.");
            }
            else sb.AppendLine($"   • cablePrefab: '{box.cablePrefab.name}' ✔");

            // 2) el prefab trae puntas conectables (Connector) para Arduino/slots
            if (box.cablePrefab != null)
            {
                int conns = box.cablePrefab.GetComponentsInChildren<Component>(true)
                    .Count(c => c != null && c.GetType().Name == "Connector");
                bool tieneVR = box.cablePrefab.GetComponentInChildren<VRCableRenderer>(true) != null;
                sb.AppendLine($"   • puntas conectables (Connector): {conns} {(conns >= 2 ? "✔" : "⚠ deberían ser 2")}");
                sb.AppendLine($"   • VRCableRenderer en el prefab: {(tieneVR ? "sí" : "no (se añade al dispensar — OK)")}");
            }

            // 3) dónde aparece el cable
            sb.AppendLine($"   • spawnOffset (al lado de la caja): {box.spawnOffset} · máx cables: {box.maxActiveCables}");
        }

        sb.AppendLine();
        sb.AppendLine("Al dispensar, MakeCableFlexible fuerza arco PARABÓLICO ARRIBA (straight=false, arcUpward=true)");
        sb.AppendLine("y las puntas se clavan en pines del Arduino y en slots del protoboard.");
        if (arreglados > 0)
            sb.AppendLine($"\nGuarda la escena (Ctrl+S): asigné cablePrefab en {arreglados} caja(s).");

        Debug.Log("[Reto4CableBox] " + sb);
        EditorUtility.DisplayDialog("Reto 4 — Caja de cables", sb.ToString(), "OK");
    }
}

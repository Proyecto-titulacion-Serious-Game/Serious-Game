using UnityEngine;

/// <summary>
/// Tiñe TODA la manguera de un cable físico (todos sus Renderer hijos: segmentos + conectores) con
/// un color, usando MaterialPropertyBlock (sin instanciar materiales → sin fugas). Así cada
/// CableFisico puede tener un color distinto. Lo añade y configura el editor tool al spawnear.
/// </summary>
public class CableColorTint : MonoBehaviour
{
    [Tooltip("Color de toda la manguera del cable.")]
    public Color color = Color.white;

    static readonly int ColorID     = Shader.PropertyToID("_Color");      // built-in / legacy
    static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");  // URP Lit/Unlit

    void Start() => Tint();

    /// <summary>Aplica el color a todos los Renderer del cable vía MaterialPropertyBlock.</summary>
    public void Tint()
    {
        var block = new MaterialPropertyBlock();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor(ColorID, color);
            block.SetColor(BaseColorID, color);
            r.SetPropertyBlock(block);
        }
    }
}

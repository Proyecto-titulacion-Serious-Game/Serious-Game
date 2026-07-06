using System.Collections;
using UnityEngine;

/// <summary>
/// Elimina la AUTO-COLISIÓN de un cable físico (HPhysic PhysicCable): los SphereColliders de los
/// puntos de la cuerda chocan entre sí y la enredan en un "nido". Este script marca cada par de
/// colliders del cable con <see cref="Physics.IgnoreCollision"/>, así el cable YA no colisiona
/// consigo mismo — pero SIGUE colisionando con el mundo (piso/mesa), así que cae y descansa recto.
///
/// Se re-aplica un par de veces al inicio porque PhysicCable crea sus puntos en Start (los clones
/// pueden aparecer después del primer frame).
/// </summary>
public class CableSelfCollisionOff : MonoBehaviour
{
    [Tooltip("Cuántas veces re-aplicar (los puntos de la cuerda se generan en Start, a veces tras 1 frame).")]
    public int reintentos = 3;

    void OnEnable() => StartCoroutine(AplicarVariasVeces());

    IEnumerator AplicarVariasVeces()
    {
        for (int k = 0; k < Mathf.Max(1, reintentos); k++)
        {
            Aplicar();
            yield return null;   // esperar un frame por si se crearon más puntos
        }
    }

    void Aplicar()
    {
        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null) continue;
            for (int j = i + 1; j < cols.Length; j++)
            {
                if (cols[j] == null) continue;
                Physics.IgnoreCollision(cols[i], cols[j], true);
            }
        }
    }
}

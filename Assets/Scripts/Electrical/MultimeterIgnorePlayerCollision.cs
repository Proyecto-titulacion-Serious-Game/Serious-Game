using UnityEngine;

/// <summary>
/// Ignora la colisión física entre el multímetro (cuerpo + ambas puntas) y el CharacterController
/// del jugador. El multímetro es kinematic y lo sostiene la mano de VR — sostenido cerca del
/// cuerpo (como se hace con uno real, para leer la pantalla), su collider puede empujar al
/// CharacterController del jugador. CharacterController hereda de Collider, así que
/// Physics.IgnoreCollision funciona directo contra él, sin tocar capas de física globales.
///
/// Busca colliders desde transform.root (no solo los propios) — el proyecto tiene el multímetro
/// repartido en más de un GameObject/prefab (cuerpo + puntas en jerarquías separadas), así que
/// anclarse solo a los hijos de ESTE GameObject podía dejar colliders hermanos sin cubrir.
/// Reintenta en Update() hasta encontrar el CharacterController, por si el rig del Explorador
/// todavía no existe cuando este Start() corre (orden de carga entre XR Origin y el multímetro).
/// </summary>
public class MultimeterIgnorePlayerCollision : MonoBehaviour
{
    private bool _aplicado;

    void Start() => TryApply();

    void Update()
    {
        if (!_aplicado) TryApply();
    }

    void TryApply()
    {
        var player = FindAnyObjectByType<CharacterController>();
        if (player == null) return;

        // Cubrir el propio root Y cualquier otro GameObject con un Multimeter en la escena: el
        // cuerpo y las puntas pueden vivir en prefabs/raíces distintas (Multimeter_VR vs
        // Multimeter_VR_Art) — no asumir que todos los colliders cuelgan de ESTE transform.root.
        foreach (var col in transform.root.GetComponentsInChildren<Collider>(true))
            Physics.IgnoreCollision(col, player, true);

        foreach (var mult in FindObjectsByType<Multimeter>(FindObjectsInactive.Include))
            foreach (var col in mult.transform.root.GetComponentsInChildren<Collider>(true))
                Physics.IgnoreCollision(col, player, true);

        _aplicado = true;
    }
}

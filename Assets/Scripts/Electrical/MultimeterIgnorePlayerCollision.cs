using UnityEngine;

/// <summary>
/// Ignora la colisión física entre el multímetro (cuerpo + ambas puntas) y el CharacterController
/// del jugador. El multímetro es kinematic y lo sostiene la mano de VR — sostenido cerca del
/// cuerpo (como se hace con uno real, para leer la pantalla), su collider puede empujar al
/// CharacterController del jugador. CharacterController hereda de Collider, así que
/// Physics.IgnoreCollision funciona directo contra él, sin tocar capas de física globales.
/// </summary>
public class MultimeterIgnorePlayerCollision : MonoBehaviour
{
    void Start()
    {
        var player = FindAnyObjectByType<CharacterController>();
        if (player == null) return;

        foreach (var col in GetComponentsInChildren<Collider>(true))
            Physics.IgnoreCollision(col, player, true);
    }
}

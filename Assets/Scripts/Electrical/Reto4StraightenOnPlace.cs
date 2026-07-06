using System.Collections;
using UnityEngine;

/// <summary>
/// RETO 4 — Endereza el componente al soltarlo sobre la protoboard.
///
/// Problema que resuelve: el componente sigue la mano del Explorador con la rotación que tenga
/// el controlador (XRGrabInteractable · Track Rotation). Al soltarlo, quedaba con esa inclinación
/// "torcida" de la mano, así que sus patas (leadA/leadB) no caían rectas en los huecos y el
/// ProtoboardConnector no las enganchaba (ver LeadB "libre" en el log).
///
/// Este script, SOLO durante el Reto 4 (modo protoboard activo), al soltar la pieza la alinea a
/// la cuadrícula de la protoboard: snap a la orientación ortogonal más cercana RELATIVA a la
/// protoboard. Elimina la inclinación de la mano pero respeta hacia dónde la orientó el jugador
/// (no la fuerza a una única pose). Luego la deja quieta (kinematic) para que la gravedad no la
/// tumbe y arruine el enderezado — que es además el estado de reposo documentado del prefab.
///
/// Se auto-engancha desde ExplorerComponentReceiver.ConfigurarComponente (no requiere cablear nada).
/// </summary>
[DisallowMultipleComponent]
public class Reto4StraightenOnPlace : MonoBehaviour
{
    GrabbableComponent _grab;
    Rigidbody          _rb;

    void Awake()
    {
        _grab = GetComponent<GrabbableComponent>();
        _rb   = GetComponent<Rigidbody>();
    }

    void OnEnable()  { if (_grab != null) _grab.Released += OnReleased; }
    void OnDisable() { if (_grab != null) _grab.Released -= OnReleased; }

    void OnReleased(GrabbableComponent _)
    {
        // Solo en Reto 4: Reto4BreadboardMode setea ResistorScaleReto4 mientras el modo protoboard
        // está activo (y lo pone null al salir). Es nuestro flag de "estamos en el Reto 4".
        if (!Reto4BreadboardMode.ResistorScaleReto4.HasValue) return;

        // GrabbableComponent reactiva la gravedad en ESTE mismo frame, justo después de este evento.
        // Diferimos un frame para enderezar y congelar DESPUÉS de eso (si no, lo pisa la gravedad).
        StartCoroutine(StraightenNextFrame());
    }

    IEnumerator StraightenNextFrame()
    {
        yield return null;

        // Referencia de la protoboard: el GameObject que tiene el ProtoboardSimulator.
        var sim = FindAnyObjectByType<ProtoboardSimulator>();
        Quaternion boardRot = sim != null ? sim.transform.rotation : Quaternion.identity;

        // Rotación actual EXPRESADA en el marco de la protoboard, redondeada al múltiplo de 90°
        // más cercano en cada eje → queda alineada a la cuadrícula, sin la inclinación de la mano.
        Quaternion local = Quaternion.Inverse(boardRot) * transform.rotation;
        Vector3 e = local.eulerAngles;
        e.x = Mathf.Round(e.x / 90f) * 90f;
        e.y = Mathf.Round(e.y / 90f) * 90f;
        e.z = Mathf.Round(e.z / 90f) * 90f;
        transform.rotation = boardRot * Quaternion.Euler(e);

        // Dejar la pieza quieta y plana sobre la protoboard (estado de reposo del prefab).
        if (_rb != null)
        {
            _rb.isKinematic     = true;
            _rb.useGravity      = false;
            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }
}

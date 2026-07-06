using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace HPhysic
{
    [RequireComponent(typeof(Connector))]
    [RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))] // Requisito para agarrar en VR
    public class VRCableConnector : MonoBehaviour
    {
        private Connector _connector;
        private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable _grabInteractable;
        
        // Guarda el conector sobre el que tenemos la mano actualmente
        private Connector _hoveredConnector;

        private void Awake()
        {
            _connector = GetComponent<Connector>();
            _grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

            // Suscribimos las funciones a los eventos de agarre de los controles Quest
            _grabInteractable.selectEntered.AddListener(OnGrabbed);
            _grabInteractable.selectExited.AddListener(OnReleased);
        }

        private void OnDestroy()
        {
            // Limpieza de eventos
            _grabInteractable.selectEntered.RemoveListener(OnGrabbed);
            _grabInteractable.selectExited.RemoveListener(OnReleased);
        }

        // Se ejecuta cuando aprietas el gatillo/grip del control para agarrar el cable
        private void OnGrabbed(SelectEnterEventArgs args)
        {
            // Si el cable estaba conectado a un slot del protoboard, lo desconectamos al tirar de él
            if (_connector.ConnectedTo)
            {
                _connector.Disconnect();
            }
        }

        // Se ejecuta cuando sueltas el gatillo/grip del control
        private void OnReleased(SelectExitEventArgs args)
        {
            // Si soltamos el cable y la punta está tocando un conector válido (ej. un Slot del protoboard)
            if (_hoveredConnector != null && _connector.CanConnect(_hoveredConnector))
            {
                // Conectamos la punta del cable al slot
                _hoveredConnector.Connect(_connector);
            }
            else if (_hoveredConnector != null && !_hoveredConnector.IsConnected)
            {
                // Si no se puede conectar por reglas de la clase Connector, al menos lo posicionamos cerca
                transform.rotation = _hoveredConnector.ConnectionRotation * _connector.RotationOffset;
                transform.position = (_hoveredConnector.ConnectionPosition + _hoveredConnector.ConnectedOutOffset * 0.2f) - (_connector.ConnectionPosition - _connector.transform.position);
            }
        }

        // Usamos un Collider en modo Trigger para detectar los slots del protoboard
        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent(out Connector otherConnector))
            {
                _hoveredConnector = otherConnector;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.TryGetComponent(out Connector otherConnector) && _hoveredConnector == otherConnector)
            {
                _hoveredConnector = null; // Limpiamos si alejamos la punta del cable
            }
        }
    }
}
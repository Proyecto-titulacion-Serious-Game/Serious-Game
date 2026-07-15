using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;

namespace HPhysic
{
    [RequireComponent(typeof(Rigidbody))]
    public class Connector : MonoBehaviour
    {
        public enum ConType { Male, Female }
        public enum CableColor { White, Red, Green, Yellow, Blue, Cyan, Magenta }

        [field: Header("Settings")]

        [field: SerializeField] public ConType ConnectionType { get; private set; } = ConType.Male;
        [field: SerializeField, OnValueChanged(nameof(UpdateConnectorColor))] public CableColor ConnectionColor { get; private set; } = CableColor.White;

        [SerializeField] private bool makeConnectionKinematic = false;

        [SerializeField] private bool hideInteractableWhenIsConnected = false;
        [SerializeField] private bool allowConnectDifrentCollor = false;

        [Tooltip("Solo aplica a conectores HEMBRA (slots): si está activo, más de un macho (cable) " +
                 "puede conectarse a la vez en el mismo punto — no se mueve el slot, solo deja de " +
                 "rechazar la segunda conexión (p.ej. 2 cables en el mismo slot del Reto 2). Los " +
                 "conectores macho (puntas de cable) siempre son 1:1, esto no los afecta. " +
                 "Apagado por defecto para no cambiar el comportamiento de otros retos.")]
        [SerializeField] private bool allowMultipleConnections = false;

        [Tooltip("Conexión pre-cableada desde el Inspector (opcional, para cables fijos de fábrica). " +
                 "Se establece de verdad al arrancar (crea el joint); no se usa en runtime.")]
        [SerializeField] private Connector preWiredConnection;

        [Tooltip("Separación visual (m) entre puntas de cable cuando hay más de una conectada al " +
                 "mismo slot (hembra con Allow Multiple Connections). Solo evita que se vean " +
                 "exactamente encimadas — no mueve el slot ni afecta la conexión eléctrica.")]
        [SerializeField] private float multiConnectionOffset = 0.05f;

        // Conexiones activas. Un MACHO nunca tiene más de un elemento (una punta de cable solo va a
        // un sitio). Una HEMBRA puede tener varias si allowMultipleConnections está activo.
        private readonly List<Connector> _connections = new List<Connector>();
        // Joints que ESTE conector creó (solo el lado que llamó Connect() posee el FixedJoint físico).
        private readonly Dictionary<Connector, FixedJoint> _joints = new Dictionary<Connector, FixedJoint>();
        private readonly Dictionary<Connector, bool> _wasKinematicBeforeConnect = new Dictionary<Connector, bool>();

        /// <summary>Primera conexión activa — compat con código que solo mira una punta.</summary>
        public Connector ConnectedTo => _connections.Count > 0 ? _connections[0] : null;
        public IReadOnlyList<Connector> AllConnections => _connections;

        [Header("Object to set")]
        [SerializeField, Required] private Transform connectionPoint;
        [SerializeField] private MeshRenderer collorRenderer;
        [SerializeField] private ParticleSystem sparksParticle;

        public Rigidbody Rigidbody { get; private set; }

        public Vector3 ConnectionPosition => connectionPoint ? connectionPoint.position : transform.position;
        public Quaternion ConnectionRotation => connectionPoint ? connectionPoint.rotation : transform.rotation;
        public Quaternion RotationOffset => connectionPoint ? connectionPoint.localRotation : Quaternion.Euler(Vector3.zero);
        public Vector3 ConnectedOutOffset => connectionPoint ? connectionPoint.right : transform.right;

        public bool IsConnected => _connections.Count > 0;
        public bool IsConnectedRight => IsConnected && ConnectionColor == ConnectedTo.ConnectionColor;

        /// <summary>¿Puede aceptar una conexión más ahora mismo? Las hembras con
        /// <see cref="allowMultipleConnections"/> siempre tienen sitio; el resto, solo si están libres.</summary>
        bool AcceptsMore => !IsConnected || (ConnectionType == ConType.Female && allowMultipleConnections);

        private void Awake()
        {
            Rigidbody = gameObject.GetComponent<Rigidbody>();
        }

        private void Start()
        {
            UpdateConnectorColor();

            if (preWiredConnection != null)
            {
                var t = preWiredConnection;
                preWiredConnection = null;
                Connect(t);
            }
        }

        private void OnDisable() => Disconnect();

        public void Connect(Connector secondConnector)
        {
            if (secondConnector == null)
            {
                Debug.LogWarning("Attempt to connect null");
                return;
            }

            // Si NO hay sitio (macho ya ocupado, o hembra sin multi-conexión), libera la conexión
            // previa antes de tomar la nueva — mismo comportamiento que antes para 1:1.
            if (!AcceptsMore) Disconnect(ConnectedTo);
            if (!secondConnector.AcceptsMore) secondConnector.Disconnect(secondConnector.ConnectedTo);

            secondConnector.transform.rotation = ConnectionRotation * secondConnector.RotationOffset;
            Vector3 basePos = ConnectionPosition - (secondConnector.ConnectionPosition - secondConnector.transform.position);
            // _connections.Count aquí todavía es el conteo ANTES de sumar esta conexión: 0 para el
            // primer cable (sin offset, exacto sobre el slot), 1+ para los siguientes — evita que
            // varias puntas queden exactamente encimadas cuando la hembra admite multi-conexión.
            secondConnector.transform.position = basePos + VisualOffsetFor(_connections.Count);

            var joint = gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = secondConnector.Rigidbody;
            _joints[secondConnector] = joint;
            _connections.Add(secondConnector);

            secondConnector.RegisterPassive(this);

            _wasKinematicBeforeConnect[secondConnector] = secondConnector.Rigidbody.isKinematic;
            if (makeConnectionKinematic)
                secondConnector.Rigidbody.isKinematic = true;

            // sparks on inncretc connection
            if (incorrectSparksC == null && sparksParticle && IsConnected && !IsConnectedRight)
            {
                incorrectSparksC = IncorrectSparks();
                StartCoroutine(incorrectSparksC);
            }

            // disable outline on select
            UpdateInteractableWhenIsConnected();
        }

        /// <summary>Desplazamiento lateral para la conexión número <paramref name="index"/> (0 = primer
        /// cable, sin desplazar). Alterna a cada lado del punto de conexión: 1=+offset, 2=-offset,
        /// 3=+2·offset, 4=-2·offset… así varias puntas se reparten alrededor del slot en vez de
        /// apilarse en el mismo punto exacto.</summary>
        Vector3 VisualOffsetFor(int index)
        {
            if (index <= 0) return Vector3.zero;
            float lado = (index % 2 == 1) ? 1f : -1f;
            int   par  = (index + 1) / 2;
            return ConnectionRotation * Vector3.right * (lado * par * multiConnectionOffset);
        }

        /// <summary>Lado que RECIBE la conexión (no crea el joint físico — ya vive en el otro objeto).</summary>
        void RegisterPassive(Connector other)
        {
            if (!_connections.Contains(other)) _connections.Add(other);
            UpdateInteractableWhenIsConnected();
        }

        /// <summary>Sin argumento: desconecta TODAS las conexiones activas. Con argumento: solo esa.</summary>
        public void Disconnect(Connector onlyThis = null)
        {
            if (_connections.Count == 0) return;

            if (onlyThis == null)
            {
                foreach (var c in new List<Connector>(_connections)) DisconnectFrom(c);
                return;
            }
            DisconnectFrom(onlyThis);
        }

        void DisconnectFrom(Connector other)
        {
            if (other == null || !_connections.Contains(other)) return;

            CleanupJointIfOwned(other);
            _connections.Remove(other);
            other.RemovePassive(this);

            // sparks on inncretc connection
            if (sparksParticle)
            {
                sparksParticle.Stop();
                sparksParticle.Clear();
            }

            UpdateInteractableWhenIsConnected();
        }

        /// <summary>Lado pasivo de una desconexión iniciada por <paramref name="other"/> — sin volver
        /// a llamar a other.Disconnect() (evita recursión infinita). El joint físico puede vivir de
        /// CUALQUIER lado (Connect() lo crea en quien lo llamó, y Connect() se invoca desde ambos
        /// lados según el flujo — hembra.Connect(macho) o macho.Connect(hembra)); si vive aquí, hay
        /// que destruirlo también aquí, o quedaba un FixedJoint fantasma sujetando ambos objetos.</summary>
        void RemovePassive(Connector other)
        {
            if (!_connections.Contains(other)) return;
            CleanupJointIfOwned(other);
            _connections.Remove(other);
            UpdateInteractableWhenIsConnected();
        }

        /// <summary>Destruye el FixedJoint y restaura el kinematic SOLO si este lado fue quien los creó.</summary>
        void CleanupJointIfOwned(Connector other)
        {
            if (!_joints.TryGetValue(other, out var joint)) return;
            if (joint != null) Destroy(joint);
            _joints.Remove(other);
            if (makeConnectionKinematic && _wasKinematicBeforeConnect.TryGetValue(other, out bool wasKinematic))
                other.Rigidbody.isKinematic = wasKinematic;
            _wasKinematicBeforeConnect.Remove(other);
        }

        private void UpdateInteractableWhenIsConnected()
        {
            if (hideInteractableWhenIsConnected)
            {
                if (TryGetComponent(out Collider collider))
                    collider.enabled = !IsConnected;
            }
        }


        private IEnumerator incorrectSparksC;
        private IEnumerator IncorrectSparks()
        {
            while (incorrectSparksC != null && sparksParticle && IsConnected && !IsConnectedRight)
            {
                sparksParticle.Play();

                yield return new WaitForSeconds(Random.Range(0.6f, 0.8f));
            }
            incorrectSparksC = null;
        }

        private void UpdateConnectorColor()
        {
            if (collorRenderer == null)
                return;

            Color color = MaterialColor(ConnectionColor);
            MaterialPropertyBlock probs = new();
            collorRenderer.GetPropertyBlock(probs);
            probs.SetColor("_Color", color);
            collorRenderer.SetPropertyBlock(probs);
        }

        private Color MaterialColor(CableColor cableColor) => cableColor switch
        {
            CableColor.White => Color.white,
            CableColor.Red => Color.red,
            CableColor.Green => Color.green,
            CableColor.Yellow => Color.yellow,
            CableColor.Blue => Color.blue,
            CableColor.Cyan => Color.cyan,
            CableColor.Magenta => Color.magenta,
            _ => Color.clear
        };


        public bool CanConnect(Connector secondConnector) =>
            this != secondConnector
            && this.AcceptsMore && secondConnector.AcceptsMore
            && this.ConnectionType != secondConnector.ConnectionType
            && (this.allowConnectDifrentCollor || secondConnector.allowConnectDifrentCollor || this.ConnectionColor == secondConnector.ConnectionColor);
    }
}

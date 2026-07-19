using UnityEngine;

/// <summary>
/// Dibuja un cable jumper como un ARCO PARABÓLICO entre dos puntas (origin/target).
///
/// La altura del arco crece con la distancia entre puntas (parábola natural) y arquea hacia ARRIBA
/// por defecto, de modo que el cuerpo se levanta del tablero: las puntas quedan más accesibles para
/// agarrar y se ve de un vistazo qué dos puntos une cada cable (no quedan cables planos pisándose).
///
/// El cuerpo visible es el LineRenderer de este GameObject (el cilindro rígido Cable_Body se oculta
/// en CableBoxSpawner.MakeCableFlexible). Usa una Bézier cuadrática = parábola.
/// </summary>
[ExecuteAlways]   // dibuja el arco también en modo edición (no solo en Play) → visible al calibrar
[RequireComponent(typeof(LineRenderer))]
public class VRCableRenderer : MonoBehaviour
{
    public Transform origin;
    public Transform target;

    [Header("Forma del cable (arco parabólico)")]
    [Range(6, 48)] public int segments = 20;
    [Tooltip("Altura del arco por metro de separación entre puntas. Más alto = más curvo.")]
    public float arcPerMeter = 0.4f;
    [Tooltip("Altura mínima del arco aunque las puntas estén casi juntas (m).")]
    public float minArc = 0.02f;
    [Tooltip("Altura máxima del arco (m). Evita arcos gigantes en conexiones largas.")]
    public float maxArc = 0.12f;
    [Tooltip("Arquea hacia arriba (se levanta del tablero, fácil de agarrar y de seguir la conexión). " +
             "Desactívalo si prefieres que el cable cuelgue hacia abajo como cadena.")]
    public bool arcUpward = true;

    [Tooltip("Cable RECTO: línea directa de punta a punta, sin arco/parábola.")]
    public bool straight = false;

    [Tooltip("Transform cuyo eje define la dirección del arco (ver arcAxis). Vacío = arriba de MUNDO " +
             "(comportamiento de siempre, correcto para tableros horizontales como Reto 2/3). Asignar " +
             "el tablero/grupo cuando el jumper vive en una superficie inclinada o vertical (Reto 4 en " +
             "atril), para que el arco se aleje de la superficie en vez de siempre apuntar al techo.")]
    public Transform referenceUp;

    public enum ArcAxis { Up, Forward, Right }

    [Tooltip("Qué eje de referenceUp define la dirección del arco. En un tablero inclinado, 'Up' local " +
             "suele apuntar casi al techo del mundo (arco invisible desde el punto de vista del " +
             "jugador); 'Forward' apunta en profundidad hacia/desde el jugador (el arco se ve casi " +
             "plano por escorzo); 'Right' queda de lado a lado en el campo visual — el que realmente " +
             "se ve como un arco al mirar el tablero de frente. Ignorado si referenceUp es null.")]
    public ArcAxis arcAxis = ArcAxis.Up;

    private LineRenderer _lineRenderer;

    void Awake()    => _lineRenderer = GetComponent<LineRenderer>();
    void OnEnable() { if (_lineRenderer == null) _lineRenderer = GetComponent<LineRenderer>(); }

    void Update()
    {
        if (origin == null || target == null || _lineRenderer == null) return;

        Vector3 start = origin.position;
        Vector3 end   = target.position;

        // Cable RECTO: solo 2 puntos, línea directa.
        if (straight)
        {
            if (_lineRenderer.positionCount != 2) _lineRenderer.positionCount = 2;
            _lineRenderer.SetPosition(0, start);
            _lineRenderer.SetPosition(1, end);
            return;
        }

        // Altura del arco proporcional a la separación → parábola que crece con la distancia.
        float dist = Vector3.Distance(start, end);
        float h    = Mathf.Clamp(dist * arcPerMeter, minArc, maxArc);

        // Control de la Bézier cuadrática. Para que el PICO real del arco sea ~h, el control va a 2h
        // (en una cuadrática el punto medio de la curva está a la mitad de la altura del control).
        Vector3 up = Vector3.up;
        if (referenceUp != null)
        {
            up = arcAxis switch
            {
                ArcAxis.Forward => referenceUp.forward,
                ArcAxis.Right   => referenceUp.right,
                _               => referenceUp.up,
            };
        }
        Vector3 dir  = arcUpward ? up : -up;
        Vector3 ctrl = (start + end) * 0.5f + dir * (2f * h);

        if (_lineRenderer.positionCount != segments) _lineRenderer.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float t = (float)i / (segments - 1);
            _lineRenderer.SetPosition(i, Bezier(start, ctrl, end, t));
        }
    }

    // Bézier cuadrática (parábola) de 3 puntos de control.
    static Vector3 Bezier(Vector3 p0, Vector3 c, Vector3 p1, float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * c + t * t * p1;
    }
}

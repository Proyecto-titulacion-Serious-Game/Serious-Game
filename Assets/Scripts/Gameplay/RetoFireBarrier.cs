using UnityEngine;

/// <summary>
/// Barrera de FUEGO que bloquea la entrada a una zona de reto hasta que se completa el reto
/// ANTERIOR. Mientras está encendida, un collider sólido impide el paso del jugador; al
/// completar el reto previo, el collider se desactiva (deja pasar) y el fuego se apaga.
///
/// Se usa en Reto 2 y Reto 3 (el Reto 4 NO se bloquea). Lo coloca y cablea el editor tool
/// 'Tools → TITA → Explorador → Setup Barreras de Fuego'.
///
///   Reto 2 (Parallel)  → se apaga al completar Reto 1 (OhmLaw).
///   Reto 3 (Mixed)     → se apaga al completar Reto 2 (Parallel).
///
/// Se apaga tanto si el reto previo se GANA como si se le ACABA EL TIEMPO (derrota): en
/// ambos casos GameManager dispara <see cref="GameManager.OnLevelCompleted"/> con el reto
/// terminado — la barrera no debe dejar al grupo atascado solo porque no llegaron a tiempo.
///
/// BUG real corregido: F4 (DebugLevelSkipper) SIEMPRE completa el reto en el GameManager del
/// HOST/Técnico (directo si el Host lo presiona, o vía RPC_SolicitarCompletarReto si lo presiona
/// el Explorador) — nunca en el GameManager del Explorador. OnLevelCompleted es un evento C#
/// LOCAL (no viaja por red), así que la barrera —que vive en Explorador.unity— nunca se enteraba:
/// F4 avanzaba el reto (el índice SÍ se replica por RPC_CambiarReto) pero el muro de fuego seguía
/// encendido. Fix: también escuchar GameManager.OnLevelLoaded (que SÍ corre en el Explorador cada
/// vez que RPC_CambiarReto llega, sea por F4 o por victoria real) y apagar si el nuevo reto ya
/// superó al que bloqueaba esta barrera — cubre ambos caminos con una sola fuente de verdad.
/// </summary>
public class RetoFireBarrier : MonoBehaviour
{
    [Header("Lógica")]
    [Tooltip("Completar ESTE reto apaga la barrera. Ej.: barrera del Reto 2 → OhmLaw (Reto 1).")]
    public LevelType retoPrevioACompletar = LevelType.OhmLaw;

    [Header("Referencias (las cablea el editor tool)")]
    [Tooltip("GameObject del VFX de fuego (ParticleSystem de CartoonFX).")]
    public GameObject fuegoVFX;
    [Tooltip("Collider sólido que bloquea el paso mientras el fuego está encendido.")]
    public Collider barreraCollider;

    [Header("Apagado")]
    [Tooltip("Segundos que tarda el fuego en desvanecerse al apagarse.")]
    public float fadeSeconds = 3f;

    private bool _apagada;

    void OnEnable()
    {
        GameManager.OnLevelCompleted += OnLevelCompleted;
        GameManager.OnLevelLoaded    += OnLevelLoaded;
    }
    void OnDisable()
    {
        GameManager.OnLevelCompleted -= OnLevelCompleted;
        GameManager.OnLevelLoaded    -= OnLevelLoaded;
    }

    void Start()
    {
        // Si al cargar la escena el reto previo YA está superado (p.ej. recarga a mitad de
        // partida), la barrera arranca apagada.
        var gm = FindAnyObjectByType<GameManager>();
        if (gm != null && (int)gm.currentLevel > (int)retoPrevioACompletar)
        {
            ApagarInmediato();
            return;
        }
        SetEncendida(true);
    }

    void OnLevelCompleted(LevelType reto, bool success)
    {
        // Se apaga tanto en éxito como en timeout — ver comentario de clase.
        if (reto == retoPrevioACompletar)
            Apagar();
    }

    /// <summary>Red de seguridad para F4 (ver comentario de clase): OnLevelLoaded SÍ corre en el
    /// Explorador cada vez que el reto avanza por red, sin importar si el avance vino de una
    /// victoria real (que ya dispara OnLevelCompleted arriba) o del atajo de debug F4 (que no).</summary>
    void OnLevelLoaded(LevelType nuevoReto)
    {
        if (!_apagada && (int)nuevoReto > (int)retoPrevioACompletar)
            Apagar();
    }

    /// <summary>Apaga la barrera con desvanecido: deja pasar y disuelve el fuego.</summary>
    public void Apagar()
    {
        if (_apagada) return;
        _apagada = true;

        if (barreraCollider != null) barreraCollider.enabled = false;   // ya se puede pasar

        if (fuegoVFX != null)
        {
            foreach (var ps in fuegoVFX.GetComponentsInChildren<ParticleSystem>())
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);   // deja morir las partículas vivas
            Destroy(fuegoVFX, Mathf.Max(0.1f, fadeSeconds));
        }
        Debug.Log($"[RetoFireBarrier] Barrera apagada (se completó {retoPrevioACompletar}).");
    }

    void ApagarInmediato()
    {
        _apagada = true;
        if (barreraCollider != null) barreraCollider.enabled = false;
        if (fuegoVFX != null) fuegoVFX.SetActive(false);
    }

    void SetEncendida(bool on)
    {
        if (barreraCollider != null) barreraCollider.enabled = on;
        if (fuegoVFX != null) fuegoVFX.SetActive(on);
    }
}

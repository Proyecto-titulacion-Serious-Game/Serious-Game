using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Sistema ambiental de la nave espacial.
/// Conecta el estado eléctrico de cada zona (CircuitManager) con sus efectos visuales:
///   - Normal   → luz cálida estable
///   - Fault    → luz roja parpadeante + partículas de atmósfera
///   - Repaired → luz verde breve, luego vuelve a normal
///   - Blackout → apagón completo (cortocircuito)
///
/// Funciona con cualquier geometría — solo requiere Light[], ParticleSystem[]
/// y un Volume URP opcional asignados en el Inspector por zona.
/// </summary>
public class SpaceshipAmbientSystem : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Configuración por zona
    // ─────────────────────────────────────────────
    [Serializable]
    public class ZoneAmbientConfig
    {
        [Header("Identidad")]
        public LevelType    reto;
        public CircuitManager circuitManager;

        [Header("Luces de la zona")]
        public Light[]          zoneLights;
        public Renderer[]       damagePanels;       // paneles que cambian color según estado
        public ParticleSystem[] atmosphereParticles; // vapor, chispas ambientales

        [Header("Post-Processing (opcional — URP Volume)")]
        public Volume postProcessVolume;

        [Header("Color por estado")]
        public Color colorNormal   = new Color(1.00f, 0.85f, 0.50f); // blanco cálido
        public Color colorFault    = new Color(1.00f, 0.20f, 0.08f); // rojo alerta
        public Color colorRepaired = new Color(0.35f, 1.00f, 0.50f); // verde OK
        public Color colorBlackout = Color.black;

        [Header("Intensidad por estado")]
        public float normalIntensity   = 1.0f;
        public float faultIntensity    = 0.8f;
        public float repairedIntensity = 1.4f;

        [Header("Parpadeo (Fault)")]
        [Range(2f, 30f)] public float flickerSpeed     = 10f;
        [Range(0f, 0.5f)] public float flickerAmplitude = 0.25f;

        [Header("Transición")]
        [Tooltip("Segundos para interpolar entre estados de luz.")]
        public float transitionDuration = 0.6f;
        [Tooltip("Segundos que dura el verde 'Reparado' antes de volver a Normal.")]
        public float repairedDisplayTime = 3f;

        // ── Runtime (no editar en Inspector) ──
        [HideInInspector] public ZoneAmbientState currentState = ZoneAmbientState.Inactive;
        [HideInInspector] public bool             active       = false;
    }

    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Zonas (orden: Reto1, Reto2, Reto3, Reto4)")]
    public ZoneAmbientConfig[] zones = new ZoneAmbientConfig[4];

    [Header("Referencias GameManager")]
    public GameManager gameManager;

    // ─────────────────────────────────────────────
    //  Constantes de material (URP)
    // ─────────────────────────────────────────────
    static readonly int _baseColorID     = Shader.PropertyToID("_BaseColor");
    static readonly int _emissionColorID = Shader.PropertyToID("_EmissionColor");

    // ─────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────
    void Start()
    {
        GameManager.OnZoneActivated      += OnZoneActivated;
        GameManager.OnLevelCompleted     += OnLevelCompleted;
        GameManager.OnFaultDetected      += OnFaultDetected;
        CircuitManager.OnCircuitChanged  += EvaluateCircuitStates;

        AutoDetectCircuitManagers();
        SetAllZonesInactive();
    }

    void OnDestroy()
    {
        GameManager.OnZoneActivated      -= OnZoneActivated;
        GameManager.OnLevelCompleted     -= OnLevelCompleted;
        GameManager.OnFaultDetected      -= OnFaultDetected;
        CircuitManager.OnCircuitChanged  -= EvaluateCircuitStates;
    }

    // ─────────────────────────────────────────────
    //  Handlers de eventos
    // ─────────────────────────────────────────────
    void OnZoneActivated(int zoneIndex)
    {
        for (int i = 0; i < zones.Length; i++)
            zones[i].active = (i == zoneIndex);

        var z = GetZone(zoneIndex);
        if (z != null) TransitionTo(z, ZoneAmbientState.Normal);
    }

    void OnLevelCompleted(LevelType level, bool success)
    {
        var z = GetZoneByLevel(level);
        if (z == null) return;

        if (success)
            StartCoroutine(RepairedThenNormal(z), z);
        else
            TransitionTo(z, ZoneAmbientState.Normal);
    }

    void OnFaultDetected(string _)
    {
        // Parpadeo rápido en la zona activa para feedback de error del jugador
        var z = GetActiveZone();
        if (z != null && z.currentState == ZoneAmbientState.Normal)
            StartCoroutine(QuickFaultFlash(z));
    }

    void EvaluateCircuitStates()
    {
        foreach (var z in zones)
        {
            if (!z.active || z.circuitManager == null) continue;

            // Reto2CircuitGuard (y el cutover equivalente de otros retos rediseñados a protoboard)
            // DESHABILITA el CircuitManager viejo de la zona una vez que el ProtoboardSimulator (MNA)
            // toma el control eléctrico real — pero z.circuitManager sigue apuntando a esa instancia.
            // Un componente deshabilitado deja de correr su Update(), así que isShortCircuited queda
            // CONGELADO en lo último que calculó (a veces "true", si el LED dañado inicial rendía
            // resistencia ~0 en ESE motor obsoleto). Sin este guard, CUALQUIER OnCircuitChanged
            // posterior de OTRA zona (evento estático, dispara para todos los CircuitManager)
            // re-evaluaba este valor viejo y mandaba la zona a Blackout (todas las luces y partículas
            // apagadas) en cualquier momento de la sesión — el protoboard/cables seguían ahí, solo
            // invisibles a oscuras, mientras GameManager.currentLevel nunca se enteraba (no es un
            // cambio de reto real). Bug reportado: "el reto2 desaparece el protoboard y los cables
            // pero seguimos en el reto2".
            if (!z.circuitManager.enabled) continue;

            var cm = z.circuitManager;

            if (cm.isShortCircuited)
            {
                if (z.currentState != ZoneAmbientState.Blackout)
                    TransitionTo(z, ZoneAmbientState.Blackout);
            }
            else if (HasActiveFaults(cm))
            {
                if (z.currentState == ZoneAmbientState.Normal ||
                    z.currentState == ZoneAmbientState.Blackout)
                    TransitionTo(z, ZoneAmbientState.Fault);
            }
            else if (z.currentState == ZoneAmbientState.Blackout ||
                     z.currentState == ZoneAmbientState.Fault)
            {
                TransitionTo(z, ZoneAmbientState.Normal);
            }
        }
    }

    // ─────────────────────────────────────────────
    //  Transiciones de estado
    // ─────────────────────────────────────────────
    void TransitionTo(ZoneAmbientConfig z, ZoneAmbientState newState)
    {
        if (z.currentState == newState) return;

        StopAllCoroutinesForZone(z);
        z.currentState = newState;

        switch (newState)
        {
            case ZoneAmbientState.Normal:
                StartCoroutine(LerpLights(z, z.colorNormal, z.normalIntensity));
                SetAtmosphereParticles(z, false);
                break;

            case ZoneAmbientState.Fault:
                StartCoroutine(FlickerLights(z));
                SetAtmosphereParticles(z, true);
                break;

            case ZoneAmbientState.Repaired:
                // Gestionado por RepairedThenNormal
                break;

            case ZoneAmbientState.Blackout:
                StartCoroutine(LerpLights(z, z.colorBlackout, 0f, duration: 0.3f));
                SetAtmosphereParticles(z, false);
                break;

            case ZoneAmbientState.Inactive:
                SetLightsImmediate(z, z.colorBlackout, 0f);
                SetAtmosphereParticles(z, false);
                break;
        }

        SetDamagePanelColor(z, GetDamagePanelColor(z, newState));
        SetPostProcessWeight(z, GetPostProcessWeight(newState));
    }

    // ─────────────────────────────────────────────
    //  Corrutinas de luz
    // ─────────────────────────────────────────────
    IEnumerator LerpLights(ZoneAmbientConfig z, Color targetColor, float targetIntensity,
        float duration = -1f)
    {
        float t   = 0f;
        float dur = duration > 0f ? duration : z.transitionDuration;

        Color[] startColors     = GetLightColors(z);
        float[] startIntensities = GetLightIntensities(z);

        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float eased = Mathf.SmoothStep(0f, 1f, t);

            for (int i = 0; i < z.zoneLights.Length; i++)
            {
                if (z.zoneLights[i] == null) continue;
                z.zoneLights[i].color     = Color.Lerp(startColors[i], targetColor, eased);
                z.zoneLights[i].intensity = Mathf.Lerp(startIntensities[i], targetIntensity, eased);
            }
            yield return null;
        }

        SetLightsImmediate(z, targetColor, targetIntensity);
    }

    IEnumerator FlickerLights(ZoneAmbientConfig z)
    {
        // Transición suave hacia color falla
        yield return StartCoroutine(LerpLights(z, z.colorFault, z.faultIntensity));

        // Parpadeo continuo mientras el estado sea Fault
        while (z.currentState == ZoneAmbientState.Fault)
        {
            float flicker = 1f + Mathf.Sin(Time.time * z.flickerSpeed) * z.flickerAmplitude
                              + Mathf.Sin(Time.time * z.flickerSpeed * 2.3f) * z.flickerAmplitude * 0.4f;

            for (int i = 0; i < z.zoneLights.Length; i++)
            {
                if (z.zoneLights[i] == null) continue;
                z.zoneLights[i].intensity = z.faultIntensity * flicker;
            }
            yield return null;
        }
    }

    IEnumerator RepairedThenNormal(ZoneAmbientConfig z)
    {
        z.currentState = ZoneAmbientState.Repaired;
        SetAtmosphereParticles(z, false);
        SetDamagePanelColor(z, z.colorRepaired);
        SetPostProcessWeight(z, 0f);

        yield return StartCoroutine(LerpLights(z, z.colorRepaired, z.repairedIntensity));
        yield return new WaitForSeconds(z.repairedDisplayTime);

        if (z.currentState == ZoneAmbientState.Repaired)
            TransitionTo(z, ZoneAmbientState.Normal);
    }

    IEnumerator QuickFaultFlash(ZoneAmbientConfig z)
    {
        // Flash rojo breve sin cambiar el estado real
        Color[] orig     = GetLightColors(z);
        float[] origInts = GetLightIntensities(z);

        SetLightsImmediate(z, z.colorFault, z.faultIntensity * 1.5f);
        yield return new WaitForSeconds(0.12f);
        SetLightsImmediate(z, orig, origInts);
    }

    // ─────────────────────────────────────────────
    //  Helpers de luz
    // ─────────────────────────────────────────────
    void SetLightsImmediate(ZoneAmbientConfig z, Color c, float intensity)
    {
        foreach (var light in z.zoneLights)
        {
            if (light == null) continue;
            light.color     = c;
            light.intensity = intensity;
        }
    }

    void SetLightsImmediate(ZoneAmbientConfig z, Color[] colors, float[] intensities)
    {
        for (int i = 0; i < z.zoneLights.Length; i++)
        {
            if (z.zoneLights[i] == null) continue;
            z.zoneLights[i].color     = colors[i];
            z.zoneLights[i].intensity = intensities[i];
        }
    }

    Color[] GetLightColors(ZoneAmbientConfig z)
    {
        var result = new Color[z.zoneLights.Length];
        for (int i = 0; i < z.zoneLights.Length; i++)
            result[i] = z.zoneLights[i] != null ? z.zoneLights[i].color : Color.black;
        return result;
    }

    float[] GetLightIntensities(ZoneAmbientConfig z)
    {
        var result = new float[z.zoneLights.Length];
        for (int i = 0; i < z.zoneLights.Length; i++)
            result[i] = z.zoneLights[i] != null ? z.zoneLights[i].intensity : 0f;
        return result;
    }

    // ─────────────────────────────────────────────
    //  Paneles de daño y post-process
    // ─────────────────────────────────────────────
    void SetDamagePanelColor(ZoneAmbientConfig z, Color c)
    {
        if (z.damagePanels == null) return;
        foreach (var r in z.damagePanels)
        {
            if (r == null) continue;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(_baseColorID,     c);
            mpb.SetColor(_emissionColorID, c * 1.5f);
            r.SetPropertyBlock(mpb);
        }
    }

    Color GetDamagePanelColor(ZoneAmbientConfig z, ZoneAmbientState state) => state switch
    {
        ZoneAmbientState.Fault    => z.colorFault,
        ZoneAmbientState.Repaired => z.colorRepaired,
        ZoneAmbientState.Blackout => z.colorBlackout,
        _                         => z.colorNormal,
    };

    void SetPostProcessWeight(ZoneAmbientConfig z, float weight)
    {
        if (z.postProcessVolume != null)
            z.postProcessVolume.weight = weight;
    }

    float GetPostProcessWeight(ZoneAmbientState state) => state switch
    {
        ZoneAmbientState.Fault    => 0.7f,
        ZoneAmbientState.Blackout => 1.0f,
        ZoneAmbientState.Repaired => 0.2f,
        _                         => 0.0f,
    };

    // ─────────────────────────────────────────────
    //  Partículas
    // ─────────────────────────────────────────────
    void SetAtmosphereParticles(ZoneAmbientConfig z, bool play)
    {
        if (z.atmosphereParticles == null) return;
        foreach (var ps in z.atmosphereParticles)
        {
            if (ps == null) continue;
            if (play && !ps.isPlaying) ps.Play();
            else if (!play && ps.isPlaying) ps.Stop();
        }
    }

    // ─────────────────────────────────────────────
    //  Detección de fallas en circuito
    // ─────────────────────────────────────────────
    bool HasActiveFaults(CircuitManager cm)
    {
        foreach (var comp in cm.components)
        {
            if (comp is Resistor r && r.hasFault)   return true;
            if (comp is LED led && led.polarityInverted) return true;
            if (comp is Capacitor cap && cap.polarityInverted) return true;
            if (comp is VoltageSource vs && vs.hasFault) return true;
            if (comp is ArduinoPin pin && (pin.hasFault || pin.hasLooseCable)) return true;
            if (comp.isOpenCircuit) return true;
        }
        return false;
    }

    // ─────────────────────────────────────────────
    //  Gestión de corrutinas por zona
    // ─────────────────────────────────────────────
    // Guardamos la corrutina activa por índice para poder cancelarla
    Coroutine[] _zoneCoroutines = new Coroutine[4];

    void StopAllCoroutinesForZone(ZoneAmbientConfig z)
    {
        int idx = Array.IndexOf(zones, z);
        if (idx < 0 || idx >= _zoneCoroutines.Length) return;
        if (_zoneCoroutines[idx] != null)
        {
            StopCoroutine(_zoneCoroutines[idx]);
            _zoneCoroutines[idx] = null;
        }
    }

    // Override StartCoroutine para rastrear por zona
    Coroutine StartCoroutine(IEnumerator routine, ZoneAmbientConfig z)
    {
        int idx = Array.IndexOf(zones, z);
        var cr = base.StartCoroutine(routine);
        if (idx >= 0 && idx < _zoneCoroutines.Length)
            _zoneCoroutines[idx] = cr;
        return cr;
    }

    // Sobrecarga sin zona para corrutinas helper (LerpLights anidado)
    new Coroutine StartCoroutine(IEnumerator routine) => base.StartCoroutine(routine);

    // ─────────────────────────────────────────────
    //  Utilidades
    // ─────────────────────────────────────────────
    ZoneAmbientConfig GetZone(int index)
        => index >= 0 && index < zones.Length ? zones[index] : null;

    ZoneAmbientConfig GetZoneByLevel(LevelType level)
    {
        foreach (var z in zones)
            if (z.reto == level) return z;
        return null;
    }

    ZoneAmbientConfig GetActiveZone()
    {
        foreach (var z in zones)
            if (z.active) return z;
        return null;
    }

    void AutoDetectCircuitManagers()
    {
        // Asocia cada zona con el CircuitManager de su GameObject en GameManager
        if (gameManager == null) return;

        GameObject[] zoneGOs =
        {
            gameManager.reto1Zone, gameManager.reto2Zone,
            gameManager.reto3Zone, gameManager.reto4Zone
        };

        LevelType[] levels = { LevelType.OhmLaw, LevelType.Parallel, LevelType.Mixed, LevelType.Arduino };

        for (int i = 0; i < zones.Length; i++)
        {
            if (zones[i].reto != levels[i])
                zones[i].reto = levels[i];

            if (zones[i].circuitManager != null) continue;
            if (i >= zoneGOs.Length || zoneGOs[i] == null) continue;

            zones[i].circuitManager = zoneGOs[i].GetComponentInChildren<CircuitManager>(true);
        }
    }

    void SetAllZonesInactive()
    {
        foreach (var z in zones)
            TransitionTo(z, ZoneAmbientState.Inactive);
    }
}

public enum ZoneAmbientState { Inactive, Normal, Fault, Repaired, Blackout }

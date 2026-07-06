using UnityEngine;

/// <summary>
/// Efecto visual TEMÁTICO por reto: al entrar a cada reto lanza un "pulso de bienvenida"
/// (anillo de partículas) con un color propio, para que cada reto se sienta distinto.
///   Reto 1 (OhmLaw, serie)      → azul
///   Reto 2 (Parallel)           → cian
///   Reto 3 (Mixed, polaridad)   → naranja
///   Reto 4 (Arduino)            → verde Arduino
///
/// AUTO-BOOTSTRAP: no hace falta ponerlo en la escena. Escucha GameManager.OnLevelLoaded.
/// Es puramente cosmético y de bajo acoplamiento: si molesta, borra este archivo y nada más se rompe.
/// </summary>
public class RetoThematicFX : MonoBehaviour
{
    static RetoThematicFX _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("[RetoThematicFX]");
        _instance = go.AddComponent<RetoThematicFX>();
        DontDestroyOnLoad(go);
    }

    static readonly Color[] ColorPorReto =
    {
        new Color(0.30f, 0.55f, 1.00f),  // Reto 1 — azul
        new Color(0.20f, 0.90f, 0.95f),  // Reto 2 — cian
        new Color(1.00f, 0.55f, 0.15f),  // Reto 3 — naranja
        new Color(0.25f, 0.95f, 0.45f),  // Reto 4 — verde Arduino
    };

    void OnEnable()  => GameManager.OnLevelLoaded += OnLevelLoaded;
    void OnDisable() => GameManager.OnLevelLoaded -= OnLevelLoaded;

    void OnLevelLoaded(LevelType level)
    {
        int idx = Mathf.Clamp((int)level, 0, ColorPorReto.Length - 1);
        SpawnPulso(PosicionCircuito(), ColorPorReto[idx]);
    }

    Vector3 PosicionCircuito()
    {
        var src = FindAnyObjectByType<VoltageSource>();
        if (src != null) return src.transform.position + Vector3.up * 0.05f;
        var cm = FindAnyObjectByType<CircuitManager>();
        if (cm != null) return cm.transform.position + Vector3.up * 0.05f;
        var cam = Camera.main;
        return cam != null ? cam.transform.position + cam.transform.forward * 0.9f : Vector3.zero;
    }

    void SpawnPulso(Vector3 pos, Color color)
    {
        var go = new GameObject("RetoPulse_VFX");
        go.transform.position = pos;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration        = 1f;
        main.loop            = false;
        main.playOnAwake     = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);  // se expande
        main.startSize       = new ParticleSystem.MinMaxCurve(0.01f, 0.03f);
        main.startColor      = color;
        main.gravityModifier = 0f;
        main.maxParticles    = 120;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.enabled      = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 90) });

        // Anillo horizontal que se abre como una onda.
        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.04f;
        shape.arc       = 360f;
        shape.rotation  = new Vector3(90f, 0f, 0f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));

        var rend = go.GetComponent<ParticleSystemRenderer>();
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
              ?? Shader.Find("Particles/Standard Unlit")
              ?? Shader.Find("Sprites/Default");
        if (sh != null)
        {
            var mat = new Material(sh);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // transparente
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend",   1f); // additive
            mat.renderQueue = 3000;
            rend.material = mat;
        }

        ps.Play();
        Destroy(go, 2f);
    }
}

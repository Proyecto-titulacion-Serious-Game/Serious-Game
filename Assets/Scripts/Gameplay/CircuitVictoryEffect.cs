using UnityEngine;

/// <summary>
/// Efecto especial al COMPLETAR un reto: un estallido de partículas verdes en cada LED encendido
/// (o en el centro del circuito si no hay LEDs). Refuerzo visual de "¡circuito correcto!".
///
/// Auto-bootstrap: no requiere ponerlo en la escena. Escucha GameManager.OnLevelCompleted.
/// </summary>
public class CircuitVictoryEffect : MonoBehaviour
{
    static CircuitVictoryEffect _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("[CircuitVictoryEffect]");
        _instance = go.AddComponent<CircuitVictoryEffect>();
        DontDestroyOnLoad(go);
    }

    void OnEnable()  => GameManager.OnLevelCompleted += OnLevelCompleted;

    void OnDisable()
    {
        GameManager.OnLevelCompleted -= OnLevelCompleted;
        LED.BoostVictoria = 1f;   // nunca dejar el boost atascado
    }

    void OnLevelCompleted(LevelType level, bool success)
    {
        if (!success) return;

        // Flash de LUZ: todos los LEDs encendidos LATEN con emisión intensa ~3 s — se ve
        // claramente que el circuito funcionó (complementa el estallido de partículas).
        StopAllCoroutines();
        StartCoroutine(FlashDeVictoria(3f));

        bool alguno = false;
        foreach (var led in FindObjectsByType<LED>(FindObjectsInactive.Exclude))
        {
            if (led == null || !led.isOn) continue;
            SpawnBurst(led.transform.position);
            alguno = true;
        }

        if (!alguno)
        {
            var cm = FindAnyObjectByType<CircuitManager>();
            if (cm != null) SpawnBurst(cm.transform.position);
        }

        Debug.Log("[CircuitVictoryEffect] ¡Estallido de victoria!");
    }

    /// <summary>Late la emisión de todos los LEDs encendidos: ~4 pulsos fuertes que decaen a normal.</summary>
    System.Collections.IEnumerator FlashDeVictoria(float duracion)
    {
        float t = 0f;
        while (t < duracion)
        {
            t += Time.deltaTime;
            float decae  = 1f - (t / duracion);                        // el efecto se apaga gradualmente
            float latido = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2.6f));  // ~4 latidos en 3 s
            LED.BoostVictoria = 1f + 3.5f * latido * decae;            // pico ~4.5x el brillo normal
            yield return null;
        }
        LED.BoostVictoria = 1f;
    }

    void SpawnBurst(Vector3 pos)
    {
        var go = new GameObject("VictoryBurst");
        go.transform.position = pos;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration        = 1.2f;
        main.loop            = false;
        main.playOnAwake     = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.4f, 1.3f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.008f, 0.025f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   new Color(0.3f, 1f, 0.4f), new Color(0.7f, 1f, 0.7f));
        main.gravityModifier = -0.04f;   // suben suave
        main.maxParticles    = 90;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.enabled      = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 70) });

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.03f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(0.5f, 1f, 0.6f), 0f),
                    new GradientColorKey(new Color(0.2f, 0.9f, 0.3f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var rend = go.GetComponent<ParticleSystemRenderer>();
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
              ?? Shader.Find("Particles/Standard Unlit")
              ?? Shader.Find("Sprites/Default");
        if (sh != null) rend.material = new Material(sh);

        ps.Play();
        Destroy(go, 2.5f);
    }
}

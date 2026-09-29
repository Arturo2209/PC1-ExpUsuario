using UnityEngine;

/// <summary>Hace parpadear una luz como si fuera fuego.</summary>
[RequireComponent(typeof(Light))]
public class Parpadeo : MonoBehaviour
{
    public float intensidadBase = 3f;
    public float variacion = 1f;
    public float rapidez = 8f;

    Light luz;
    float semilla;

    void Awake()
    {
        luz = GetComponent<Light>();
        semilla = Random.value * 100f;
    }

    void Update()
    {
        luz.intensity = intensidadBase + (Mathf.PerlinNoise(semilla, Time.time * rapidez) - 0.5f) * 2f * variacion;
    }
}

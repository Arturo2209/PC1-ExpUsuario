using UnityEngine;

/// <summary>Arbusto seco (tumbleweed) que rueda empujado por el viento.</summary>
public class RodarArbusto : MonoBehaviour
{
    public Vector3 viento = new Vector3(1f, 0f, 0.3f);
    public float velocidad = 3f;
    public float radio = 0.45f;
    public float recorrido = 80f;

    Vector3 inicio;
    Terrain terreno;

    void Start()
    {
        inicio = transform.position;
        terreno = Terrain.activeTerrain;
        viento.y = 0f;
        viento.Normalize();
    }

    void Update()
    {
        Vector3 p = transform.position + viento * velocidad * Time.deltaTime;
        Vector2 desde = new Vector2(inicio.x, inicio.z);
        if (Vector2.Distance(new Vector2(p.x, p.z), desde) > recorrido) p = inicio;

        float suelo = terreno != null ? terreno.SampleHeight(p) + terreno.transform.position.y : inicio.y - radio;
        p.y = suelo + radio + Mathf.Abs(Mathf.Sin(Time.time * 2.5f + inicio.x)) * 0.5f;
        transform.position = p;

        Vector3 eje = Vector3.Cross(Vector3.up, viento);
        transform.Rotate(eje, velocidad * Time.deltaTime / radio * Mathf.Rad2Deg, Space.World);
    }
}

using UnityEngine;

/// <summary>Vuelo en círculos (buitres sobre el desierto).</summary>
public class VueloCircular : MonoBehaviour
{
    public Vector3 centro;
    public float radio = 25f;
    public float velocidad = 0.25f;
    public float fase;
    public float inclinacion = 20f;

    void Update()
    {
        Colocar(Time.time);
    }

    /// <summary>Posiciona el ave en el instante t (también se usa desde el editor).</summary>
    public void Colocar(float t)
    {
        float a = fase + t * velocidad;
        Vector3 p = centro + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radio
                    + Vector3.up * Mathf.Sin(t * 0.7f + fase) * 1.5f;
        Vector3 dir = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
        transform.position = p;
        transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, inclinacion);
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controlador en primera persona para recorrer el escenario.
/// WASD mover, Shift correr, Espacio saltar, clic para capturar el mouse, Esc para soltarlo.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PrimeraPersona : MonoBehaviour
{
    public Transform camara;
    public float velocidad = 6f;
    public float velocidadCorrer = 12f;
    public float sensibilidad = 0.12f;
    public float alturaSalto = 1.4f;
    public float gravedad = -20f;

    CharacterController cc;
    float pitch;
    float velY;

    void Start()
    {
        cc = GetComponent<CharacterController>();
        if (camara == null)
        {
            var cam = GetComponentInChildren<Camera>();
            if (cam != null) camara = cam.transform;
        }
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (kb.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Mirar
        if (Cursor.lockState == CursorLockMode.Locked && camara != null)
        {
            Vector2 d = mouse.delta.ReadValue() * sensibilidad;
            transform.Rotate(0f, d.x, 0f);
            pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
            camara.localEulerAngles = new Vector3(pitch, 0f, 0f);
        }

        // Mover
        Vector2 mov = Vector2.zero;
        if (kb.wKey.isPressed) mov.y += 1;
        if (kb.sKey.isPressed) mov.y -= 1;
        if (kb.dKey.isPressed) mov.x += 1;
        if (kb.aKey.isPressed) mov.x -= 1;
        if (mov.sqrMagnitude > 1) mov.Normalize();

        float v = kb.leftShiftKey.isPressed ? velocidadCorrer : velocidad;
        Vector3 dir = (transform.right * mov.x + transform.forward * mov.y) * v;

        if (cc.isGrounded)
        {
            velY = -1f;
            if (kb.spaceKey.wasPressedThisFrame) velY = Mathf.Sqrt(alturaSalto * -2f * gravedad);
        }
        velY += gravedad * Time.deltaTime;
        dir.y = velY;
        cc.Move(dir * Time.deltaTime);
    }
}

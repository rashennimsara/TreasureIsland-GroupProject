
using UnityEngine;

public class PlayerMouseLook : MonoBehaviour
{
    public float sensitivity = 2f;
    private float pitch;

    void Start()
    {
        pitch = transform.localEulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
    }

    void Update()
    {
        float mouseX = Input.GetAxis("Mouse X") * sensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity;

        transform.parent.Rotate(0f, mouseX, 0f);

        pitch = Mathf.Clamp(pitch - mouseY, -65f, 70f);
        transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }
}

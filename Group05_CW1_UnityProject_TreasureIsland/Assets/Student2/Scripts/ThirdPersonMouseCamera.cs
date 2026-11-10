
using UnityEngine;

public class ThirdPersonMouseCamera : MonoBehaviour
{
    [Header("Follow Target")]
    public Transform target;

    [Header("Camera Settings")]
    public float distance = 5f;
    public float height = 1.5f;
    public float mouseSensitivity = 2f;

    [Header("Vertical Rotation Limits")]
    public float minimumPitch = -20f;
    public float maximumPitch = 60f;

    private float yaw;
    private float pitch = 15f;

    void Start()
    {
        if (target != null)
        {
            yaw = target.eulerAngles.y;
        }
    }

    void LateUpdate()
    {
        if (target == null)
            return;

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            yaw += mouseX * mouseSensitivity;
            pitch -= mouseY * mouseSensitivity;

            pitch = Mathf.Clamp(
                pitch,
                minimumPitch,
                maximumPitch
            );
        }

        Quaternion rotation =
            Quaternion.Euler(pitch, yaw, 0f);

        Vector3 focusPoint =
            target.position + Vector3.up * height;

        transform.position =
            focusPoint -
            rotation * Vector3.forward * distance;

        transform.rotation = rotation;
    }
}

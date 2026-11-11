using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MouseInput : MonoBehaviour
{
    public float speed = 5f;

    void Update()
    {

    }

    void OnMouseDrag()
    {
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        float moveX = mouseX * speed * Time.deltaTime;
        float moveZ = mouseY * speed * Time.deltaTime;

        transform.Translate(moveX, 0f, moveZ, Space.World);
    }
}

// From: https://docs.unity3d.com/ScriptReference/Input.GetAxis.html
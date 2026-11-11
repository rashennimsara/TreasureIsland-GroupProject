
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class CaveTrigger : MonoBehaviour
{
    [Header("References")]
    public Transform playerRoot;
    public WallExplosion wallExplosion;
    public TMP_Text interactionText;

    private HashSet<Collider> playerColliders =
        new HashSet<Collider>();

    private bool activated = false;

    private void Start()
    {
        if (interactionText != null)
        {
            interactionText.text =
                "Press E to break the wall";

            interactionText.gameObject.SetActive(false);
        }
    }

    private bool IsPlayer(Collider other)
    {
        if (playerRoot != null)
        {
            return other.transform == playerRoot ||
                   other.transform.IsChildOf(playerRoot);
        }

        return other.CompareTag("Player") ||
               (other.attachedRigidbody != null &&
                other.attachedRigidbody.CompareTag("Player"));
    }

    private void OnTriggerEnter(Collider other)
    {
        if (activated || !IsPlayer(other))
            return;

        playerColliders.Add(other);

        if (interactionText != null)
        {
            interactionText.gameObject.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other))
            return;

        playerColliders.Remove(other);

        if (playerColliders.Count == 0 &&
            interactionText != null)
        {
            interactionText.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (activated || playerColliders.Count == 0)
            return;

        if (Keyboard.current == null)
            return;

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (wallExplosion == null)
            {
                Debug.LogWarning(
                    "Wall Explosion is not assigned!"
                );
                return;
            }

            wallExplosion.Explode();
            activated = true;

            if (interactionText != null)
            {
                interactionText.gameObject.SetActive(false);
            }
        }
    }
}

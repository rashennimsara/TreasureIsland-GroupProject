
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class TreasureChestTrigger : MonoBehaviour
{
    [Header("Player")]
    public Transform playerRoot;

    [Header("Chest Animation")]
    public Animator chestAnimator;
    public string openStateName = "Base Layer.ChestOpen";
    public float openingDuration = 1.5f;

    [Header("Treasure")]
    public GameObject chestCoins;

    [Header("UI")]
    public TMP_Text chestPromptText;
    public TMP_Text rewardText;

    private HashSet<Collider> nearbyPlayerColliders =
        new HashSet<Collider>();

    private bool opened = false;

    private void Start()
    {
        if (chestPromptText != null)
        {
            chestPromptText.text = "Press E to open chest";
            chestPromptText.gameObject.SetActive(false);
        }

        if (rewardText != null)
        {
            rewardText.text =
                "Congratulations! You found the treasure!";
            rewardText.gameObject.SetActive(false);
        }

        if (chestCoins != null)
        {
            chestCoins.SetActive(false);
        }
    }

    private bool IsPlayer(Collider other)
    {
        if (playerRoot == null)
            return false;

        if (other.transform == playerRoot ||
            other.transform.IsChildOf(playerRoot))
        {
            return true;
        }

        return other.attachedRigidbody != null &&
               other.attachedRigidbody.transform == playerRoot;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (opened || !IsPlayer(other))
            return;

        nearbyPlayerColliders.Add(other);

        if (chestPromptText != null)
        {
            chestPromptText.gameObject.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other))
            return;

        nearbyPlayerColliders.Remove(other);

        if (nearbyPlayerColliders.Count == 0 &&
            chestPromptText != null)
        {
            chestPromptText.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (opened || nearbyPlayerColliders.Count == 0)
            return;

        if (Keyboard.current == null)
            return;

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (chestAnimator == null)
            {
                Debug.LogWarning(
                    "Chest Animator is not assigned!");
                return;
            }

            int stateHash = Animator.StringToHash(openStateName);

            if (!chestAnimator.HasState(0, stateHash))
            {
                Debug.LogWarning(
                    "Chest opening state not found: " + openStateName);
                return;
            }

            StartCoroutine(OpenChest());
        }
    }

    private IEnumerator OpenChest()
    {
        opened = true;

        if (chestPromptText != null)
        {
            chestPromptText.gameObject.SetActive(false);
        }

        chestAnimator.Play(openStateName, 0, 0f);

        yield return new WaitForSeconds(openingDuration);

        if (chestCoins != null)
        {
            chestCoins.SetActive(true);
        }

        if (rewardText != null)
        {
            rewardText.gameObject.SetActive(true);
        }

        Debug.Log("Treasure chest opened!");
    }
}

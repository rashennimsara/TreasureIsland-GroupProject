
using UnityEngine;

public class CaveTrigger : MonoBehaviour
{
    public WallExplosion wallExplosion;
    private bool activated = false;

    private void OnTriggerEnter(Collider other)
    {
        if (activated) return;

        if (other.CompareTag("Player"))
        {
            activated = true;

            Debug.Log("Player entered the cave trigger!");

            if (wallExplosion != null)
            {
                wallExplosion.Explode();
            }
        }
    }
}

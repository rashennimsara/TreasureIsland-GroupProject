
using UnityEngine;

public class WallExplosion : MonoBehaviour
{
    [Header("Explosion Settings")]
    public float explosionForce = 5f;
    public float spinForce = 2f;

    private Rigidbody[] stones;
    private bool exploded = false;

    private void Awake()
    {
        stones = GetComponentsInChildren<Rigidbody>(true);

        foreach (Rigidbody stone in stones)
        {
            stone.isKinematic = true;
            stone.useGravity = true;
        }
    }

    public void Explode()
    {
        if (exploded) return;

        if (stones.Length == 0)
        {
            Debug.LogWarning("No stone Rigidbodies found!");
            return;
        }

        exploded = true;

        Vector3 explosionCenter = Vector3.zero;

        foreach (Rigidbody stone in stones)
        {
            explosionCenter += stone.worldCenterOfMass;
        }

        explosionCenter /= stones.Length;

        foreach (Rigidbody stone in stones)
        {
            stone.isKinematic = false;
            stone.useGravity = true;
            stone.WakeUp();

            Vector3 direction =
                stone.worldCenterOfMass - explosionCenter;

            direction.y += 0.6f;

            if (direction.sqrMagnitude < 0.01f)
            {
                direction = new Vector3(
                    Random.Range(-1f, 1f),
                    1f,
                    Random.Range(-1f, 1f)
                );
            }

            stone.AddForce(
                direction.normalized * explosionForce,
                ForceMode.Impulse
            );

            stone.AddTorque(
                Random.insideUnitSphere * spinForce,
                ForceMode.Impulse
            );
        }

        Debug.Log("Wall pieces scattered: " + stones.Length);
    }
}

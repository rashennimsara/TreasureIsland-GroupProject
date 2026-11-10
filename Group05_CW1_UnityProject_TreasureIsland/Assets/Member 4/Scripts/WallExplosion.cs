
using UnityEngine;

public class WallExplosion : MonoBehaviour
{
    public float explosionForce = 12f;
    public float explosionRadius = 5f;

    private Rigidbody[] stones;

    void Start()
    {
        stones = GetComponentsInChildren<Rigidbody>();
    }

    public void Explode()
    {
        Vector3 explosionCenter = transform.position;

        foreach (Rigidbody stone in stones)
        {
            stone.isKinematic = false;

            stone.AddExplosionForce(
                explosionForce,
                explosionCenter,
                explosionRadius,
                1f,
                ForceMode.Impulse
            );
        }
    }
}

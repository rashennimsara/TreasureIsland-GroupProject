using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class TestPlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float turnSpeed = 10f;
    public float airControl = 0.6f;
    public Transform cameraTransform;

    [Header("Jumping")]
    public float jumpSpeed = 6f;
    public float groundCheckDistance = 0.2f;
    public float coyoteTime = 0.12f;
    public float jumpBufferTime = 0.15f;

    [Header("Animation")]
    public Animator anim;

    private Rigidbody rb;
    private CapsuleCollider capsule;
    private Vector2 moveInput;

    private float lastGroundedTime = -100f;
    private float lastJumpPressedTime = -100f;
    private float jumpLockoutUntil = -100f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();

        // Automatically finds the Animator on TestPlayer or any child object inside it
        if (anim == null)
        {
            anim = GetComponentInChildren<Animator>();
        }

        rb.useGravity = true;
        rb.isKinematic = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            moveInput = Vector2.zero;
            return;
        }

        float x = 0f;
        float y = 0f;

        if (keyboard.aKey.isPressed) x -= 1f;
        if (keyboard.dKey.isPressed) x += 1f;
        if (keyboard.wKey.isPressed) y += 1f;
        if (keyboard.sKey.isPressed) y -= 1f;

        moveInput = new Vector2(x, y).normalized;

        // --- ANIMATION TRIGGERS ---
        if (anim != null)
        {
            bool isMoving = moveInput.sqrMagnitude > 0.01f;

            // 1. Trigger Walk / Stand
            anim.SetBool("isWalking", isMoving);

            // 2. Press Z to toggle Sleep
            if (keyboard.zKey.wasPressedThisFrame)
            {
                bool currentlySleeping = anim.GetBool("isSleeping");
                anim.SetBool("isSleeping", !currentlySleeping);
            }

            // 3. Automatically wake up if the player starts walking or jumps
            if (isMoving || keyboard.spaceKey.wasPressedThisFrame)
            {
                anim.SetBool("isSleeping", false);
            }
        }

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            lastJumpPressedTime = Time.time;
            Debug.Log("Space pressed");
        }
    }

    private void FixedUpdate()
    {
        bool grounded =
            Time.time >= jumpLockoutUntil && CheckGrounded();

        if (grounded)
        {
            lastGroundedTime = Time.time;
        }

        bool canJump =
            Time.time - lastGroundedTime <= coyoteTime;

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;

        if (cameraTransform != null)
        {
            forward = cameraTransform.forward;
            right = cameraTransform.right;

            forward.y = 0f;
            right.y = 0f;

            forward.Normalize();
            right.Normalize();
        }

        Vector3 moveDirection =
            forward * moveInput.y + right * moveInput.x;

        moveDirection = Vector3.ClampMagnitude(
            moveDirection, 1f
        );

        // Rotate the character to face the movement direction
        if (moveDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                turnSpeed * Time.fixedDeltaTime
            );
        }

        float speed = grounded
            ? moveSpeed
            : moveSpeed * airControl;

        Vector3 velocity = rb.linearVelocity;

        velocity.x = moveDirection.x * speed;
        velocity.z = moveDirection.z * speed;

        bool jumpRequested =
            Time.time - lastJumpPressedTime <= jumpBufferTime;

        if (jumpRequested && canJump)
        {
            velocity.y = jumpSpeed;

            lastJumpPressedTime = -100f;
            lastGroundedTime = -100f;

            jumpLockoutUntil = Time.time + 0.15f;

            Debug.Log("Player jumped!");
        }

        rb.linearVelocity = velocity;
    }

    private bool CheckGrounded()
    {
        Bounds bounds = capsule.bounds;

        float distance =
            bounds.extents.y + groundCheckDistance;

        RaycastHit[] hits = Physics.RaycastAll(
            bounds.center,
            Vector3.down,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore
        );

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform == transform ||
                hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            if (hit.normal.y > 0.55f)
            {
                return true;
            }
        }

        return false;
    }
}

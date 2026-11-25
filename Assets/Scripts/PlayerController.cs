using UnityEngine;

namespace PGIM.Sekiro2D
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 8f;
        [SerializeField] private float jumpForce = 12f;
        [SerializeField] private float acceleration = 50f;
        [SerializeField] private float deceleration = 50f;

        [Header("Ground Check")]
        [SerializeField] private Transform groundCheck;
        [SerializeField] private float groundCheckRadius = 0.15f;
        [SerializeField] private LayerMask groundLayer;

        [Header("Combat Reference")]
        [SerializeField] private PlayerCombat playerCombat;

        // Components
        private Rigidbody2D rb;
        private Animator anim;
        private SpriteRenderer spriteRenderer;

        // Input & State
        private float horizontalInput;
        private bool isGrounded;
        private bool isFacingRight = true;

        // External control flags
        public bool IsAttacking => playerCombat != null && playerCombat.IsAttacking;
        public bool IsParrying => playerCombat != null && playerCombat.IsParrying;
        public bool IsGrounded => isGrounded;
        public bool IsDashing { get; private set; }
        public bool IsInvulnerable { get; private set; }
        // True ketika pemain sudah mati -> seluruh input & pergerakan dihentikan.
        public bool IsDead => playerCombat != null && playerCombat.IsDead;
        // True ketika pemain sedang stagger (Guard Break) -> tidak bisa bergerak/bertarung.
        public bool IsStaggered => playerCombat != null && playerCombat.IsStaggered;
        // True ketika pemain sedang menahan tombol defend (hold-block).
        // Dipakai PlayerCombat & PlayerController agar animasi "Defend" tidak ditimpa
        // oleh animasi gerakan (Idle / Run).
        public bool IsBlocking => playerCombat != null && playerCombat.IsBlocking;

        [Header("Dodge Settings")]
        [SerializeField] private float dashSpeed = 14f;
        [SerializeField] private float dashDuration = 0.3f;
        [SerializeField] private float dashCooldown = 0.6f;
        private float lastDashTime = -999f;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            anim = GetComponent<Animator>();
            spriteRenderer = GetComponent<SpriteRenderer>();

            if (playerCombat == null)
            {
                playerCombat = GetComponent<PlayerCombat>();
            }

            if (groundCheck == null)
            {
                Transform found = transform.Find("GroundCheck");
                if (found != null)
                {
                    groundCheck = found;
                }
                else
                {
                    Debug.LogWarning("[PlayerController] GroundCheck transform not assigned and not found on child objects!");
                }
            }
        }

        private void Update()
        {
            // Pemain yang sudah mati tidak boleh menerima input lagi (A/D, lompat, dodge, dll).
            if (IsDead)
            {
                StopHorizontalMovement();
                UpdateAnimator();
                return;
            }

            // Read horizontal input (A/D or Left/Right Arrow)
            horizontalInput = Input.GetAxisRaw("Horizontal");

            // Ground check using Physics2D.OverlapCircle
            if (groundCheck != null)
            {
                isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
            }

            // Jump input (Space)
            if (Input.GetButtonDown("Jump") && isGrounded && !IsAttacking && !IsParrying && !IsDashing && !IsStaggered)
            {
                Jump();
            }

            // Dodge input (LeftShift or L)
            bool isStunned = playerCombat != null && (playerCombat.IsHurt || playerCombat.IsDead || playerCombat.IsStaggered);
            if ((Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.L)) && isGrounded && !IsDashing && Time.time >= lastDashTime + dashCooldown && !isStunned)
            {
                StartDodge();
            }

            // Handle facing direction flip
            // Tidak boleh berbalik arah saat sedang menyerang / parry / block (hold-defend)
            // supaya pose / animasi yang sedang aktif tidak terganggu.
            if (!IsAttacking && !IsParrying && !IsBlocking && !IsDashing && !IsStaggered)
            {
                if (horizontalInput > 0f && !isFacingRight)
                {
                    Flip();
                }
                else if (horizontalInput < 0f && isFacingRight)
                {
                    Flip();
                }
            }

            // Update Animator parameters
            UpdateAnimator();
        }

        private void FixedUpdate()
        {
            // Pemain yang sudah mati tidak boleh bergerak lagi (tidak bisa digeser dengan A/D).
            if (IsDead)
            {
                StopHorizontalMovement();
                return;
            }

            if (!IsDashing)
            {
                Move();
            }
        }

        /// <summary>
        /// Menghentikan pergerakan horizontal (kecepatan vertikal tetap dibiarkan agar gravitasi bekerja).
        /// </summary>
        private void StopHorizontalMovement()
        {
            if (rb == null) return;
            if (Mathf.Approximately(rb.linearVelocity.x, 0f)) return;
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }

        private void Move()
        {
            // Lock horizontal movement if attacking, parrying, blocking (hold-defend),
            // dashing, or staggered (Guard Break).
            //
            // Menahan tombol defend juga mengunci gerak agar pose "Defend" tetap stabil
            // dan parameter "Speed" tetap 0 (sehingga transisi "Idle -> Run" tidak
            // pernah menyela animasi Defend).
            float targetSpeed = (IsAttacking || IsParrying || IsBlocking || IsDashing || IsStaggered || IsDead) ? 0f : horizontalInput * moveSpeed;
            float speedRate = Mathf.Abs(targetSpeed) > 0.01f ? acceleration : deceleration;
            float newVelocityX = Mathf.MoveTowards(rb.linearVelocity.x, targetSpeed, speedRate * Time.fixedDeltaTime);

            rb.linearVelocity = new Vector2(newVelocityX, rb.linearVelocity.y);
        }

        private void StartDodge()
        {
            lastDashTime = Time.time;
            StartCoroutine(DodgeRoutineCo());
        }

        private System.Collections.IEnumerator DodgeRoutineCo()
        {
            IsDashing = true;
            IsInvulnerable = true;
            anim.SetTrigger("Dodge");

            // Always perform Backstep opposite to current facing direction, ignoring horizontal input
            float dashDir = isFacingRight ? -1f : 1f;

            float elapsed = 0f;
            while (elapsed < dashDuration)
            {
                rb.linearVelocity = new Vector2(dashDir * dashSpeed, 0f);
                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }

            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            IsDashing = false;
            IsInvulnerable = false;
        }

        private void Jump()
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            anim.SetTrigger("Jump");
        }

        private void Flip()
        {
            isFacingRight = !isFacingRight;
            Vector3 localScale = transform.localScale;
            localScale.x *= -1f;
            transform.localScale = localScale;
        }

        private void UpdateAnimator()
        {
            // Ketika pemain menahan tombol defend (hold-block), kecepatan horizontal
            // di-report sebagai 0 supaya transisi "Idle -> Run" (Speed > 0.1) TIDAK
            // terpicu dan menimpa animasi "Defend".
            //
            // Catatan: state "Defend" di controller Player tidak punya transisi ke
            // "Run", jadi begitu pemain sudah masuk ke "Defend" dia akan MENAHAN
            // di sana selama tombol ditekan. Yang perlu dijaga di sini adalah
            // jangan sampai transisi "Idle -> Run" terjadi lebih dulu.
            float currentSpeed = Mathf.Abs(rb.linearVelocity.x);
            if (IsBlocking || IsParrying)
            {
                currentSpeed = 0f;
            }

            anim.SetFloat("Speed", currentSpeed);
            anim.SetBool("IsGrounded", isGrounded);
            anim.SetFloat("yVelocity", rb.linearVelocity.y);
        }

        private void OnDrawGizmosSelected()
        {
            if (groundCheck != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
            }
        }
    }
}

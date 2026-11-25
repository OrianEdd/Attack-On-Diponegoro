using System.Collections;
using UnityEngine;

namespace PGIM.Sekiro2D
{
    /// <summary>
    /// Konstanta timing combat global (Sumber kebenaran / single source of truth).
    /// Nilai-nilai ini dipakai bersama oleh PlayerCombat dan EnemyKlewang agar
    /// timing Perfect Parry dan frame impact serangan musuh selalu sinkron.
    /// </summary>
    public static class CombatTiming
    {
        /// <summary>
        /// Waktu (detik) sejak animasi serangan musuh dimulai sampai FRAME IMPACT
        /// ayunan senjata, yaitu momen bilahPedang musuh menyentuh badan pemain.
        /// Harus sama dengan waktu Animation Event "TriggerAttackHitbox" pada
        /// clip Attack (detik ke-0.4).
        /// </summary>
        public const float EnemyAttackImpactTime = 0.4f;

        /// <summary>
        /// Jendela Perfect Parry (detik) sejak frame pertama tombol defend ditekan.
        /// Diperbesar (0.30 - 0.32 detik) agar pemain bisa menekan parry tepat
        /// saat senjata musuh hendak menyentuh badan.
        /// </summary>
        public const float ParryWindow = 0.32f;

        /// <summary>Posture damage ke musuh dari satu Perfect Parry.</summary>
        public const float ParryPostureDamageToEnemy = 30f;

        /// <summary>Posture damage ke musuh dari satu tebasan serangan pemain.</summary>
        public const float SlashPostureDamageToEnemy = 12f;

        /// <summary>Kecepatan pemulihan posture musuh (per detik).</summary>
        public const float EnemyPostureRecoveryRate = 12f;

        /// <summary>Jeda (detik) sebelum posture musuh mulai pulih kembali.</summary>
        public const float EnemyPostureRecoveryDelay = 2.0f;
    }

    public class EnemyKlewang : MonoBehaviour
    {
        // Alias konstanta global agar mudah dibaca di dalam kelas ini.
        private const float AttackImpactFrameTime = CombatTiming.EnemyAttackImpactTime;
        private const float ParryPostureDamage = CombatTiming.ParryPostureDamageToEnemy;
        private const float SlashPostureDamage = CombatTiming.SlashPostureDamageToEnemy;
        private const float EnemyPostureRecoveryRate = CombatTiming.EnemyPostureRecoveryRate;
        private const float EnemyPostureRecoveryDelay = CombatTiming.EnemyPostureRecoveryDelay;

        [Header("Combat Settings")]
        [SerializeField] private Transform attackPoint;
        [SerializeField] private float attackRadius = 1.0f;
        [SerializeField] private LayerMask playerLayer;
        [SerializeField] private float baseDamage = 18f;
        [SerializeField] private float basePostureDamage = 16f;
        [SerializeField] private float attackCooldown = 2.0f;
        // Waktu (detik) sejak animasi serangan dimulai sampai FRAME IMPACT ayunan
        // senjata, yaitu momen di mana bilahPedang musuh menyentuh badan pemain.
        // Nilai ini HARUS sama dengan waktu Animation Event "TriggerAttackHitbox"
        // pada clip Attack (detik ke-0.4). Nilai ini hanya dipakai sebagai
        // jaring pengaman bila Animation Event gagal terpanggil.
        [SerializeField] private float attackImpactTime = 0.4f;
        // Durasi (detik) fase recover setelah ayunan mendarat.
        [SerializeField] private float attackRecoverTime = 0.25f;
        // Toleransi (detik) sebelum fallback hitbox dianggap terlalu dini dan
        // dibatalkan, sehingga damage TIDAK pernah terjadi saat wind-up.
        [SerializeField] private float attackImpactTolerance = 0.05f;

        [Header("Health & Posture Stats (Sekiro Style)")]
        [SerializeField] private float maxHealth = 150f;
        private float currentHealth;
        // POSTURE TERSEMBUNYI (tanpa UI bar).
        // Posture musuh tidak pernah ditampilkan ke pemain; pemain harus menebak
        // kapan guard break terjadi dengan cara menahan tebasan / mem-parry.
        // Catatan: TIDAK ada Slider / Image / UI apa pun yang dibuat atau ditampilkan
        // untuk posture musuh di file ini maupun di PlayerHUD.
        [SerializeField] private float maxPosture = 100f;
        private float currentPosture;
        // Kecepatan pemulihan posture (per detik) setelah jeda pemulihan terlewati.
        [SerializeField] private float postureRecoveryRate = 12f;
        // Jeda (detik) sejak terakhir terkena serangan / parry sebelum posture mulai pulih.
        [SerializeField] private float postureRecoveryDelay = 2.0f;

        [Header("Posture Damage Sources (Tersembunyi - Tidak Ada UI Bar)")]
        // Posture yang ditambahkan ke musuh saat pemain melakukan Perfect Parry (Deflect).
        [SerializeField] private float parryPostureDamage = 30f;
        // Posture yang ditambahkan ke musuh saat terkena tebasan serangan pemain.
        [SerializeField] private float slashPostureDamage = 12f;

        [Header("Guard Break / Stagger Settings")]
        // Durasi musuh tidak bisa bergerak / menyerang saat posture pecah (Guard Break).
        [SerializeField] private float guardBreakDuration = 1.5f;

        [Header("Movement & AI Settings")]
        [SerializeField] private float moveSpeed = 3.5f;
        [SerializeField] private float detectionRange = 7.0f;
        [SerializeField] private float attackRange = 1.5f;
        [SerializeField] private Transform playerTransform;

        [Header("References")]
        [SerializeField] private Animator anim;
        [SerializeField] private Rigidbody2D rb;
        [SerializeField] private SpriteRenderer spriteRenderer;

        public bool IsDead { get; private set; }
        public bool IsHurt { get; private set; }
        public bool IsAttacking { get; private set; }
        // True saat musuh sedang mengalami Guard Break / Stagger karena posture penuh.
        public bool IsStaggered { get; private set; }
        // Posture musuh TIDAK ditampilkan ke pemain (sistem posture tersembunyi).
        // Nilai ini hanya tersedia untuk keperluan internal / debugging.
        public float CurrentPosture => currentPosture;
        public float MaxPosture => maxPosture;

        private float lastAttackTime = -999f;
        private float postureTimer;
        private bool isFacingRight = false;

        // Coroutine yang sedang berjalan (dipakai untuk membatalkan serangan saat stagger).
        private Coroutine attackRoutine;
        private Coroutine staggerRoutine;
        // Menandai bahwa frame impact ayunan senjata sudah lewat, sehingga hitbox
        // hanya aktif pada momen benturan (bukan saat wind-up).
        private bool attackImpactReached;
        // Waktu (Time.time) saat animasi serangan musuh dimulai, dipakai untuk
        // memastikan damage hanya terjadi pada frame impact (tidak lebih awal).
        private float attackStartTime = -999f;

        public event System.Action OnEnemyDeath;
        public event System.Action<float, float> OnHealthChanged;
        public event System.Action<float, float> OnPostureChanged;
        private void Awake()
        {
            if (anim == null) anim = GetComponent<Animator>();
            if (rb == null) rb = GetComponent<Rigidbody2D>();
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();

            if (attackPoint == null)
            {
                Transform found = transform.Find("AttackPoint");
                if (found != null) attackPoint = found;
            }

            // Terapkan nilai timing kanonik agar perilaku konsisten di scene mana pun
            // (nilai yang ter-serialize di scene bisa saja hasil tuning lama).
            ApplyCombatTimingSettings();

            currentHealth = maxHealth;
            currentPosture = 0f;
        }

        /// <summary>
        /// Menegakkan nilai timing serangan yang benar (sinkron dengan Timing Perfect
        /// Parry). Nilai di sini adalah sumber kebenaran dan ditulis ulang saat Awake
        /// supaya timing tidak lagi bergantung pada nilai lama yang tersimpan di scene.
        /// </summary>
        private void ApplyCombatTimingSettings()
        {
            // FRAME IMPACT ayunan senjata pada clip Attack terjadi di detik ke-0.4
            // (Animation Event "TriggerAttackHitbox"). Damage TIDAK BOLEH diberikan
            // lebih awal dari itu, karena pemain harus sempat menekan parry.
            attackImpactTime = AttackImpactFrameTime;
            attackImpactTolerance = 0.05f;
            attackRecoverTime = Mathf.Max(0f, attackRecoverTime);

            // Posture musuh: pulih 12/detik setelah jeda 2 detik tanpa pukulan.
            postureRecoveryRate = EnemyPostureRecoveryRate;
            postureRecoveryDelay = EnemyPostureRecoveryDelay;

            // Posture damage ke musuh: 30 dari Perfect Parry, 12 dari tebasan pemain.
            parryPostureDamage = ParryPostureDamage;
            slashPostureDamage = SlashPostureDamage;
        }

        private void Start()
        {
            if (playerTransform == null)
            {
                GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null)
                {
                    playerTransform = playerObj.transform;
                }
            }

            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            OnPostureChanged?.Invoke(currentPosture, maxPosture);
        }

        private void Update()
        {
            if (IsDead) return;

            // Musuh yang sedang stagger (Guard Break) tidak boleh bergerak / menyerang.
            if (IsStaggered)
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                anim.SetFloat("Speed", 0f);
                return;
            }

            if (IsHurt) return;

            if (playerTransform != null)
            {
                PlayerCombat playerCombatRef = playerTransform.GetComponent<PlayerCombat>();
                if (playerCombatRef != null && playerCombatRef.IsDead)
                {
                    rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                    anim.SetFloat("Speed", 0f);
                    IsAttacking = false;
                    return;
                }
            }

            // Pemulihan posture (tersembunyi - tidak ada UI bar).
            // Timer pemulihan terus berjalan; posture baru mulai pulih setelah
            // postureRecoveryDelay detik sejak terakhir dipukul / diparry.
            // Pemulihan TIDAK berhenti saat musuh menyerang, agar posture tetap
            // bisa menyusut ketika pemain memberi jeda (sirik).
            UpdatePostureRecovery();

            if (playerTransform == null) return;

            float distanceToPlayer = Vector2.Distance(transform.position, playerTransform.position);

            if (playerTransform.position.x > transform.position.x && !isFacingRight)
            {
                Flip();
            }
            else if (playerTransform.position.x < transform.position.x && isFacingRight)
            {
                Flip();
            }

            if (distanceToPlayer <= attackRange)
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                if (Time.time >= lastAttackTime + attackCooldown && !IsAttacking)
                {
                    if (attackRoutine != null) StopCoroutine(attackRoutine);
                    attackRoutine = StartCoroutine(AttackRoutineCo());
                }
                else
                {
                    anim.SetFloat("Speed", 0f);
                }
            }
            else if (distanceToPlayer <= detectionRange)
            {
                float dir = playerTransform.position.x > transform.position.x ? 1f : -1f;
                rb.linearVelocity = new Vector2(dir * moveSpeed, rb.linearVelocity.y);
                anim.SetFloat("Speed", moveSpeed);
            }
            else
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                anim.SetFloat("Speed", 0f);
            }
        }
        /// <summary>
        /// Pemulihan posture musuh (sistem tersembunyi - tanpa UI bar).
        /// Timer di-reset setiap kali musuh terkena tebasan / parry, sehingga posture
        /// hanya mulai pulih setelah <see cref="postureRecoveryDelay"/> detik tanpa
        /// pukulan. Selama stagger / hurt pemulihan ditangguhkan.
        /// </summary>
        private void UpdatePostureRecovery()
        {
            if (currentPosture <= 0f) return;
            if (IsStaggered || IsHurt) return;

            postureTimer += Time.deltaTime;
            if (postureTimer < postureRecoveryDelay) return;

            currentPosture = Mathf.Max(0f, currentPosture - postureRecoveryRate * Time.deltaTime);
            OnPostureChanged?.Invoke(currentPosture, maxPosture);
        }

        /// <summary>
        /// Alur serangan musuh.
        /// Damage TIDAK lagi dipanggil pada frame wind-up (ancang-ancang). Damage hanya
        /// diberikan pada FRAME IMPACT ayunan senjata, yaitu ketika bilahPedang musuh
        /// benar-benar menyentuh badan pemain (dikendalikan oleh Animation Event
        /// "TriggerAttackHitbox" pada clip Attack di detik ke-0.4).
        /// Fallback berbasis waktu tetap ada sebagai jaring pengaman bila Animation Event
        /// tidak terpanggil (mis. clip animasi diganti di prefab), TETAPI fallback itu
        /// hanya boleh terjadi pada / setelah frame impact, tidak boleh lebih awal.
        /// </summary>
        private IEnumerator AttackRoutineCo()
        {
            IsAttacking = true;
            lastAttackTime = Time.time;
            attackStartTime = Time.time;
            attackImpactReached = false;
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            anim.SetTrigger("Attack");

            // Wind-up: musuh bersiap-siap. Selama fase ini TIDAK ada damage sama sekali,
            // sehingga pemain punya kesempatan menekan parry sebelum ayunan mendarat.
            yield return new WaitForSeconds(attackImpactTime);

            // Frame impact sudah tercapai. Tandai dulu, baru panggil hitbox.
            attackImpactReached = true;
            TriggerAttackHitbox();

            // Sisa durasi animasi ayunan (recover) sebelum musuh boleh menyerang lagi.
            yield return new WaitForSeconds(attackRecoverTime);

            attackImpactReached = false;
            IsAttacking = false;
            attackRoutine = null;
        }

        /// <summary>
        /// Dipanggil oleh Animation Event "TriggerAttackHitbox" pada frame impact ayunan.
        /// </summary>
        public void TriggerAttackHitbox()
        {
            // Abaikan jika musuh sedang stagger / mati / sudah selesai ayunan.
            if (IsDead || IsStaggered || IsHurt) return;
            if (attackPoint == null) return;

            // Jaring pengaman anti-wind-up: bila pemanggilan terjadi terlalu dini
            // (mis. Animation Event salah tempat / clip animasi diganti), damage
            // dibatalkan agar pemain tetap bisa mem-parry tepat saat bilahPedang
            // hendak menyentuh badan. Impact yang sah tetap diizinkan lewat flag
            // attackImpactReached (di-set oleh fallback waktu di AttackRoutineCo).
            float elapsed = Time.time - attackStartTime;
            bool impactFrameReached = elapsed >= (attackImpactTime - attackImpactTolerance);
            if (!attackImpactReached && !impactFrameReached) return;

            Collider2D[] hitPlayers = Physics2D.OverlapCircleAll(attackPoint.position, attackRadius, playerLayer);
            foreach (Collider2D player in hitPlayers)
            {
                PlayerCombat playerCombat = player.GetComponent<PlayerCombat>();
                if (playerCombat != null)
                {
                    Debug.Log($"[EnemyKlewang] Attacking Player! Dealing {baseDamage} damage & {basePostureDamage} posture damage.");
                    playerCombat.TakeDamage(baseDamage, basePostureDamage, transform.position);
                }
            }
        }

        /// <summary>
        /// Dipanggil ketika pemain melakukan Perfect Parry / Deflect.
        /// Musuh menerima posture damage balik (tanpa kehilangan HP) serta reaksi deflect
        /// (animasi Hurt + knockback ringan). Jika posture musuh penuh, musuh mengalami
        /// GUARD BREAK / STAGGER (bukan mati), karena posture musuh bersifat tersembunyi
        /// dan tidak pernah ditampilkan lewat UI bar.
        /// </summary>
        public void ReceiveParryPostureDamage(float postureDamage, Vector2 attackerPosition)
        {
            if (IsDead || IsStaggered) return;

            // Perfect Parry pemain memberi posture damage ke musuh (nilai standarnya
            // dari parryPostureDamage = 30). Nilai dari pemanggil tetap dipakai bila
            // memang diberikan secara eksplisit.
            float appliedPosture = postureDamage > 0f ? postureDamage : parryPostureDamage;

            // Reset timer pemulihan posture: posture baru akan pulih setelah
            // postureRecoveryDelay detik sejak terakhir terkena parry.
            postureTimer = 0f;
            currentPosture = Mathf.Min(maxPosture, currentPosture + appliedPosture);
            OnPostureChanged?.Invoke(currentPosture, maxPosture);

            Debug.Log($"[EnemyKlewang] Deflected! Took {appliedPosture} posture damage. Current Posture: {currentPosture}");

            // Reaksi deflect: animasi Hurt + knockback kecil menjauh dari pemain.
            if (!IsHurt)
            {
                StartCoroutine(HurtRoutineCo());
            }

            if (rb != null)
            {
                float knockbackDir = transform.position.x >= attackerPosition.x ? 1f : -1f;
                rb.linearVelocity = new Vector2(knockbackDir * 3f, rb.linearVelocity.y);
            }

            // Posture penuh -> Guard Break / Stagger (musuh TIDAK mati).
            if (currentPosture >= maxPosture)
            {
                StartGuardBreak();
            }
        }

        public void TakeDamage(float damage, float postureDamage, Vector2 attackerPosition)
        {
            if (IsDead || IsStaggered) return;

            // Tebasan serangan pemain memberi posture damage ke musuh
            // (nilai standarnya dari slashPostureDamage = 12).
            float appliedPosture = postureDamage > 0f ? postureDamage : slashPostureDamage;

            // Reset timer pemulihan posture setiap kali musuh terkena serangan.
            postureTimer = 0f;
            currentHealth = Mathf.Max(0f, currentHealth - damage);
            currentPosture = Mathf.Min(maxPosture, currentPosture + appliedPosture);

            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            OnPostureChanged?.Invoke(currentPosture, maxPosture);

            Debug.Log($"[EnemyKlewang] Took {damage} damage, {appliedPosture} posture damage. Current HP: {currentHealth}, Posture: {currentPosture}");

            // Mati HANYA jika HP habis. Posture penuh = Guard Break / Stagger.
            if (currentHealth <= 0f)
            {
                Die();
            }
            else if (currentPosture >= maxPosture)
            {
                StartGuardBreak();
            }
            else
            {
                StartCoroutine(HurtRoutineCo());
            }
        }

        /// <summary>
        /// Guard Break / Stagger musuh: dipanggil ketika currentPosture mencapai maxPosture.
        /// Musuh TIDAK mati, tapi:
        ///   1. Serangan yang sedang berjalan dihentikan (coroutine dibatalkan).
        ///   2. Pergerakan & serangan dikunci selama <see cref="guardBreakDuration"/> detik.
        ///   3. Animasi Hurt diputar sebagai tanda stagger.
        ///   4. Setelah durasi stagger selesai, posture di-reset ke 0.
        /// </summary>
        private void StartGuardBreak()
        {
            if (IsStaggered || IsDead) return;

            Debug.Log("[EnemyKlewang] GUARD BREAK! Posture penuh -> Stagger (bukan death).");

            IsStaggered = true;
            IsAttacking = false;
            IsHurt = false;
            attackImpactReached = false;
            attackStartTime = -999f;

            // Hentikan serangan yang sedang berjalan (bila ada) agar ayunan tidak
            // tetap menimpah pemain saat musuh sudah stagger.
            if (attackRoutine != null)
            {
                StopCoroutine(attackRoutine);
                attackRoutine = null;
            }

            // Kunci pergerakan musuh selama stagger.
            if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            anim.SetFloat("Speed", 0f);

            // Putar animasi Hurt sebagai tanda stagger / guard break.
            anim.SetTrigger("Hurt");

            if (staggerRoutine != null) StopCoroutine(staggerRoutine);
            staggerRoutine = StartCoroutine(GuardBreakRoutineCo());
        }

        private IEnumerator GuardBreakRoutineCo()
        {
            yield return new WaitForSeconds(guardBreakDuration);

            // Posture di-reset ke 0 setelah durasi stagger selesai.
            currentPosture = 0f;
            postureTimer = 0f;
            OnPostureChanged?.Invoke(currentPosture, maxPosture);

            IsStaggered = false;
            staggerRoutine = null;
        }

        private IEnumerator HurtRoutineCo()
        {
            IsHurt = true;
            IsAttacking = false;
            anim.SetTrigger("Hurt");
            yield return new WaitForSeconds(0.25f);
            IsHurt = false;
        }

        private void Die()
        {
            IsDead = true;
            IsAttacking = false;
            IsStaggered = false;
            if (rb != null) rb.linearVelocity = Vector2.zero;
            anim.SetTrigger("Death");
            OnEnemyDeath?.Invoke();
            Debug.Log("[EnemyKlewang] Enemy Klewang defeated!");
            Destroy(gameObject, 2.0f);
        }

        private void Flip()
        {
            isFacingRight = !isFacingRight;

            Vector3 scale = transform.localScale;
            scale.x = Mathf.Abs(scale.x) * (isFacingRight ? 1f : -1f);
            transform.localScale = scale;
        }

        private void OnDrawGizmosSelected()
        {
            if (attackPoint == null) return;

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(attackPoint.position, attackRadius);
        }
    }
}

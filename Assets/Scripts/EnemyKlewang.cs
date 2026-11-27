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
        public const float ParryWindow = 0.2f;

        /// <summary>Posture damage ke musuh dari satu Perfect Parry.</summary>
        public const float ParryPostureDamageToEnemy = 30f;

        /// <summary>Posture damage ke musuh dari satu tebasan serangan pemain.</summary>
        public const float SlashPostureDamageToEnemy = 12f;

        /// <summary>Kecepatan pemulihan posture musuh (per detik).</summary>
        public const float EnemyPostureRecoveryRate = 12f;

        /// <summary>Jeda (detik) sebelum posture musuh mulai pulih kembali.</summary>
        public const float EnemyPostureRecoveryDelay = 2.0f;

        /// <summary>
        /// Jeda (detik) antar serangan musuh. Nilai ini dibuat agresif supaya
        /// tekanan ke pemain meningkat dan Ritme bertarung terasa hidup.
        /// </summary>
        public const float EnemyAttackCooldown = 1.1f;

        /// <summary>
        /// Durasi Guard Break / Stagger musuh (detik). Cukup panjang supaya
        /// pemain punya jendela untuk eksekusi Deathblow.
        /// </summary>
        public const float EnemyGuardBreakDuration = 3.5f;
    }

    public class EnemyKlewang : MonoBehaviour
    {
        // Alias konstanta global agar mudah dibaca di dalam kelas ini.
        private const float AttackImpactFrameTime = CombatTiming.EnemyAttackImpactTime;
        private const float ParryPostureDamage = CombatTiming.ParryPostureDamageToEnemy;
        private const float SlashPostureDamage = CombatTiming.SlashPostureDamageToEnemy;
        private const float EnemyPostureRecoveryRate = CombatTiming.EnemyPostureRecoveryRate;
        private const float EnemyPostureRecoveryDelay = CombatTiming.EnemyPostureRecoveryDelay;
        private const float EnemyAttackCooldown = CombatTiming.EnemyAttackCooldown;
        private const float EnemyGuardBreakDuration = CombatTiming.EnemyGuardBreakDuration;

        // Nama trigger Animator musuh (dipakai sebagai konstanta supaya tidak
        // ada lagi penamaan trigger yang menebak / typo).
        private const string AnimTriggerAttack = "Attack";
        private const string AnimTriggerHurt = "Hurt";
        private const string AnimTriggerDeath = "Death";

        // Warna tint kemerahan saat musuh sedang Guard Break / lumpuh.
        // Nilai ini TIDAK bisa ditimpa Inspector (bukan [SerializeField]) supaya
        // tampilan guard break konsisten di seluruh scene.
        private static readonly Color GuardBreakTintColor = new Color(1f, 0.45f, 0.45f, 1f);

        // Warna indikator prosedural (lingkaran merah) saat Guard Break.
        private static readonly Color GuardBreakIndicatorColor = new Color(1f, 0.2f, 0.2f, 1f);

        [Header("Combat Settings")]
        [SerializeField] private Transform attackPoint;
        [SerializeField] private float attackRadius = 1.0f;
        [SerializeField] private LayerMask playerLayer;
        [SerializeField] private float baseDamage = 18f;
        [SerializeField] private float basePostureDamage = 16f;
        // Jeda (detik) antar serangan musuh. Default 1.1f (lebih agresif dari 2.0f
        // sebelumnya). CATATAN: nilai ini di-enforce ulang di Awake, jadi nilai
        // lama yang tersimpan di Inspector/scene TIDAK lagi memengaruhi ritme.
        [SerializeField] private float attackCooldown = 1.1f;
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
        // Default 3.5f (lebih panjang dari 1.5f sebelumnya) supaya pemain punya
        // jendela untuk eksekusi Deathblow. CATATAN: nilai ini di-enforce ulang
        // di Awake, jadi nilai lama yang tersimpan di Inspector/scene TIDAK lagi
        // memengaruhi durasi.
        [SerializeField] private float guardBreakDuration = 3.5f;
        // True bila musuh ini boleh dieksekusi dengan Deathblow.
        // Dipakai PlayerCombat saat mencari target Deathblow.
        [SerializeField] private bool canBeDeathblown = true;
        // Durasi knockback singkat (detik) saat Guard Break dimulai.
        [SerializeField] private float guardBreakKnockbackDuration = 0.2f;
        // Kecepatan knockback (unit/detik) saat Guard Break dimulai.
        [SerializeField] private float guardBreakKnockbackSpeed = 6f;

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
        // Alias dari IsStaggered: True saat Guard Break sedang berlangsung.
        // Dipakai PlayerCombat untuk mendeteksi target Deathblow.
        public bool IsGuardBroken => IsStaggered;
        // True bila musuh ini boleh dieksekusi dengan Deathblow.
        public bool CanBeDeathblown => canBeDeathblown;
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
        // Coroutine knockback Guard Break (dorongan singkat menjauhi pemain).
        private Coroutine knockbackRoutine;
        // True selama knockback Guard Break masih berjalan, supaya Update()
        // tidak menolkan velocity horizontal di tengah dorongan.
        private bool isGuardBreakKnockbackActive;
        // Menandai bahwa frame impact ayunan senjata sudah lewat, sehingga hitbox
        // hanya aktif pada momen benturan (bukan saat wind-up).
        private bool attackImpactReached;
        // Waktu (Time.time) saat animasi serangan musuh dimulai, dipakai untuk
        // memastikan damage hanya terjadi pada frame impact (tidak lebih awal).
        private float attackStartTime = -999f;

        // Collider hitbox serangan (bila prefab punya collider di AttackPoint).
        // Di-cache di Awake supaya bisa dimatikan saat Guard Break dan
        // diaktifkan kembali setelah stagger selesai.
        private Collider2D[] attackHitboxColliders = new Collider2D[0];

        // Warna ASLI sprite musuh, disimpan SEBELUM di-tint merah saat Guard
        // Break, supaya bisa dikembalikan persis seperti semula (bukan Color.white).
        private Color originalSpriteColor = Color.white;
        private bool hasOriginalSpriteColor;

        // Objek indikator prosedural (lingkaran/titik merah) saat Guard Break.
        private GameObject guardBreakIndicator;
        private SpriteRenderer guardBreakIndicatorRenderer;
        private Coroutine indicatorPulseRoutine;

        // Sprite indikator di-cache secara STATIC supaya teksturnya hanya dibuat
        // SEKALI lalu dipakai bersama oleh semua musuh di scene.
        private static Sprite guardBreakIndicatorSprite;

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

            // Cache collider hitbox serangan supaya bisa dimatikan / diaktifkan
            // lagi saat Guard Break.
            CacheAttackHitboxColliders();

            // Simpan warna asli sprite (sebelum ada tint merah Guard Break)
            // supaya warnanya bisa dikembalikan persis seperti semula.
            if (spriteRenderer != null)
            {
                originalSpriteColor = spriteRenderer.color;
                hasOriginalSpriteColor = true;
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

            // Ritme bertarung musuh: jeda antar serangan diperpendek (1.1 detik)
            // supaya tekanan ke pemain meningkat.
            attackCooldown = EnemyAttackCooldown;

            // Durasi Guard Break diperpanjang (3.5 detik) => pemain punya
            // jendela yang cukup untuk eksekusi Deathblow.
            guardBreakDuration = EnemyGuardBreakDuration;
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
            //
            // Catatan: jalur ini juga MENGAKCI flip arah hadap, sehingga selama
            // IsGuardBroken == true musuh tidak pernah berbalik arah.
            if (IsStaggered)
            {
                // Selama knockback Guard Break masih berjalan, velocity horizontal
                // SENGAJA tidak ditolkan (di-nolkan di akhir coroutine knockback).
                if (!isGuardBreakKnockbackActive)
                {
                    rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                }
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
            anim.SetTrigger(AnimTriggerAttack);

            // Aktifkan kembali hitbox serangan (kalau sempat dimatikan saat Guard Break).
            SetAttackHitboxEnabled(true);

            // Feedback audio: suara tebasan (whoosh) saat musuh memulai ayunan.
            CombatFeedback.PlaySlashSfx();

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
                StartGuardBreak(attackerPosition);
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
                StartGuardBreak(attackerPosition);
            }
            else
            {
                StartCoroutine(HurtRoutineCo());
            }
        }

        /// <summary>
        /// Guard Break / Stagger musuh: dipanggil ketika currentPosture mencapai maxPosture.
        /// Musuh TIDAK mati, tapi:
        ///   1. Serangan yang sedang berjalan dihentikan (coroutine dibatalkan) &
        ///      collider hitbox serangan dimatikan.
        ///   2. Trigger serangan di Animator di-reset.
        ///   3. Knockback singkat menjauhi pemain, lalu velocity di-nolkan.
        ///   4. Pergerakan, serangan & flip arah hadap dikunci selama
        ///      <see cref="guardBreakDuration"/> detik (IsGuardBroken == true).
        ///   5. Sprite di-tint merah + indikator prosedural berdenyut sekali.
        ///   6. Setelah durasi stagger selesai, posture di-reset ke 0, warna
        ///      sprite dikembalikan ke warna ASLI yang tersimpan, dan indikator
        ///      disembunyikan.
        /// </summary>
        /// <param name="attackerPosition">
        /// Posisi penyerang (pemain). Dipakai untuk menentukan arah knockback.
        /// Kalau null, posisi playerTransform dipakai sebagai gantinya.
        /// </param>
        private void StartGuardBreak(Vector2? attackerPosition = null)
        {
            if (IsStaggered || IsDead) return;

            Debug.Log("[EnemyKlewang] GUARD BREAK! Posture penuh -> Stagger (bukan death).");

            IsStaggered = true;
            IsAttacking = false;
            IsHurt = false;
            attackImpactReached = false;
            attackStartTime = -999f;

            // Feedback: suara retak guard + percikan oranye-merah besar + getaran
            // kamera yang kuat (guard break = momen berat).
            CombatFeedback.PlayGuardBreakSfx();
            CombatFeedback.SpawnSparks(
                transform.position,
                new Color(1f, 0.45f, 0.1f),
                34,
                0.22f);
            CombatFeedback.ShakeCamera(0.18f, 0.2f);

            // Hentikan serangan yang sedang berjalan (bila ada) agar ayunan tidak
            // tetap menimpah pemain saat musuh sudah stagger.
            if (attackRoutine != null)
            {
                StopCoroutine(attackRoutine);
                attackRoutine = null;
            }

            // Matikan collider hitbox serangan supaya tidak ada tebasan aktif
            // selama musuh sedang lumpuh.
            SetAttackHitboxEnabled(false);

            // Reset trigger serangan di Animator supaya trigger "Attack" yang
            // nyangkut tidak terpicu ulang di tengah stagger.
            if (anim != null)
            {
                anim.ResetTrigger(AnimTriggerAttack);
            }

            // Simpan warna ASLI sprite SEBELUM di-tint merah, supaya bisa
            // dikembalikan persis seperti semula (bukan Color.white).
            if (spriteRenderer != null)
            {
                originalSpriteColor = spriteRenderer.color;
                hasOriginalSpriteColor = true;
                spriteRenderer.color = GuardBreakTintColor;
            }

            // Kunci pergerakan musuh selama stagger (knockback singkat menyusul).
            if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            anim.SetFloat("Speed", 0f);

            // Putar animasi Hurt sebagai tanda stagger / guard break.
            anim.SetTrigger(AnimTriggerHurt);

            // Indikator prosedural: lingkaran merah berdenyut sekali, di atas sprite.
            SpawnGuardBreakIndicator();

            // Knockback singkat menjauhi pemain, lalu velocity di-nolkan.
            if (knockbackRoutine != null) StopCoroutine(knockbackRoutine);
            knockbackRoutine = StartCoroutine(GuardBreakKnockbackCo(ResolveKnockbackDirection(attackerPosition)));

            if (staggerRoutine != null) StopCoroutine(staggerRoutine);
            staggerRoutine = StartCoroutine(GuardBreakRoutineCo());
        }

        /// <summary>
        /// Menentukan arah knockback: menjauhi sumber serangan (pemain).
        /// </summary>
        private float ResolveKnockbackDirection(Vector2? attackerPosition)
        {
            Vector2 source;
            if (attackerPosition.HasValue)
            {
                source = attackerPosition.Value;
            }
            else if (playerTransform != null)
            {
                source = playerTransform.position;
            }
            else
            {
                source = transform.position;
            }

            return transform.position.x >= source.x ? 1f : -1f;
        }

        /// <summary>
        /// Knockback singkat saat Guard Break dimulai: dorongan menjauhi pemain,
        /// lalu velocity DI-NOLKAN supaya musuh tidak meluncur terus.
        ///
        /// Memakai WaitForSecondsRealtime supaya dorongan pasti selesai walaupun
        /// ada hit-stop (Time.timeScale = 0) di frame yang sama.
        /// </summary>
        private IEnumerator GuardBreakKnockbackCo(float direction)
        {
            if (rb == null) yield break;

            isGuardBreakKnockbackActive = true;

            // Dorongan singkat menjauhi pemain.
            rb.linearVelocity = new Vector2(direction * guardBreakKnockbackSpeed, 0f);

            yield return new WaitForSecondsRealtime(Mathf.Max(0f, guardBreakKnockbackDuration));

            // Setelah knockback selesai, velocity DINOLKAN.
            rb.linearVelocity = Vector2.zero;
            isGuardBreakKnockbackActive = false;
            knockbackRoutine = null;
        }

        private IEnumerator GuardBreakRoutineCo()
        {
            // Timer Guard Break memakai waktu SCALED (Time.deltaTime), bukan realtime:
            // durasi stagger mengikuti alur game (ikut berhenti saat hit-stop).
            float duration = Mathf.Max(0f, guardBreakDuration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!IsDead)
            {
                // Posture di-reset ke 0 setelah durasi stagger selesai, supaya
                // musuh tidak langsung terkena Guard Break lagi.
                currentPosture = 0f;
                postureTimer = 0f;
                OnPostureChanged?.Invoke(currentPosture, maxPosture);

                // Kembalikan warna sprite ke warna ASLI yang tersimpan
                // (bukan Color.white).
                RestoreSpriteColor();

                // Sembunyikan indikator Guard Break (objeknya tetap ada supaya
                // bisa dipakai lagi bila musuh guard break untuk kedua kalinya).
                HideGuardBreakIndicator();

                // Aktifkan kembali hitbox serangan.
                SetAttackHitboxEnabled(true);

                // Musuh boleh bergerak / menyerang lagi.
                IsStaggered = false;
            }

            staggerRoutine = null;
        }

        /// <summary>
        /// Eksekusi Deathblow: damage mematikan yang HANYA boleh terjadi saat
        /// musuh sedang Guard Break.
        ///
        /// Metode ini TERPISAH dari TakeDamage() supaya jalur damage biasa
        /// (yang menolak damage saat IsStaggered) tetap utuh dan logika
        /// parry tidak berubah sama sekali.
        /// </summary>
        /// <param name="damage">Damage mematikan (bawaan 9999f dari PlayerCombat).</param>
        /// <param name="attackerPosition">Posisi pemain yang mengeksekusi.</param>
        public void ApplyDeathblow(float damage, Vector2 attackerPosition)
        {
            if (IsDead) return;

            currentHealth = Mathf.Max(0f, currentHealth - damage);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);

            Debug.Log($"[EnemyKlewang] DEATHBLOW! Took {damage} damage from {attackerPosition}. HP: {currentHealth}");

            Die();
        }

        // ==================================================================
        // Hitbox serangan
        // ==================================================================

        /// <summary>
        /// Cache collider hitbox serangan (bila prefab punya collider di
        /// AttackPoint / anak-anaknya).
        /// </summary>
        private void CacheAttackHitboxColliders()
        {
            if (attackPoint == null)
            {
                attackHitboxColliders = new Collider2D[0];
                return;
            }

            attackHitboxColliders = attackPoint.GetComponentsInChildren<Collider2D>(true);
        }

        /// <summary>
        /// Mengaktifkan / mematikan collider hitbox serangan.
        /// Dipakai saat Guard Break dimulai (dimatikan) dan saat stagger
        /// selesai (diaktifkan kembali).
        /// </summary>
        private void SetAttackHitboxEnabled(bool enabledState)
        {
            if (attackHitboxColliders == null) return;

            for (int i = 0; i < attackHitboxColliders.Length; i++)
            {
                Collider2D hitbox = attackHitboxColliders[i];
                if (hitbox != null) hitbox.enabled = enabledState;
            }
        }

        // ==================================================================
        // Visual Stun (tint merah + indikator prosedural)
        // ==================================================================

        /// <summary>
        /// Mengembalikan warna sprite ke warna ASLI yang tersimpan saat Guard
        /// Break (bukan Color.white, karena sprite bisa saja tidak putih).
        /// </summary>
        private void RestoreSpriteColor()
        {
            if (spriteRenderer == null || !hasOriginalSpriteColor) return;
            spriteRenderer.color = originalSpriteColor;
        }

        /// <summary>
        /// Memunculkan indikator prosedural (lingkaran/titik merah) di ATAS
        /// sprite musuh, lalu membuatnya berdenyut SEKALI.
        /// </summary>
        private void SpawnGuardBreakIndicator()
        {
            if (spriteRenderer == null) return;

            if (guardBreakIndicator == null)
            {
                guardBreakIndicator = new GameObject("GuardBreakIndicator");
                guardBreakIndicator.transform.SetParent(transform, false);

                guardBreakIndicatorRenderer = guardBreakIndicator.AddComponent<SpriteRenderer>();
                guardBreakIndicatorRenderer.sprite = GetGuardBreakIndicatorSprite();
                guardBreakIndicatorRenderer.color = GuardBreakIndicatorColor;
            }

            // Indikator selalu tampil DI ATAS sprite musuh.
            guardBreakIndicatorRenderer.sortingOrder = spriteRenderer.sortingOrder + 1;
            guardBreakIndicator.transform.localPosition = Vector3.zero;
            guardBreakIndicator.transform.localScale = Vector3.one;
            guardBreakIndicator.SetActive(true);

            // Berdenyut SATU KALI saja (bukan loop).
            if (indicatorPulseRoutine != null) StopCoroutine(indicatorPulseRoutine);
            indicatorPulseRoutine = StartCoroutine(GuardBreakIndicatorPulseCo());
        }

        /// <summary>
        /// Sembunyikan indikator Guard Break (objeknya tidak di-destroy supaya
        /// bisa dipakai lagi kalau musuh guard break untuk kedua kalinya).
        /// </summary>
        private void HideGuardBreakIndicator()
        {
            if (indicatorPulseRoutine != null)
            {
                StopCoroutine(indicatorPulseRoutine);
                indicatorPulseRoutine = null;
            }

            if (guardBreakIndicator == null) return;
            guardBreakIndicator.SetActive(false);
        }

        /// <summary>
        /// Hancurkan objek indikator Guard Break (dipanggil saat musuh mati).
        /// </summary>
        private void DestroyGuardBreakIndicator()
        {
            if (indicatorPulseRoutine != null)
            {
                StopCoroutine(indicatorPulseRoutine);
                indicatorPulseRoutine = null;
            }

            if (guardBreakIndicator == null) return;

            Destroy(guardBreakIndicator);
            guardBreakIndicator = null;
            guardBreakIndicatorRenderer = null;
        }

        /// <summary>
        /// Satu denyut indikator: membesar lalu kembali ke ukuran normal,
        /// dengan alpha yang sedikit memudar.
        /// </summary>
        private IEnumerator GuardBreakIndicatorPulseCo()
        {
            if (guardBreakIndicator == null) yield break;

            Transform indicatorTransform = guardBreakIndicator.transform;
            const float pulseDuration = 0.35f;
            float elapsed = 0f;

            while (elapsed < pulseDuration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / pulseDuration);

                // Satu denyut: 0.55 -> 1.4 -> 1.0
                float scale = progress < 0.5f
                    ? Mathf.Lerp(0.55f, 1.4f, progress * 2f)
                    : Mathf.Lerp(1.4f, 1f, (progress - 0.5f) * 2f);

                indicatorTransform.localScale = new Vector3(scale, scale, 1f);

                if (guardBreakIndicatorRenderer != null)
                {
                    Color indicatorColor = GuardBreakIndicatorColor;
                    indicatorColor.a = Mathf.Lerp(1f, 0.65f, progress);
                    guardBreakIndicatorRenderer.color = indicatorColor;
                }

                yield return null;
            }

            if (guardBreakIndicatorRenderer != null)
            {
                guardBreakIndicatorRenderer.color = GuardBreakIndicatorColor;
            }

            indicatorPulseRoutine = null;
        }

        /// <summary>
        /// Membuat sprite indikator Guard Break (lingkaran merah) secara
        /// prosedural.
        ///
        /// Sprite di-cache secara STATIC supaya teksturnya hanya dibuat SATU
        /// KALI lalu dipakai bersama oleh semua musuh di scene.
        /// Filter mode = Point supaya tepinya tetap tajam (tidak blur).
        /// </summary>
        private static Sprite GetGuardBreakIndicatorSprite()
        {
            if (guardBreakIndicatorSprite != null) return guardBreakIndicatorSprite;

            const int size = 32;
            const float pixelsPerUnit = 32f;

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "GuardBreakIndicatorTexture"
            };

            Color32[] pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float outerRadius = size * 0.44f;
            float innerRadius = size * 0.26f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    // Cincin: berlubang di tengah, solid di cincin, transparan di luar.
                    float alpha = (distance <= outerRadius && distance >= innerRadius) ? 1f : 0f;

                    // Tepi luar di-soft-kan 1 piksel supaya tidak bergerigi.
                    float outerEdge = outerRadius - distance;
                    if (outerEdge >= 0f && outerEdge < 1f) alpha = outerEdge;

                    // Tepi dalam (lubang) juga di-soft-kan.
                    float innerEdge = distance - innerRadius;
                    if (distance >= innerRadius && innerEdge < 1f) alpha = Mathf.Min(alpha, innerEdge);

                    byte alphaByte = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255);
                    pixels[y * size + x] = new Color32(255, 255, 255, alphaByte);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            guardBreakIndicatorSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit);
            guardBreakIndicatorSprite.name = "GuardBreakIndicatorSprite";

            return guardBreakIndicatorSprite;
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

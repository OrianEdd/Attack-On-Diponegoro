using System.Collections;
using UnityEngine;

namespace PGIM.Sekiro2D
{
    public class PlayerCombat : MonoBehaviour
    {
        [Header("Combat Settings - 3-Hit Combo")]
        [SerializeField] private Transform attackPoint;
        [SerializeField] private float attackRadius = 0.8f;
        [SerializeField] private LayerMask enemyLayer;
        [SerializeField] private float baseDamage = 25f;
        [SerializeField] private float basePostureDamage = 15f;
        [SerializeField] private float comboResetTime = 1.2f;
        [SerializeField] private float attackDuration = 0.35f;

        [Header("Combat Settings - Parry / Deflect")]
        [SerializeField] private float parryTotalDuration = 0.3125f; 
        [SerializeField] private float hitStopDuration = 0.05f;

        // Nama state / parameter Animator Player (lihat Assets/Animations/Player/Player.controller).
        // PENTING: nama state di controller adalah "Defend" (BUKAN "Block"), dan
        // parameter boolean-nya bernama "IsBlocking". Nilai-nilai ini dideklarasikan
        // di sini sebagai konstanta agar tidak ada lagi penamaan yang menebak.
        private const string AnimStateParry = "Parry";
        private const string AnimStateDefend = "Defend";
        private const string AnimStateDeath = "Death";
        private const string AnimParamIsBlocking = "IsBlocking";
        private const string AnimTriggerAttack1 = "Attack1";
        private const string AnimTriggerAttack2 = "Attack2";
        private const string AnimTriggerAttack3 = "Attack3";
        private const string AnimTriggerParry = "Parry";
        private const string AnimTriggerHurt = "Hurt";
        private const string AnimTriggerDeath = "Death";
        private const string AnimTriggerJump = "Jump";
        private const string AnimTriggerDodge = "Dodge";

        [Header("Perfect Parry / Deflect Settings")]
        // Jendela waktu Perfect Parry (detik) sejak frame pertama tombol defend ditekan.
        // Diperbesar (0.30 - 0.32 detik) agar pemain bisa menekan parry tepat saat
        // senjata musuh hendak menyentuh badan (momen benturan ayunan).
        [SerializeField] private float parryWindow = 0.32f;
        // Posture yang didapat pemain saat parry sukses (sangat kecil, tidak memicu Guard Break).
        [SerializeField] private float parryPostureDamageOnSuccess = 2f;
        // Posture damage yang dikembalikan ke musuh saat parry sukses.
        [SerializeField] private float parryPostureDamageToEnemy = 30f;
        // Durasi hit-stop saat parry/block sukses.
        [SerializeField] private float deflectHitStopDuration = 0.12f;

        [Header("Health & Posture Stats")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth;
        [SerializeField] private float maxPosture = 100f;
        [SerializeField] private float currentPosture;
        [SerializeField] private float postureRecoveryRate = 15f;
        [SerializeField] private float postureRecoveryDelay = 1.5f;

        [Header("Guard Break / Stagger Settings")]
        // Durasi pemain tidak bisa menyerang/block setelah posture pecah.
        [SerializeField] private float guardBreakDuration = 1.2f;

        [Header("References")]
        [SerializeField] private Animator anim;
        [SerializeField] private PlayerController playerController;
        [SerializeField] private Rigidbody2D rb;

        public enum AttackType { Normal, BlueParryOnly, RedUnblockable }

        public bool IsAttacking { get; private set; }
        public bool IsParrying { get; private set; }
        public bool IsBlocking { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsHurt { get; private set; }
        // True saat pemain sedang mengalami Guard Break / Stagger (posture penuh).
        public bool IsStaggered { get; private set; }

        private int comboStep = 0;
        private float lastAttackTime;
        private bool attackRequested;

        private Coroutine attackRoutine;
        private Coroutine parryRoutine;
        private Coroutine guardBreakRoutine;
        private bool isParryActiveWindow;
        private float postureTimer;
        // True selama hit-stop (Time.timeScale = 0) sedang berjalan. Dipakai sebagai
        // penjaga agar dua hit-stop tidak tumpang tindih dan timeScale tidak nyangkut 0f.
        private bool isHitStopActive;
        // True saat pemain sedang mengalami Guard Break / Stagger (alias dari IsStaggered).
        // Disimpan sebagai field terpisah agar status stagger bisa di-reset dengan aman
        // dari mana pun (mis. saat coroutine dibatalkan / object dinonaktifkan).
        private bool isGuardBroken;

        // Waktu (Time.time) saat jendela Perfect Parry dibuka / ditutup.
        private float parryWindowStartTime = -999f;
        private float parryWindowEndTime = -999f;
        // True saat tombol defend sedang ditahan (dipakai untuk logika posture & guard break).
        private bool isDefendHeld;
        // True ketika pemain sedang MENAHAN tombol defend (setelah menekan parry).
        // Dipakai sebagai syarat transitioning Parry -> Defend setelah jendela parry
        // selesai. Hanya di-reset ke false ketika tombol benar-benar dilepas
        // (atau saat pemain terkena hit / stagger / mati).
        private bool isParryHeld;
        // Nilai parameter Animator "IsBlocking" yang TERPAKAI ke Animator.
        //
        // PENTING: ini sengaja dipisahkan dari property IsBlocking (logika game).
        // Di controller Player, state "Parry" punya DUA transisi:
        //   - Parry -> Defend : kondisi IsBlocking == TRUE , exitTime 0.75 (butuh 75% clip)
        //   - Parry -> Idle   : kondisi IsBlocking == FALSE, exitTime 0 (langsung fired!)
        // Karena transisi ke "Idle" tidak punya exit time, begitu IsBlocking bernilai
        // FALSE saat masih di state "Parry", animasi parry LANGSUNG terpotong ke Idle.
        // Itu adalah akar bug "hold-defend" (animasi parry terputus & Defend tak pernah tampil).
        //
        // Solusi: parameter Animator "IsBlocking" diset TRUE sejak parry dimulai, sehingga
        // satu-satunya jalan keluar dari "Parry" adalah "Parry -> Defend". Parameter itu
        // baru diset FALSE saat tombol defend dilepas (-> transisi "Defend -> Idle").
        private bool isAnimatorBlocking;

        public event System.Action<float, float> OnHealthChanged;
        public event System.Action<float, float> OnPostureChanged;
        public event System.Action OnPlayerDeath;

        private void Awake()
        {
            if (anim == null) anim = GetComponent<Animator>();
            if (playerController == null) playerController = GetComponent<PlayerController>();
            if (rb == null) rb = GetComponent<Rigidbody2D>();

            if (attackPoint == null)
            {
                Transform found = transform.Find("AttackPoint");
                if (found != null) attackPoint = found;
                else Debug.LogWarning("[PlayerCombat] AttackPoint transform not assigned!");
            }

            currentHealth = maxHealth;
            currentPosture = 0f; // Initial posture is 0 (Sekiro style: posture starts at 0 and builds up)

            // Terapkan nilai timing kanonik agar perilaku konsisten di scene mana pun
            // (nilai yang ter-serialize di scene bisa saja hasil tuning lama).
            ApplyCombatTimingSettings();
        }

        /// <summary>
        /// Menegakkan nilai timing Perfect Parry yang benar (sinkron dengan frame impact
        /// serangan musuh). Nilai di sini adalah sumber kebenaran dan ditulis ulang saat
        /// Awake supaya jendela parry tidak lagi bergantung pada nilai lama di scene.
        /// </summary>
        private void ApplyCombatTimingSettings()
        {
            // Jendela Perfect Parry diperbesar (0.30 - 0.32 detik) agar pemain bisa
            // menekan parry tepat saat senjata musuh hendak menyentuh badan.
            parryWindow = CombatTiming.ParryWindow;

            // Posture damage yang dikembalikan ke musuh saat parry sukses.
            parryPostureDamageToEnemy = CombatTiming.ParryPostureDamageToEnemy;
        }

        private void Update()
        {
            if (IsDead) return;

            bool defendPressed = (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.K));
            bool defendHeld = (Input.GetMouseButton(1) || Input.GetKey(KeyCode.K));
            isDefendHeld = defendHeld;

            // Cek apakah pemain boleh memulai parry baru di frame ini.
            //
            // PENTING (Attack Cancel ala Sekiro): syarat "!IsAttacking" SUDAH DIHAPUS.
            // Menekan tombol defend/parry di tengah animasi serangan akan membatalkan
            // serangan yang sedang berjalan dan langsung masuk ke mode Parry.
            bool canStartParry = defendPressed
                && !IsParrying
                && !IsHurt
                && !IsStaggered
                && !isGuardBroken
                && (playerController == null || (playerController.IsGrounded && !playerController.IsDashing));

            // Attack Cancel: tekan defend saat sedang menyerang -> batalkan serangan
            // (_IsAttacking, attackRequested, attackRoutine, trigger Attack1/2/3)_ lalu
            // seketika masuk ke mode Parry.
            if (canStartParry && IsAttacking)
            {
                CancelActiveAttack();
            }

            // Kelola jendela Perfect Parry + transisi hold ke Defend
            UpdateParryWindow(canStartParry, defendHeld);

            // Regenerasi posture alami
            UpdatePostureRecovery();

            // Input serangan. Diblokir selama parry / hurt / stagger.
            // Perhatikan: pemeriksaan ini dilakukan SESUDAH UpdateParryWindow()
            // supaya bila pemain menekan attack & defend di frame yang sama,
            // defend (parry) yang menang dan serangan TIDAK ikut terpicu.
            if ((Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.J)) && !IsParrying && !IsHurt && !IsStaggered)
            {
                if (playerController != null && (!playerController.IsGrounded || playerController.IsDashing)) return;

                if (Time.time - lastAttackTime > comboResetTime) comboStep = 0;
                attackRequested = true;
            }

            if (attackRequested && !IsAttacking && !IsStaggered)
            {
                ExecuteAttack();
            }
        }

        private void UpdateParryWindow(bool startNewParry, bool defendHeld)
        {
            // 1. Tombol BARU ditekan & kondisi valid -> mulai jendela parry + putar animasi instan
            if (startNewParry)
            {
                IsParrying = true;
                isParryActiveWindow = true;
                isParryHeld = true;
                parryWindowStartTime = Time.time;
                parryWindowEndTime = Time.time + Mathf.Max(0.05f, parryWindow);

                // Logika game: belum "blocking" sungguhan, masih di mode parry.
                IsBlocking = false;

                // Parameter Animator "IsBlocking" di-set TRUE di sini (BUKAN false).
                // Lihat penjelasan pada field isAnimatorBlocking: dengan cara ini,
                // transisi "Parry -> Idle" (yang melompati exit time) tidak akan
                // memotong animasi parry, dan satu-satunya transisi keluar dari
                // "Parry" adalah "Parry -> Defend" pada 75% durasi clip.
                SetAnimatorBlocking(true);

                StartParry();

                if (parryRoutine != null) StopCoroutine(parryRoutine);
                parryRoutine = StartCoroutine(ParryRoutineCo());
                return;
            }

            // 2. Tombol DILEPAS -> keluar dari parry & blokir.
            //    Parameter Animator "IsBlocking" juga dikembalikan ke false supaya
            //    transisi "Defend -> Idle" / "Parry -> Idle" langsung bekerja.
            if (!defendHeld)
            {
                isParryHeld = false;
                if (IsBlocking)
                {
                    IsBlocking = false;
                    SetAnimatorBlocking(false);
                }
                else if (IsParrying)
                {
                    // Masih di mode parry (tombol dilepas sebelum jendela parry selesai):
                    // lepaskan "IsBlocking" Animator supaya animasi parry kembali normal.
                    SetAnimatorBlocking(false);
                }
            }

            // 3. Jendela parry sedang berjalan
            bool parryWindowActive = IsParrying && Time.time <= parryWindowEndTime;
            if (parryWindowActive)
            {
                isParryActiveWindow = Time.time >= parryWindowStartTime;
                // Selama jendela parry, logika game tetap "bukan blocking"
                // (tidak menahan damage biasa), namun parameter Animator tetap TRUE
                // agar animasi parry tidak terpotong ke Idle.
                IsBlocking = false;
                return;
            }

            // 4. Jendela parry SELESAI -> tutup status parry.
            //    Perhatikan: "isParryHeld" TIDAK diubah di sini. Flag ini berarti
            //    "pemain sedang menahan tombol defend", jadi harus tetap TRUE selama
            //    tombol ditahan (dipakai sebagai syarat masuk ke pose Defend).
            isParryActiveWindow = false;
            if (IsParrying)
            {
                IsParrying = false;
                if (parryRoutine != null)
                {
                    StopCoroutine(parryRoutine);
                    parryRoutine = null;
                }
            }

            // 5. Jika tombol MASIH ditahan setelah jendela parry lewat -> langsung masuk pose Defend.
            //    Parameter Animator "IsBlocking" sudah TRUE sejak langkah 1, sehingga
            //    transisi "Parry -> Defend" (exitTime 0.75) sudah / akan mengantar pemain
            //    ke state "Defend" dan MENAHAN di sana selama tombol ditahan.
            //    CrossFade eksplisit ke "Defend" dipakai sebagai jaring pengaman,
            //    kalau mis. animasi parry sempat ter-interupsi.
            //
            //    Syarat "isParryHeld" memastikan transisi Parry -> Defend hanya terjadi
            //    kalau pemain memang masih menahan tombol defend.
            if (defendHeld && isParryHeld && !IsStaggered && !isGuardBroken && !IsHurt && !IsAttacking && !IsDead)
            {
                if (!IsBlocking)
                {
                    IsBlocking = true;
                    SetAnimatorBlocking(true);

                    // Pastikan state "Defend" aktif (nama state di controller memang "Defend").
                    if (anim != null && !anim.GetCurrentAnimatorStateInfo(0).IsName(AnimStateDefend))
                    {
                        anim.CrossFadeInFixedTime(AnimStateDefend, 0.08f, 0, 0f);
                    }
                }
            }
        }

        /// <summary>
        /// Menyalakan / mematikan parameter boolean "IsBlocking" pada Animator.
        ///
        /// Parameter ini dipakai controller Player untuk mengatur transisi:
        ///   - state "Parry" -> "Defend" (IsBlocking == true)
        ///   - state "Parry" -> "Idle"   (IsBlocking == false)
        ///   - state "Defend" -> "Idle"  (IsBlocking == false)
        ///
        /// Method ini juga menyinkronkan cache lokal <see cref="isAnimatorBlocking"/>
        /// supaya kita tidak perlu memanggil anim.GetBool() setiap frame.
        /// </summary>
        private void SetAnimatorBlocking(bool value)
        {
            isAnimatorBlocking = value;
            if (anim != null) anim.SetBool(AnimParamIsBlocking, value);
        }
        private void UpdatePostureRecovery()
        {
            if (currentPosture <= 0f) return;
            if (IsStaggered || IsHurt || IsAttacking || IsParrying) return;

            postureTimer += Time.deltaTime;
            if (postureTimer < postureRecoveryDelay) return;

            currentPosture = Mathf.Max(0f, currentPosture - postureRecoveryRate * Time.deltaTime);
            OnPostureChanged?.Invoke(currentPosture, maxPosture);
        }

        private void Start()
        {
            // Initial UI synchronization
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            OnPostureChanged?.Invoke(currentPosture, maxPosture);
        }

        private void ExecuteAttack()
        {
            attackRequested = false;
            IsAttacking = true;
            lastAttackTime = Time.time;

            // Feedback audio: suara tebasan (whoosh) saat pemain memulai serangan.
            CombatFeedback.PlaySlashSfx();

            if (anim != null)
            {
                // Reset trigger serangan sebelumnya supaya tidak menumpuk.
                anim.ResetTrigger(AnimTriggerAttack1);
                anim.ResetTrigger(AnimTriggerAttack2);
                anim.ResetTrigger(AnimTriggerAttack3);

                if (comboStep == 0) anim.SetTrigger(AnimTriggerAttack1);
                else if (comboStep == 1) anim.SetTrigger(AnimTriggerAttack2);
                else if (comboStep == 2) anim.SetTrigger(AnimTriggerAttack3);
            }

            TriggerAttackHitbox();

            comboStep = (comboStep + 1) % 3;

            if (attackRoutine != null) StopCoroutine(attackRoutine);
            attackRoutine = StartCoroutine(AttackTimerCo());
        }

        private IEnumerator AttackTimerCo()
        {
            yield return new WaitForSeconds(attackDuration);
            IsAttacking = false;

            if (attackRequested && Time.time - lastAttackTime <= comboResetTime)
            {
                ExecuteAttack();
            }
            else if (Time.time - lastAttackTime > comboResetTime)
            {
                comboStep = 0;
            }
        }

        public void TriggerAttackHitbox()
        {
            if (attackPoint == null) return;

            Collider2D[] hitEnemies = Physics2D.OverlapCircleAll(attackPoint.position, attackRadius, enemyLayer);
            foreach (Collider2D enemy in hitEnemies)
            {
                EnemyKlewang enemyKlewang = enemy.GetComponent<EnemyKlewang>();
                if (enemyKlewang != null)
                {
                    Debug.Log($"[PlayerCombat] Hit enemy: {enemy.name} with damage {baseDamage} & posture damage {basePostureDamage}");
                    enemyKlewang.TakeDamage(baseDamage, basePostureDamage, transform.position);

                    // Feedback: suara impact + percikan api merah/oranye kecil di posisi musuh.
                    CombatFeedback.PlayHitFleshSfx();
                    CombatFeedback.SpawnSparks(
                        enemyKlewang.transform.position,
                        new Color(1f, 0.35f, 0.1f),
                        10,
                        0.12f);
                }
                else
                {
                    Debug.Log($"[PlayerCombat] Hit enemy: {enemy.name} with damage {baseDamage}");
                }
            }
        }

        /// <summary>
        /// Membatalkan serangan yang sedang berjalan (Attack Cancel ala Sekiro).
        ///
        /// Dipanggil ketika pemain menekan tombol defend/parry di tengah animasi
        /// serangan. Effect:
        ///   1. IsAttacking = false            -> pemain tidak lagi "menyerang"
        ///   2. attackRequested = false        -> combo yang antre dibatalkan
        ///   3. attackRoutine dihentikan       -> timer serangan tidak lanjut
        ///   4. trigger Attack1/2/3 di-reset    -> animasi serangan tidak akan
        ///      terpicu lagi oleh transisi AnyState -> Attack1/2/3
        ///
        /// Setelah ini, pemanggil dapat langsung masuk ke mode Parry.
        /// </summary>
        private void CancelActiveAttack()
        {
            // Batalkan antrean combo.
            attackRequested = false;

            // Hentikan timer serangan yang sedang berjalan.
            if (attackRoutine != null)
            {
                StopCoroutine(attackRoutine);
                attackRoutine = null;
            }

            IsAttacking = false;

            // Reset trigger serangan di Animator supaya transisi AnyState -> Attack*
            // tidak terpicu ulang (memicu ulang akan mengulang animasi serangan).
            if (anim != null)
            {
                anim.ResetTrigger(AnimTriggerAttack1);
                anim.ResetTrigger(AnimTriggerAttack2);
                anim.ResetTrigger(AnimTriggerAttack3);
            }

            // Kembalikan combo ke awal, karena serangan dibatalkan di tengah jalan.
            comboStep = 0;
        }

        /// <summary>
        /// Memulai animasi parry saat tombol defend ditekan.
        ///
        /// CATATAN: status parry (IsParrying / jendela waktu) TIDAK diset di sini,
        /// melainkan dikelola oleh <see cref="UpdateParryWindow"/> yang dipanggil
        /// lebih dulu di Update(). Method ini HANYA bertanggung jawab untuk
        /// memutar animasi Parry secara INSTAN tanpa jeda transisi.
        /// </summary>
        private void StartParry()
        {
            if (anim != null)
            {
                // Jangan panggil SetTrigger("Parry") bersamaan dengan CrossFade agar trigger tidak nyangkut
                anim.ResetTrigger(AnimTriggerParry);

                // CrossFade instan (durasi 0) ke state "Parry" supaya animasi parry
                // langsung tampil pada frame tombol ditekan, tanpa jeda transisi.
                anim.CrossFadeInFixedTime(AnimStateParry, 0f, 0, 0f);
            }
        }

        /// <summary>
        /// Routine pendukung parry. Loop-nya memakai WaitForSecondsRealtime sehingga
        /// TIDAK ikut membeku saat Time.timeScale = 0 (hit-stop). Kalau memakai
        /// Time.deltaTime, routine ini akan menggantung selamanya selama hit-stop dan
        /// flag IsParrying tidak pernah ter-reset -> karakter terkunci.
        /// </summary>
        private IEnumerator ParryRoutineCo()
        {
            // Trigger "Parry" TIDAK dipanggil di sini. Animasi sudah diputar secara
            // instan di StartParry() pada frame saat tombol ditekan.
            float elapsed = 0f;
            while (elapsed < parryTotalDuration)
            {
                // Keluar lebih awal bila tombol defend sudah dilepas.
                if (!isDefendHeld && !Input.GetMouseButton(1) && !Input.GetKey(KeyCode.K))
                {
                    break;
                }

                // Pakai waktu realtime agar tidak menggantung saat hit-stop.
                elapsed += Time.unscaledDeltaTime;

                // Jendela Perfect Parry tetap aktif sampai parryWindowEndTime.
                isParryActiveWindow = IsParrying && Time.time >= parryWindowStartTime && Time.time <= parryWindowEndTime;

                yield return null;
            }

            // Status parry & jendela dibersihkan di sini juga, sebagai jaring
            // pengaman bila UpdateParryWindow() tidak sempat berjalan.
            isParryActiveWindow = false;
            IsParrying = false;
            parryRoutine = null;

            // Kalau tombol defend sudah TIDAK ditahan lagi, lepaskan juga "isParryHeld"
            // (flag "pemain menahan tombol defend") dan parameter Animator "IsBlocking"
            // supaya transisi "Parry -> Idle" / "Defend -> Idle" langsung mengembalikan
            // animasi ke normal.
            if (!isDefendHeld && !Input.GetMouseButton(1) && !Input.GetKey(KeyCode.K))
            {
                isParryHeld = false;
                IsBlocking = false;
                SetAnimatorBlocking(false);
            }
        }

        public void TakeDamage(float damage, float postureDamage, Vector2 attackerPosition)
        {
            if (IsDead) return;

            // If player is currently dodging/invulnerable, ignore all damage and posture damage
            if (playerController != null && playerController.IsInvulnerable)
            {
                Debug.Log("[PlayerCombat] Dodge successful! Damage and Posture avoided.");
                return;
            }

            postureTimer = 0f; // Reset posture recovery delay on taking hit/parry

            // 1. Check Perfect Parry (Deflect) -- HARUS dicek SEBELUM cabang Block.
            if (IsParrying && isParryActiveWindow)
            {
                HandlePerfectParry(damage, postureDamage, attackerPosition);
                return;
            }

            // 2. Check Block (Holding defend/block button)
            if (IsBlocking)
            {
                Debug.Log("[PlayerCombat] SUCCESSFUL BLOCK!");
                currentPosture = Mathf.Min(maxPosture, currentPosture + postureDamage); // Blocking increases posture more
                OnPostureChanged?.Invoke(currentPosture, maxPosture);
                StartCoroutine(HitStopCo(hitStopDuration));

                // Feedback: suara block (thud tumpul) + percikan putih/abu-abu kecil
                // + getaran kamera ringan.
                CombatFeedback.PlayBlockSfx();
                CombatFeedback.SpawnSparks(
                    transform.position,
                    new Color(0.85f, 0.85f, 0.9f),
                    8,
                    0.1f);
                CombatFeedback.ShakeCamera(0.06f, 0.05f);

                // Check if posture breaks from blocking -> GUARD BREAK (bukan death!)
                if (currentPosture >= maxPosture)
                {
                    StartGuardBreak();
                }
                return;
            }

            // 3. Unblocked hit: kena damage HP + posture damage
            currentHealth = Mathf.Max(0f, currentHealth - damage);
            currentPosture = Mathf.Min(maxPosture, currentPosture + postureDamage);

            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            OnPostureChanged?.Invoke(currentPosture, maxPosture);

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
        /// Perfect Parry / Deflect: HP pemain tidak berkurang, posture naik sangat sedikit
        /// (dan dijaga agar tidak pernah memicu Guard Break), musuh menerima posture
        /// damage balik, serta efek hit-stop dan animasi deflect terpicu.
        /// </summary>
        private void HandlePerfectParry(float damage, float postureDamage, Vector2 attackerPosition)
        {
            Debug.Log("[PlayerCombat] SUCCESSFUL PARRY (DEFLECT)!");

            // HP pemain TIDAK berkurang sama sekali saat parry sukses.
            OnHealthChanged?.Invoke(currentHealth, maxHealth);

            // Posture naik sangat sedikit, dan dibatasi agar tidak pernah mencapai maxPosture
            // (sehingga parry sukses tidak bisa memicu Guard Break).
            float postureGain = Mathf.Min(parryPostureDamageOnSuccess, Mathf.Max(0f, maxPosture - 1f));
            currentPosture = Mathf.Min(maxPosture - 0.01f, currentPosture + postureGain);
            OnPostureChanged?.Invoke(currentPosture, maxPosture);

            // Musuh menerima posture damage balik + reaksi deflect.
            EnemyKlewang enemyKlewang = FindEnemyKlewang();
            if (enemyKlewang != null)
            {
                enemyKlewang.ReceiveParryPostureDamage(parryPostureDamageToEnemy, transform.position);
            }

            // Feedback Perfect Parry: metallic ping nyaring + percikan emas terang
            // di titik tengah antara pemain dan musuh + getaran kamera.
            CombatFeedback.PlayPerfectParrySfx();
            CombatFeedback.SpawnSparks(
                GetParrySparkPosition(enemyKlewang),
                new Color(1f, 0.85f, 0.2f),
                26,
                0.16f);
            CombatFeedback.ShakeCamera(0.12f, 0.14f);

            // Efek hit-stop + konfirmasi visual deflect.
            StartCoroutine(HitStopCo(deflectHitStopDuration));

            // Animasi Parry TIDAK di-restart di sini. State "Parry" sudah diputar
            // secara instan sejak frame tombol ditekan (lihat StartParry), sehingga
            // pada momen deflect pemain melihat pose parry yang sedang berlangsung
            // tanpa terputus / diulang.
            //
            // Parameter Animator "IsBlocking" TIDAK diubah di sini. Nilai itu tetap TRUE
            // supaya transisi "Parry -> Defend" tetap bisa terjadi (parry sukses bukan
            // berarti tombol dilepas). Logika game IsBlocking tetap FALSE karena kita
            // masih berada di dalam jendela parry.
        }

        /// <summary>
        /// Menghitung posisi percikan untuk efek Perfect Parry.
        ///
        /// Posisi yang dipakai adalah TITIK TENGAH antara pemain dan musuh
        /// (tempat kedua bilah Pedang bertemu), supaya efek terlihat tepat di
        /// lokasi benturan. Kalau musuh tidak ditemukan, posisi pemain dipakai
        /// sebagai fallback.
        /// </summary>
        private Vector3 GetParrySparkPosition(EnemyKlewang enemyKlewang)
        {
            Vector3 playerPosition = transform.position;

            if (enemyKlewang == null) return playerPosition;

            return (playerPosition + enemyKlewang.transform.position) * 0.5f;
        }

        /// <summary>
        /// Mencari komponen EnemyKlewang terdekat (untuk memberikan posture damage balik saat parry).
        /// </summary>
        private EnemyKlewang FindEnemyKlewang()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, 2.5f);
            foreach (Collider2D hit in hits)
            {
                EnemyKlewang enemy = hit.GetComponent<EnemyKlewang>();
                if (enemy != null) return enemy;
            }
            return null;
        }

        private IEnumerator HurtRoutineCo()
        {
            IsHurt = true;
            IsAttacking = false;
            IsParrying = false;
            isParryActiveWindow = false;
            isParryHeld = false;
            attackRequested = false;

            // Hentikan coroutine serangan & parry yang mungkin masih berjalan.
            if (attackRoutine != null)
            {
                StopCoroutine(attackRoutine);
                attackRoutine = null;
            }
            if (parryRoutine != null)
            {
                StopCoroutine(parryRoutine);
                parryRoutine = null;
            }

            // Lepaskan mode block (logika game + parameter Animator) supaya state
            // "Defend" bisa kembali ke "Idle".
            IsBlocking = false;
            SetAnimatorBlocking(false);

            if (anim != null)
            {
                anim.ResetTrigger(AnimTriggerAttack1);
                anim.ResetTrigger(AnimTriggerAttack2);
                anim.ResetTrigger(AnimTriggerAttack3);
                anim.SetTrigger(AnimTriggerHurt);
            }

            // Realtime supaya durasi hurt tetap berjalan meski ada hit-stop
            // (Time.deltaTime = 0 saat timeScale = 0 -> routine menggantung).
            yield return new WaitForSecondsRealtime(0.25f);

            // WAJIB di-reset, kalau tidak IsHurt = true membuat karakter beku.
            IsHurt = false;
        }

        /// <summary>
        /// Guard Break / Stagger: dipanggil ketika currentPosture mencapai maxPosture.
        /// Pemain TIDAK mati, tapi menjadi stagger selama <see cref="guardBreakDuration"/> detik
        /// (tidak bisa menyerang/block), memutar animasi Hurt, lalu posture di-reset ke 0.
        /// </summary>
        private void StartGuardBreak()
        {
            if (IsStaggered) return;

            Debug.Log("[PlayerCombat] GUARD BREAK! Posture penuh -> Stagger (bukan death).");
            IsStaggered = true;
            isGuardBroken = true;
            IsAttacking = false;
            IsParrying = false;
            IsBlocking = false;
            isParryActiveWindow = false;
            isParryHeld = false;
            attackRequested = false;

            // Feedback: suara retak guard + percikan oranye-merah besar + getaran
            // kamera yang kuat (guard break = momen berat).
            CombatFeedback.PlayGuardBreakSfx();
            CombatFeedback.SpawnSparks(
                transform.position,
                new Color(1f, 0.45f, 0.1f),
                34,
                0.22f);
            CombatFeedback.ShakeCamera(0.18f, 0.2f);

            // Lepaskan mode block pada parameter Animator supaya state "Defend"
            // bisa kembali ke "Idle" (transisi "Defend -> Idle" butuh IsBlocking == false).
            SetAnimatorBlocking(false);

            if (anim != null)
            {
                anim.ResetTrigger(AnimTriggerAttack1);
                anim.ResetTrigger(AnimTriggerAttack2);
                anim.ResetTrigger(AnimTriggerAttack3);
                anim.ResetTrigger(AnimTriggerParry);
                anim.SetTrigger(AnimTriggerHurt);
            }

            // Hentikan serangan / parry yang sedang berjalan.
            if (attackRoutine != null)
            {
                StopCoroutine(attackRoutine);
                attackRoutine = null;
            }
            if (parryRoutine != null)
            {
                StopCoroutine(parryRoutine);
                parryRoutine = null;
            }

            if (guardBreakRoutine != null) StopCoroutine(guardBreakRoutine);
            guardBreakRoutine = StartCoroutine(GuardBreakRoutineCo());
        }

        private IEnumerator GuardBreakRoutineCo()
        {
            // Pakai realtime supaya durasi stagger tetap berjalan meski ada hit-stop
            // di tengah-tengah (Time.deltaTime akan 0 saat timeScale = 0).
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, guardBreakDuration));

            // Posture di-reset ke 0 setelah stagger selesai.
            currentPosture = 0f;
            OnPostureChanged?.Invoke(currentPosture, maxPosture);

            // Flag stagger WAJIB di-reset, kalau tidak karakter terkunci selamanya
            // dan tidak bisa bergerak / menyerang lagi.
            IsStaggered = false;
            isGuardBroken = false;
            guardBreakRoutine = null;
        }

        /// <summary>
        /// Reset aman untuk seluruh flag pengunci gerak / aksi. Dipanggil dari
        /// OnDisable / OnDestroy supaya tidak ada state yang tertinggal dan
        /// karakter tidak terkunci selamanya.
        /// </summary>
        private void ResetAllStateFlags()
        {
            IsAttacking = false;
            IsParrying = false;
            IsBlocking = false;
            IsHurt = false;
            IsStaggered = false;
            isGuardBroken = false;
            isParryActiveWindow = false;
            isParryHeld = false;
            isDefendHeld = false;
            isHitStopActive = false;
            attackRequested = false;

            attackRoutine = null;
            parryRoutine = null;
            guardBreakRoutine = null;

            // Lepaskan juga parameter Animator "IsBlocking" supaya state "Defend"
            // tidak nyangkut ketika objek diaktifkan kembali.
            SetAnimatorBlocking(false);
        }

        private void Die()
        {
            if (IsDead) return;
            IsDead = true;

            // Pastikan hit-stop (timeScale = 0) dibatalkan agar animasi Death berjalan normal
            CancelAllHitStops();

            // WAJIB: waktu dunia harus kembali normal (timeScale = 1f).
            // Kalau kematian terjadi DI TENGAH hit-stop, timeScale bisa tertinggal di 0f
            // dan animasi "Death" tidak akan pernah benar-benar diputar.
            Time.timeScale = 1f;

            ResetAllStateFlags();

            if (anim != null)
            {
                // Reset SEMUA trigger lama supaya tidak ada trigger nyangkut yang
                // bisa mengganggu transisi AnyState -> Death.
                anim.ResetTrigger(AnimTriggerAttack1);
                anim.ResetTrigger(AnimTriggerAttack2);
                anim.ResetTrigger(AnimTriggerAttack3);
                anim.ResetTrigger(AnimTriggerParry);
                anim.ResetTrigger(AnimTriggerHurt);
                anim.ResetTrigger(AnimTriggerJump);
                anim.ResetTrigger(AnimTriggerDodge);

                SetAnimatorBlocking(false);

                // Putar animasi Death secara instan (CrossFade) supaya tidak
                // bergantung pada transisi AnyState yang bisa terblokir trigger lain.
                anim.SetTrigger(AnimTriggerDeath);
                anim.CrossFadeInFixedTime(AnimStateDeath, 0.05f, 0, 0f);
            }

            if (playerController != null) playerController.enabled = false;
            if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

            OnPlayerDeath?.Invoke();
            Debug.Log("[PlayerCombat] Player died!");
        }

        /// <summary>
        /// Hit-stop: membekukan waktu sesaat untuk memberi kesan benturan terasa berat.
        ///
        /// WAJIB menggunakan WaitForSecondsRealtime, karena Time.timeScale diset 0f
        /// (sehingga Time.deltaTime = 0 dan WaitForSeconds biasa tidak akan pernah
        /// selesai -> game beku permanen).
        ///
        /// Keamanan anti-beku:
        ///   1. Memulihkan timeScale ke nilai SEBELUM hit-stop (bukan ke 1f), supaya
        ///      hit-stop yang tumpang tindih tidak mengembalikan waktu ke 0f.
        ///   2. Nilai asli disimpan per-instance, sehingga dua hit-stop yang
        ///      tumpang tindih tidak saling menimpa.
        ///   3. OnDisable() memulihkan timeScale ke 1f, sehingga koroutine yang
        ///      dibatalkan / object di-disable tidak menyisakan timeScale = 0f.
        /// </summary>
        private IEnumerator HitStopCo(float duration)
        {
            // Jangan mulai hit-stop baru kalau sedang ada hit-stop yang belum selesai,
            // agar tidak saling menimpa nilai timeScale asli.
            if (isHitStopActive) yield break;

            isHitStopActive = true;
            float timeScaleBeforeHitStop = Time.timeScale;
            Time.timeScale = 0f;

            // WaitForSecondsRealtime TETAP berjalan walau timeScale = 0.
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, duration));

            // Selalu pulihkan (meski coroutine ini dihentikan paksa, baris di bawah
            // tidak akan tereksekusi; jalur OnDisable() yang menjamin pemulihan).
            if (timeScaleBeforeHitStop > 0.0001f) Time.timeScale = timeScaleBeforeHitStop;
            else Time.timeScale = 1f;

            isHitStopActive = false;
        }

        /// <summary>
        /// Memastikan tidak ada hit-stop yang menggantung. Dipanggil saat object
        /// dinonaktifkan / dihancurkan, sehingga Time.timeScale TIDAK PERNAH
        /// tertinggal di 0f (yang membuat semua karakter membeku).
        /// </summary>
        private void CancelAllHitStops()
        {
            isHitStopActive = false;

            // Hentikan semua coroutine (termasuk HitStopCo yang sedang berjalan).
            StopAllCoroutines();
            attackRoutine = null;
            parryRoutine = null;
            guardBreakRoutine = null;

            // Jaring pengaman terakhir: pastikan waktu dunia normal kembali.
            if (Time.timeScale <= 0.0001f) Time.timeScale = 1f;
        }

        private void OnDisable()
        {
            // WAJIB: jangan biarkan Time.timeScale nyangkut di 0f. Kalau object ini
            // dinonaktifkan di tengah hit-stop, seluruh game akan membeku.
            CancelAllHitStops();

            // Reset semua flag pengunci gerak / aksi supaya karakter tidak
            // terkunci selamanya bila object diaktifkan kembali.
            ResetAllStateFlags();
        }

        private void OnDestroy()
        {
            CancelAllHitStops();
        }

        private void OnDrawGizmosSelected()
        {
            if (attackPoint != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(attackPoint.position, attackRadius);
            }
        }
    }
}

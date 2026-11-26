using System.Collections;
using UnityEngine;

namespace PGIM.Sekiro2D
{
    /// <summary>
    /// Sistem feedback audio + visual untuk mekanik combat.
    ///
    /// Komponen ini SENGAJA dibuat mandiri (standalone) dan auto-spawn:
    ///   - Tidak perlu setup manual di scene (.unity), tidak perlu prefab eksternal.
    ///   - Semua efek (camera shake, percikan api, SFX) dibangkitkan saat runtime.
    ///
    /// Alur penggunaan:
    ///   1. Scene dipanggil -> Awake() mencari instance lain; kalau tidak ada, membuat
    ///      GameObject baru (CombatFeedback + AudioSource) lalu DontDestroyOnLoad.
    ///   2. PlayerCombat / EnemyKlewang memanggil API statis (CombatFeedback.PlaySlashSfx(),
    ///      CombatFeedback.SpawnSparks(), CombatFeedback.ShakeCamera()) yang otomatis
    ///      me-resolve singleton di atas.
    ///
    /// Catatan penting soal timing:
    ///   - Hit-stop di PlayerCombat menyetel Time.timeScale = 0f. Karena itu camera
    ///     shake WAJIB memakai Time.unscaledDeltaTime, dan particle system memakai
    ///     simulation speed unscaled, supaya efek tetap terlihat saat dunia beku.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class CombatFeedback : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // Inspector slots (opsional). Kalau diisi, clip tersebut dipakai apa adanya.
        // Kalau kosong (null), AudioClip sintetis dibuat otomatis di Awake().
        // ------------------------------------------------------------------
        [Header("SFX Slots (Kosongkan untuk SFX Sintetis Otomatis)")]
        [SerializeField] private AudioClip slashSfx;
        [SerializeField] private AudioClip perfectParrySfx;
        [SerializeField] private AudioClip blockSfx;
        [SerializeField] private AudioClip hitFleshSfx;
        [SerializeField] private AudioClip guardBreakSfx;
        [SerializeField] private AudioClip deathblowSfx;

        [Header("Audio Settings")]
        [SerializeField] private AudioSource audioSource;
        // Volume multiplier untuk seluruh SFX yang dibangkitkan sistem ini.
        [SerializeField] private float sfxVolume = 0.85f;

        [Header("Camera Shake Settings")]
        // Frekuensi getaran kamera per detik (semakin tinggi = semakin rapat).
        [SerializeField] private float shakeFrequency = 28f;
        // Batas keras durasi satu kali getaran.
        [SerializeField] private float maxShakeDuration = 1.5f;

        [Header("Spark VFX Settings")]
        // Material default untuk particle system. Kalau null, dicoba otomatis
        // memakai shader bawaan Unity ("Sprites/Default" atau "Particles/Standard Unlit").
        [SerializeField] private Material sparkMaterial;

        // ------------------------------------------------------------------
        // Singleton
        // ------------------------------------------------------------------
        private static CombatFeedback instance;

        /// <summary>
        /// Instance aktif dari CombatFeedback.
        ///
        /// Kalau scene sudah punya CombatFeedback, instance itu dipakai.
        /// Kalau tidak, GameObject baru dibuat otomatis (CombatFeedback + AudioSource)
        /// sehingga tidak ada setup manual di file .unity.
        /// </summary>
        public static CombatFeedback Instance
        {
            get
            {
                if (instance == null)
                {
                    // Cari instance yang sudah ada di scene (aktif saja).
                    // CATATAN: FindFirstObjectByType deprecated di Unity 6, tapi
                    // dipakai di sini sesuai spesifikasi (pencarian instance
                    // PERTAMA). Warning-nya sengaja di-suppress agar build tetap bersih.
#pragma warning disable CS0618
                    instance = FindFirstObjectByType<CombatFeedback>();
#pragma warning restore CS0618

                    // Kalau belum ada -> buat sendiri (auto-spawn).
                    if (instance == null)
                    {
                        instance = CreateAutoInstance();
                    }
                }

                return instance;
            }
        }

        /// <summary>
        /// Membuat GameObject + CombatFeedback + AudioSource secara otomatis.
        /// Dipakai hanya ketika scene belum memiliki instance CombatFeedback.
        /// </summary>
        private static CombatFeedback CreateAutoInstance()
        {
            GameObject go = new GameObject("[CombatFeedback]");
            CombatFeedback created = go.AddComponent<CombatFeedback>();
            // AudioSource ditambahkan otomatis oleh [RequireComponent], tapi kita
            // tetap ambil reference-nya secara eksplisit supaya tidak null.
            created.audioSource = go.GetComponent<AudioSource>();

            // Jangan ikut destroyed saat pindah scene.
            DontDestroyOnLoad(go);

            Debug.Log("[CombatFeedback] Auto-created CombatFeedback instance (no scene setup required).");
            return created;
        }

        private void Awake()
        {
            // Jaga-jaga: kalau object ini dibuat sebagai prefab runtime,
            // pastikan singleton tetap menunjuk ke object yang sama.
            if (instance == null) instance = this;
            else if (instance != this) return;

            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

            ConfigureAudioSource();

            // Bangkitkan SFX sintetis untuk slot yang masih kosong di Inspector.
            BuildProceduralSfxIfMissing();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void ConfigureAudioSource()
        {
            if (audioSource == null) return;

            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f; // 2D game -> suara tidak boleh ikut 3D-attenuate.
            audioSource.volume = sfxVolume;
        }


        // ==================================================================
        // API STATIS (dipakai PlayerCombat & EnemyKlewang)
        // ==================================================================

        /// <summary>Memutar SFX tebasan (siapa pun yang menyerang).</summary>
        public static void PlaySlashSfx()
        {
            CombatFeedback self = Instance;
            if (self == null) return;
            self.PlaySfx(self.slashSfx);
        }

        /// <summary>Memutar SFX Perfect Parry / Deflect (metallic ping nyaring).</summary>
        public static void PlayPerfectParrySfx()
        {
            CombatFeedback self = Instance;
            if (self == null) return;
            self.PlaySfx(self.perfectParrySfx);
        }

        /// <summary>Memutar SFX block biasa (thud tumpul).</summary>
        public static void PlayBlockSfx()
        {
            CombatFeedback self = Instance;
            if (self == null) return;
            self.PlaySfx(self.blockSfx);
        }

        /// <summary>Memutar SFX tebasan mengenai badan musuh (impact berat).</summary>
        public static void PlayHitFleshSfx()
        {
            CombatFeedback self = Instance;
            if (self == null) return;
            self.PlaySfx(self.hitFleshSfx);
        }

        /// <summary>Memutar SFX guard break (retak rendah).</summary>
        public static void PlayGuardBreakSfx()
        {
            CombatFeedback self = Instance;
            if (self == null) return;
            self.PlaySfx(self.guardBreakSfx);
        }

        /// <summary>
        /// Feedback lengkap untuk DEATHBLOW (pukulan penghabisan):
        ///   - Memutar SFX deathblow (dentuman berat + denting tajam).
        ///   - Memunculkan ledakan percikan merah menyala & emas yang besar.
        ///   - Menggetarkan kamera dengan amplitudo besar.
        ///
        /// Dipanggil sekali ketika deathblow terjadi.
        /// </summary>
        /// <param name="position">Posisi dunia saat deathblow terjadi.</param>
        public static void PlayDeathblowFeedback(Vector3 position)
        {
            CombatFeedback self = Instance;
            if (self == null) return;

            // --- Audio ---
            self.PlaySfx(self.deathblowSfx);

            // --- VFX: ledakan merah menyala ---
            self.SpawnSparksInternal(
                position,
                new Color(1f, 0.25f, 0.05f),
                46,
                0.28f);

            // --- VFX: pancaran emas besar (layer kedua, sedikit lebih tinggi) ---
            self.SpawnSparksInternal(
                position + new Vector3(0f, 0.12f, 0f),
                new Color(1f, 0.85f, 0.2f),
                38,
                0.24f);

            // --- Camera shake: momen paling berat ---
            self.StartShake(0.25f, 0.28f);
        }

        /// <summary>
        /// Memutar satu SFX pada slot tertentu. Slot yang null berarti
        /// "tidak ada suara" (mis. AudioClip sintetis gagal dibuat) -> diabaikan.
        /// </summary>
        private void PlaySfx(AudioClip clip)
        {
            if (clip == null || audioSource == null) return;

            // PlayOneShot supaya beberapa SFX bisa bertumpuk tanpa saling memotong.
            audioSource.PlayOneShot(clip, sfxVolume);
        }

        /// <summary>
        /// Membangkitkan efek percikan api / kilatan secara prosedural.
        /// Dipakai sebagai Alias statis agar call site tetap ringkas.
        /// </summary>
        public static void SpawnSparks(Vector3 position, Color color, int count, float size)
        {
            CombatFeedback self = Instance;
            if (self == null) return;
            self.SpawnSparksInternal(position, color, count, size);
        }

        /// <summary>
        /// Menggetarkan kamera selama duration detik dengan amplitudo magnitude
        /// (dalam unit world).
        ///
        /// PENTING: memakai Time.unscaledDeltaTime. Saat hit-stop
        /// (Time.timeScale = 0f) dunia membeku, tapi getaran kamera HARUS tetap
        /// berjalan supaya benturan terasa berat.
        /// </summary>
        public static void ShakeCamera(float duration, float magnitude)
        {
            CombatFeedback self = Instance;
            if (self == null) return;
            self.StartShake(duration, magnitude);
        }


        // ==================================================================
        // Camera Shake
        // ==================================================================

        private Coroutine shakeRoutine;

        /// <summary>
        /// Memulai / menimpa getaran kamera.
        ///
        /// Getaran diterapkan pada POSISI LOKAL Camera.main supaya kamera tetap
        /// mengikuti movement pemain (script kamera lain) tanpa saling menimpa.
        /// </summary>
        private void StartShake(float duration, float magnitude)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            if (duration <= 0f || magnitude <= 0f) return;
            duration = Mathf.Min(duration, maxShakeDuration);

            // Getaran yang lebih besar / lebih lama menggantikan getaran sebelumnya.
            if (shakeRoutine != null)
            {
                StopCoroutine(shakeRoutine);
                shakeRoutine = null;
            }

            shakeRoutine = StartCoroutine(ShakeRoutineCo(cam, duration, magnitude));
        }

        /// <summary>
        /// Routine getaran kamera. Memakai Time.unscaledDeltaTime supaya tetap
        /// berjalan pada saat hit-stop (Time.timeScale = 0f).
        /// </summary>
        private IEnumerator ShakeRoutineCo(Camera cam, float duration, float magnitude)
        {
            // Simpan posisi lokal awal supaya bisa dikembalikan persis seperti semula.
            Vector3 originalLocalPosition = cam.transform.localPosition;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                // UNSCALED: wajib, agar getaran tetap terjadi saat hit-stop.
                elapsed += Time.unscaledDeltaTime;

                // Offset mengecil (decay) mendekati akhir durasi.
                float progress = Mathf.Clamp01(elapsed / duration);
                float decay = 1f - progress;
                float currentMagnitude = magnitude * decay;

                // Getaran memakai noise agar tidak terlihat terlalu mekanis.
                float time = elapsed * shakeFrequency;
                float offsetX = (Mathf.PerlinNoise(time, 0f) * 2f - 1f) * currentMagnitude;
                float offsetY = (Mathf.PerlinNoise(0f, time) * 2f - 1f) * currentMagnitude;

                cam.transform.localPosition = originalLocalPosition + new Vector3(offsetX, offsetY, 0f);

                yield return null;
            }

            // Kembalikan posisi kamera ke semula (persis, bukan mendekati).
            cam.transform.localPosition = originalLocalPosition;

            shakeRoutine = null;
        }


        // ==================================================================
        // Procedural Spark VFX
        // ==================================================================

        /// <summary>
        /// Membangkitkan efek percikan api / kilatan secara prosedural memakai
        /// ParticleSystem. Tidak butuh prefab eksternal, langsung muncul di layar.
        ///
        /// Parameter:
        ///   position : posisi dunia effect
        ///   color    : warna dasar percikan
        ///   count    : jumlah partikel
        ///   size     : ukuran dasar partikel (meter)
        /// </summary>
        private void SpawnSparksInternal(Vector3 position, Color color, int count, float size)
        {
            if (count <= 0) return;

            GameObject sparkObject = new GameObject("CombatSparks");
            sparkObject.transform.position = position;

            ParticleSystem ps = sparkObject.AddComponent<ParticleSystem>();

            // PENTING: hentikan sistem SEBELUM mengatur modul apa pun.
            // ParticleSystem yang masih "playing" akan memunculkan warning
            // "Setting the duration while system is still playing" saat kita
            // mengubah main.duration / modul lainnya. Karena itu kita stop +
            // clear dulu, dan playOnAwake dimatikan supaya tidak auto-start.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            ParticleSystem.EmissionModule emission = ps.emission;
            ParticleSystem.ShapeModule shape = ps.shape;
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = ps.sizeOverLifetime;
            ParticleSystem.NoiseModule noise = ps.noise;

            // ---- MAIN ----
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.5f;
            // Umur pendek & tajam: percikan cepat lenyap (efek kilat), bukan linger.
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            // Kecepatan lebih cepat supaya percikan meledak ke segala arah.
            main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 16f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startColor = color;
            // UNSCALED: partikel tetap bergerak pada saat hit-stop (timeScale = 0).
            main.useUnscaledTime = true;
            // TANPA gravitasi: percikan meledak radial (360 derajat / menyamping)
            // dan tidak merosot ke bawah seperti air.
            main.gravityModifier = 0f;
            main.stopAction = ParticleSystemStopAction.Destroy; // auto-destroy setelah selesai
            main.maxParticles = Mathf.Max(count, 64);

            // ---- EMISSION ----
            // Burst: semua partikel muncul sekaligus di frame pertama.
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)count) });

            // ---- SHAPE ----
            // Bentuk radial (default) dengan radius kecil: percikan menyebar dari titik hit.
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = size * 0.5f;

            // ---- COLOR OVER LIFETIME ----
            // Warna memudar ke transparan di akhir umur partikel.
            // Catatan: tipe kuncinya adalah GradientColorKey / GradientAlphaKey
            // (top-level struct di UnityEngine), bukan Gradient.ColorKey.
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(Color.Lerp(color, Color.white, 0.6f), 0.25f), new GradientColorKey(new Color(color.r, color.g, color.b, 0f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            // ---- SIZE OVER LIFETIME ----
            // Ukuran mengecil seiring waktu (percikan memendek & memudar).
            sizeOverLifetime.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 1f);
            sizeCurve.AddKey(0.35f, 0.85f);
            sizeCurve.AddKey(1f, 0f);
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // ---- NOISE (sedikit) ----
            // Sedikit gangguan supaya sebaran tidak terlalu sempurna simetris.
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            noise.frequency = 0.85f;

            // ---- RENDERER ----
            ParticleSystemRenderer renderer = sparkObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = ResolveSparkMaterial();
            renderer.alignment = ParticleSystemRenderSpace.View;
            // Sorting order tinggi -> percikan selalu tampil di depan sprite
            // karakter, supaya efeknya tidak tertutup sprite.
            renderer.sortingOrder = 20;

            // Config selesai -> baru mulai (supaya tidak ada warning
            // "Setting the duration while system is still playing").
            ps.Play();
        }

        /// <summary>
        /// Mengembalikan material untuk particle. Memakai material yang di-assign
        /// di Inspector kalau ada; kalau tidak, cari shader bawaan Unity
        /// ("Sprites/Default" atau "Particles/Standard Unlit").
        /// </summary>
        private Material ResolveSparkMaterial()
        {
            if (sparkMaterial != null) return sparkMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            if (shader == null)
            {
                Debug.LogWarning("[CombatFeedback] No shader found for spark particles, sparks will not be visible.");
                return null;
            }

            // Material runtime, tidak disimpan ke disk (tidak menyentuh asset .mat).
            Material runtimeMaterial = new Material(shader);
            runtimeMaterial.name = "CombatFeedback_SparkRuntime";

            // PBR particle butuh warna dasar; isi dengan putih supaya
            // colorOverLifetime (gradient) yang menentukan warna akhir.
            if (runtimeMaterial.HasProperty("_BaseColor")) runtimeMaterial.SetColor("_BaseColor", Color.white);
            if (runtimeMaterial.HasProperty("_Color")) runtimeMaterial.SetColor("_Color", Color.white);

            return runtimeMaterial;
        }


        // ==================================================================
        // Procedural SFX (AudioClip.Create)
        // ==================================================================

        /// <summary>
        /// Membangkitkan AudioClip sintetis untuk slot yang masih kosong.
        ///
        /// Semua suara dibuat dari sinus / noise yang digabung, TANPA file audio
        /// eksternal, sehingga langsung berbunyi saat diuji.
        /// </summary>
        private void BuildProceduralSfxIfMissing()
        {
            if (slashSfx == null) slashSfx = CreateWhooshSfx();
            if (perfectParrySfx == null) perfectParrySfx = CreateMetallicPingSfx();
            if (blockSfx == null) blockSfx = CreateThudSfx();
            if (hitFleshSfx == null) hitFleshSfx = CreateImpactSfx();
            if (guardBreakSfx == null) guardBreakSfx = CreateCrackSfx();
            if (deathblowSfx == null) deathblowSfx = CreateDeathblowSfx();
        }

        // Konstanta helper untuk sintesis audio.
        private const int SfxSampleRate = 44100;

        // Filter state untuk noise (dibuat static agar tiap clip mulai dari kondisi bersih).
        private static float prevNoiseSample = 0f;

        /// <summary>
        /// Sapuan angin halus singkat untuk tebasan (whoosh).
        ///
        /// Sengaja TANPA dentuman bass di awal supaya tidak terdengar seperti
        /// tembakan: hanya noise berfilter yang menyapu dengan gain rendah.
        /// </summary>
        private static AudioClip CreateWhooshSfx()
        {
            const float duration = 0.11f;
            const float gain = 0.25f;

            int samples = Mathf.RoundToInt(SfxSampleRate * duration);
            float[] data = new float[samples];
            System.Random rand = new System.Random(1234);
            prevNoiseSample = 0f;

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SfxSampleRate;
                float progress = (float)i / samples;

                // Amplitude: attack cepat, decay halus.
                float env = Mathf.Exp(-6f * progress) * Mathf.Min(1f, progress * 18f);

                // Noise heavily filtered (moving average) -> suara angin,
                // bukan white noise kasar.
                float noise = (float)(rand.NextDouble() * 2.0 - 1.0);
                float filtered = Mathf.Lerp(noise, prevNoiseSample, 0.94f);
                prevNoiseSample = filtered;

                // Sapuan nada tipis (tanpa bass) yang turun perlahan.
                float freq = Mathf.Lerp(1800f, 700f, progress);
                float tone = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.2f;

                data[i] = Mathf.Clamp((filtered * 0.55f + tone) * env * gain, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("SFX_Slash_Whoosh", samples, 1, SfxSampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// Denting baja nyaring (steel ring) untuk Perfect Parry.
        ///
        /// Menggunakan HANYA partial metal frekuensi tinggi yang INHARMONIK
        /// (1560 / 2480 / 3720 / 4950 Hz) dengan peluruhan eksponensial tajam.
        /// Tidak ada komponen noise maupun bass, sehingga terdengar seperti
        /// denting baja - BUKAN tembakan.
        /// </summary>
        private static AudioClip CreateMetallicPingSfx()
        {
            const float duration = 0.35f;

            int samples = Mathf.RoundToInt(SfxSampleRate * duration);
            float[] data = new float[samples];

            // Partial metal inharmonik (tinggi, bukan harmonik bass).
            float[] partials = { 1560f, 2480f, 3720f, 4950f };
            float[] partialGain = { 1f, 0.72f, 0.48f, 0.3f };

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SfxSampleRate;

                // Peluruhan eksponensial tajam (Exp(-t * 18)) -> denting singkat.
                float env = Mathf.Exp(-t * 18f);

                float sample = 0f;
                for (int p = 0; p < partials.Length; p++)
                {
                    sample += Mathf.Sin(2f * Mathf.PI * partials[p] * t) * partialGain[p];
                }

                data[i] = Mathf.Clamp(sample * env * 0.26f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("SFX_PerfectParry_SteelRing", samples, 1, SfxSampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }


        /// <summary>
        /// Benturan tumpul singkat untuk block biasa.
        ///
        /// Durasi pendek (0.08s) dan gain rendah (0.3) supaya terdengar seperti
        /// benturan tumpul, bukan tembakan. Rasio white noise dikecilkan supaya
        /// tidak terdengar kasar.
        /// </summary>
        private static AudioClip CreateThudSfx()
        {
            const float duration = 0.08f;
            const float gain = 0.3f;

            int samples = Mathf.RoundToInt(SfxSampleRate * duration);
            float[] data = new float[samples];
            System.Random rand = new System.Random(4321);
            prevNoiseSample = 0f;

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SfxSampleRate;
                float progress = (float)i / samples;

                // Decay cepat -> bunyi tumpul yang langsung habis.
                float env = Mathf.Exp(-16f * progress);

                // Noise di-low-pass dengan sangat berat -> hanya gumpalan
                // rendah yang tipis (bukan ledakan noise).
                float noise = (float)(rand.NextDouble() * 2.0 - 1.0);
                prevNoiseSample = Mathf.Lerp(prevNoiseSample, noise, 0.12f);

                // Nada rendah pendek sebagai "badan" benturan.
                float tone = Mathf.Sin(2f * Mathf.PI * 150f * t) * 0.6f
                          + Mathf.Sin(2f * Mathf.PI * 230f * t) * 0.25f;

                data[i] = Mathf.Clamp((prevNoiseSample * 0.9f + tone) * env * gain, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("SFX_Block_Thud", samples, 1, SfxSampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// Benturan tumpul singkat untuk tebasan mengenai badan.
        ///
        /// Durasi pendek (0.10s) dan gain rendah (0.3) supaya terasa sebagai
        /// "hantaman dull" yang memekakkan, bukan tembakan.
        /// </summary>
        private static AudioClip CreateImpactSfx()
        {
            const float duration = 0.1f;
            const float gain = 0.3f;

            int samples = Mathf.RoundToInt(SfxSampleRate * duration);
            float[] data = new float[samples];
            System.Random rand = new System.Random(5678);
            prevNoiseSample = 0f;

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SfxSampleRate;
                float progress = (float)i / samples;

                // Attack sangat cepat, decay cepat.
                float env = Mathf.Exp(-14f * progress) * Mathf.Min(1f, progress * 26f);

                // Noise cukup ter-filter (tidak mentah) supaya tidak "menembak".
                float noise = (float)(rand.NextDouble() * 2.0 - 1.0);
                prevNoiseSample = Mathf.Lerp(prevNoiseSample, noise, 0.28f);

                // "Badan" nada rendah yang pendek.
                float body = Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.7f
                          + Mathf.Sin(2f * Mathf.PI * 185f * t) * 0.3f;

                data[i] = Mathf.Clamp((prevNoiseSample * 1.1f + body) * env * gain, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("SFX_Hit_HeavyImpact", samples, 1, SfxSampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// Retakan logam rendah singkat untuk guard break.
        ///
        /// Durasi pendek dan TIDAK ada ledakan noise panjang; bunyinya seperti
        /// logam retak LOW: transient pendek + resonansi metal rendah yang
        /// langsung pudar.
        /// </summary>
        private static AudioClip CreateCrackSfx()
        {
            const float duration = 0.16f;
            const float gain = 0.4f;

            int samples = Mathf.RoundToInt(SfxSampleRate * duration);
            float[] data = new float[samples];
            System.Random rand = new System.Random(9012);
            prevNoiseSample = 0f;

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SfxSampleRate;
                float progress = (float)i / samples;

                float env = Mathf.Exp(-15f * progress);

                // Noise ringan & ter-filter (hanya gesekan logam, bukan ledakan).
                float noise = (float)(rand.NextDouble() * 2.0 - 1.0);
                prevNoiseSample = Mathf.Lerp(prevNoiseSample, noise, 0.3f);

                // Resonansi logam rendah yang "patah" (pitch turun).
                float freq = Mathf.Lerp(320f, 140f, progress);
                float metal = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.7f
                            + Mathf.Sin(2f * Mathf.PI * freq * 1.47f * t) * 0.25f;

                // Transien retakan sangat pendek di awal.
                float crackMoment = Mathf.Exp(-90f * progress);
                float crack = noise * crackMoment * 0.5f;

                data[i] = Mathf.Clamp((prevNoiseSample * 0.6f + metal + crack) * env * gain, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("SFX_GuardBreak_Crack", samples, 1, SfxSampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// Deathblow: DENTUMAN BERAT + DENTING TAJAM.
        ///
        /// Dua lapis yang digabung:
        ///   1. "Thump" low-end yang berat (bukan tembakan - decay-nya terkontrol
        ///      dan noise-nya sudah ter-filter).
        ///   2. "Ring" metal tinggi yang tajam (partial inharmonik) sebagai
        ///      denting pedang yang menusuk.
        /// </summary>
        private static AudioClip CreateDeathblowSfx()
        {
            const float duration = 0.55f;

            int samples = Mathf.RoundToInt(SfxSampleRate * duration);
            float[] data = new float[samples];
            System.Random rand = new System.Random(2468);
            prevNoiseSample = 0f;

            // Partial metal untuk denting tajam.
            float[] partials = { 2100f, 3350f, 4600f };
            float[] partialGain = { 1f, 0.6f, 0.35f };

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SfxSampleRate;
                float progress = (float)i / samples;

                // ---- Lapis 1: dentuman berat (thump) ----
                float thumpEnv = Mathf.Exp(-9f * progress);
                float thump = Mathf.Sin(2f * Mathf.PI * 62f * t) * 0.85f
                           + Mathf.Sin(2f * Mathf.PI * 97f * t) * 0.35f;

                // Noise ter-filter (gesekan) supaya tidak menguar seperti tembakan.
                float noise = (float)(rand.NextDouble() * 2.0 - 1.0);
                prevNoiseSample = Mathf.Lerp(prevNoiseSample, noise, 0.18f);
                float grit = prevNoiseSample * 0.7f;

                // ---- Lapis 2: denting tajam (ring) ----
                // Peluruhan jauh lebih cepat daripada thump.
                float ringEnv = Mathf.Exp(-t * 16f);
                float ring = 0f;
                for (int p = 0; p < partials.Length; p++)
                {
                    ring += Mathf.Sin(2f * Mathf.PI * partials[p] * t) * partialGain[p];
                }

                // --- Gabung: thump + denting, lalu terapkan gain akhir ---
                float mixed = (thump + grit) * thumpEnv * 0.55f + ring * ringEnv * 0.24f;

                data[i] = Mathf.Clamp(mixed, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("SFX_Deathblow_HeavyThumpRing", samples, 1, SfxSampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

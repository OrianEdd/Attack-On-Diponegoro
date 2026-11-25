using UnityEngine;
using UnityEngine.UI;

namespace AttackOnDiponegoro
{
    public class PlayerHUD : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PGIM.Sekiro2D.PlayerCombat playerCombat;

        [Header("Health Bar (Top-Left)")]
        [SerializeField] private Slider healthSlider;

        [Header("Posture Bar (Bottom-Center)")]
        [SerializeField] private GameObject postureBarContainer;
        [SerializeField] private RectTransform postureBarFill; // RectTransform dari Image bar yang melebar simetris
        [SerializeField] private Image postureFillImage;       // Image untuk Color.Lerp
        [SerializeField] private float maxPostureWidth = 300f; // Lebar maksimum posture bar saat penuh

        [Header("Posture Colors (Sekiro Style)")]
        [SerializeField] private Color normalPostureColor = new Color(1f, 0.85f, 0.2f); // #FFD933 (Kuning Emas)
        [SerializeField] private Color dangerPostureColor = new Color(1f, 0.25f, 0.1f); // #FF401A (Oranye-Merah Bahaya)

        private void OnEnable()
        {
            if (playerCombat == null)
            {
                playerCombat = Object.FindAnyObjectByType<PGIM.Sekiro2D.PlayerCombat>();
            }

            if (playerCombat != null)
            {
                playerCombat.OnHealthChanged += UpdateHealthBar;
                playerCombat.OnPostureChanged += UpdatePostureBar;
            }
            else
            {
                Debug.LogWarning("[PlayerHUD] PlayerCombat reference not found in scene!");
            }
        }

        private void OnDisable()
        {
            if (playerCombat != null)
            {
                playerCombat.OnHealthChanged -= UpdateHealthBar;
                playerCombat.OnPostureChanged -= UpdatePostureBar;
            }
        }

       
        private void UpdateHealthBar(float currentHealth, float maxHealth)
        {
            if (healthSlider != null && maxHealth > 0f)
            {
                healthSlider.value = Mathf.Clamp01(currentHealth / maxHealth);
            }
        }

        private void UpdatePostureBar(float currentPosture, float maxPosture)
        {
            if (postureBarContainer == null || postureBarFill == null) return;

            // Jika posture 0 atau kurang, otomatis sembunyikan container posture bar
            if (currentPosture <= 0f)
            {
                if (postureBarContainer.activeSelf)
                {
                    postureBarContainer.SetActive(false);
                }
                return;
            }

            // Aktifkan container jika ada posture
            if (!postureBarContainer.activeSelf)
            {
                postureBarContainer.SetActive(true);
            }

            if (maxPosture <= 0f) return;

            float ratio = Mathf.Clamp01(currentPosture / maxPosture);

            // Melebar secara simetris dari tengah ke kiri dan kanan (sizeDelta.x = ratio * maxPostureWidth)
            Vector2 sizeDelta = postureBarFill.sizeDelta;
            sizeDelta.x = ratio * maxPostureWidth;
            postureBarFill.sizeDelta = sizeDelta;

            // Berubah warna secara bertahap (Color.Lerp) dari kuning emas ke oranye-merah bahaya
            if (postureFillImage != null)
            {
                postureFillImage.color = Color.Lerp(normalPostureColor, dangerPostureColor, ratio);
            }
        }
    }
}

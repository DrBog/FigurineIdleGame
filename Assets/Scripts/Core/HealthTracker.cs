using UnityEngine;
using UnityEngine.UI;

namespace FigurineIdleGame.Core
{
    /// <summary>
    /// Tracks player health, score, kills, and wave readouts. Procedurally builds
    /// the HUD (health bar slider, score text, wave text) anchored for responsive
    /// Landscape/Portrait layouts. Applies damage and handles player death.
    /// </summary>
    public class HealthTracker : MonoBehaviour
    {
        private GameCore _core;

        public float MaxHealth { get; private set; }
        public float CurrentHealth { get; private set; }
        public int Score { get; private set; }
        public int Kills { get; private set; }
        public int Wave { get; private set; }

        [Header("Scoring")]
        public int scorePerKill = 10;
        public int waveBonusMultiplier = 5;

        private Slider _healthSlider;
        private Image _healthFill;
        private Text _healthLabel;
        private Text _scoreText;
        private Text _waveText;
        private bool _isDead;
        private Font _font;

        public void Initialize(GameCore core)
        {
            _core = core;
            MaxHealth = core.playerMaxHealth;
            CurrentHealth = MaxHealth;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildHUD();
            RefreshHealthUI();
            RefreshScoreUI();
        }

        private void BuildHUD()
        {
            // Health bar anchored to the TOP-LEFT (stays put across orientations).
            var sliderObj = new GameObject("HealthBar", typeof(RectTransform), typeof(Slider));
            sliderObj.transform.SetParent(_core.MainCanvas.transform, false);
            var sliderRect = sliderObj.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 1f);
            sliderRect.anchorMax = new Vector2(0f, 1f);
            sliderRect.pivot = new Vector2(0f, 1f);
            sliderRect.anchoredPosition = new Vector2(40f, -40f);
            sliderRect.sizeDelta = new Vector2(440f, 50f);

            _healthSlider = sliderObj.GetComponent<Slider>();
            _healthSlider.transition = Selectable.Transition.None;
            _healthSlider.interactable = false;
            _healthSlider.minValue = 0f;
            _healthSlider.maxValue = 1f;
            _healthSlider.value = 1f;

            // Background.
            var bgObj = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgObj.transform.SetParent(sliderRect, false);
            var bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            bgObj.GetComponent<Image>().color = new Color(0.10f, 0.10f, 0.12f, 0.85f);

            // Fill area.
            var fillAreaObj = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaObj.transform.SetParent(sliderRect, false);
            var fillAreaRect = fillAreaObj.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0f);
            fillAreaRect.anchorMax = new Vector2(1f, 1f);
            fillAreaRect.offsetMin = new Vector2(4f, 4f);
            fillAreaRect.offsetMax = new Vector2(-4f, -4f);

            var fillObj = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObj.transform.SetParent(fillAreaRect, false);
            var fillRect = fillObj.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            _healthFill = fillObj.GetComponent<Image>();
            _healthFill.color = new Color(0.85f, 0.25f, 0.30f, 1f);

            _healthSlider.fillRect = fillRect;
            _healthSlider.targetGraphic = _healthFill;

            // Numerical health label.
            var labelObj = new GameObject("HealthLabel", typeof(RectTransform));
            labelObj.transform.SetParent(sliderRect, false);
            var labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            _healthLabel = labelObj.AddComponent<Text>();
            _healthLabel.font = _font;
            _healthLabel.alignment = TextAnchor.MiddleCenter;
            _healthLabel.fontSize = 26;
            _healthLabel.fontStyle = FontStyle.Bold;
            _healthLabel.color = Color.white;

            // Score readout anchored TOP-RIGHT.
            var scoreObj = new GameObject("ScoreText", typeof(RectTransform));
            scoreObj.transform.SetParent(_core.MainCanvas.transform, false);
            var scoreRect = scoreObj.GetComponent<RectTransform>();
            scoreRect.anchorMin = new Vector2(1f, 1f);
            scoreRect.anchorMax = new Vector2(1f, 1f);
            scoreRect.pivot = new Vector2(1f, 1f);
            scoreRect.anchoredPosition = new Vector2(-40f, -40f);
            scoreRect.sizeDelta = new Vector2(440f, 60f);
            _scoreText = scoreObj.AddComponent<Text>();
            _scoreText.font = _font;
            _scoreText.alignment = TextAnchor.UpperRight;
            _scoreText.fontSize = 38;
            _scoreText.fontStyle = FontStyle.Bold;
            _scoreText.color = new Color(0.95f, 0.95f, 0.98f, 1f);

            // Wave readout anchored TOP-CENTER.
            var waveObj = new GameObject("WaveText", typeof(RectTransform));
            waveObj.transform.SetParent(_core.MainCanvas.transform, false);
            var waveRect = waveObj.GetComponent<RectTransform>();
            waveRect.anchorMin = new Vector2(0.5f, 1f);
            waveRect.anchorMax = new Vector2(0.5f, 1f);
            waveRect.pivot = new Vector2(0.5f, 1f);
            waveRect.anchoredPosition = new Vector2(0f, -40f);
            waveRect.sizeDelta = new Vector2(400f, 60f);
            _waveText = waveObj.AddComponent<Text>();
            _waveText.font = _font;
            _waveText.alignment = TextAnchor.UpperCenter;
            _waveText.fontSize = 34;
            _waveText.fontStyle = FontStyle.Bold;
            _waveText.color = new Color(0.85f, 0.90f, 0.98f, 1f);
        }

        public void ResetForRun()
        {
            _isDead = false;
            CurrentHealth = MaxHealth;
            Score = 0;
            Kills = 0;
            Wave = 1;
            RefreshHealthUI();
            RefreshScoreUI();
        }

        public void ApplyDamageToPlayer(float amount)
        {
            if (_isDead)
            {
                return;
            }

            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            RefreshHealthUI();

            if (CurrentHealth <= 0f)
            {
                HandleDeath();
            }
        }

        public void Heal(float amount)
        {
            if (_isDead)
            {
                return;
            }
            CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
            RefreshHealthUI();
        }

        public void RegisterKill(int wave)
        {
            Kills++;
            Score += scorePerKill + wave * waveBonusMultiplier;
            RefreshScoreUI();
        }

        public void SetWave(int wave)
        {
            Wave = wave;
            RefreshScoreUI();
        }

        private void HandleDeath()
        {
            _isDead = true;
            if (_core != null)
            {
                _core.OnPlayerDeath();
            }
        }

        private void RefreshHealthUI()
        {
            float pct = MaxHealth > 0f ? CurrentHealth / MaxHealth : 0f;
            if (_healthSlider != null)
            {
                _healthSlider.value = pct;
            }
            if (_healthFill != null)
            {
                // Shift toward orange/green as health changes for quick readability.
                _healthFill.color = Color.Lerp(new Color(0.85f, 0.20f, 0.22f, 1f),
                                                new Color(0.30f, 0.80f, 0.45f, 1f), pct);
            }
            if (_healthLabel != null)
            {
                _healthLabel.text = Mathf.CeilToInt(CurrentHealth) + " / " + Mathf.CeilToInt(MaxHealth);
            }
        }

        private void RefreshScoreUI()
        {
            if (_scoreText != null)
            {
                _scoreText.text = "SCORE " + Score + "\nKILLS " + Kills;
            }
            if (_waveText != null)
            {
                _waveText.text = "WAVE " + Wave;
            }
        }
    }
}

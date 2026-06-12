using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace FigurineIdleGame.Core
{
    /// <summary>
    /// Procedurally builds the main menu using clean unlit primitive geometric
    /// UI elements. Provides the START RUN and UPGRADE LAB buttons and a smooth
    /// fade transition into the combat layer.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        private GameCore _core;

        private CanvasGroup _group;
        private RectTransform _root;
        private bool _transitioning;

        [Header("Transition")]
        public float fadeDuration = 0.45f;

        public void Initialize(GameCore core)
        {
            _core = core;
            BuildUI();
        }

        private void BuildUI()
        {
            // Root panel anchored to fill the entire screen (responsive for both
            // Landscape and Portrait orientations).
            var panelObj = new GameObject("MainMenuPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            panelObj.transform.SetParent(_core.MainCanvas.transform, false);

            _root = panelObj.GetComponent<RectTransform>();
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;

            var bg = panelObj.GetComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.09f, 0.96f);

            _group = panelObj.GetComponent<CanvasGroup>();
            _group.alpha = 1f;

            BuildTitle();
            BuildButton("START RUN", new Vector2(0f, -40f), new Color(0.20f, 0.70f, 0.55f, 1f), OnStartRun);
            BuildButton("UPGRADE LAB", new Vector2(0f, -260f), new Color(0.30f, 0.38f, 0.55f, 1f), OnUpgradeLab);
        }

        /// <summary>
        /// Builds a clean geometric title from primitive UI rectangles plus a label.
        /// </summary>
        private void BuildTitle()
        {
            // Title backing bar.
            var barObj = new GameObject("TitleBar", typeof(RectTransform), typeof(Image));
            barObj.transform.SetParent(_root, false);
            var barRect = barObj.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0.5f, 1f);
            barRect.anchorMax = new Vector2(0.5f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.anchoredPosition = new Vector2(0f, -180f);
            barRect.sizeDelta = new Vector2(720f, 200f);
            barObj.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.20f, 1f);

            // Decorative accent strip (unlit geometric block).
            var accentObj = new GameObject("TitleAccent", typeof(RectTransform), typeof(Image));
            accentObj.transform.SetParent(barRect, false);
            var accentRect = accentObj.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(1f, 0f);
            accentRect.pivot = new Vector2(0.5f, 0f);
            accentRect.anchoredPosition = new Vector2(0f, 0f);
            accentRect.sizeDelta = new Vector2(0f, 14f);
            accentObj.GetComponent<Image>().color = new Color(0.20f, 0.70f, 0.55f, 1f);

            // Title text.
            var textObj = new GameObject("TitleText", typeof(RectTransform));
            textObj.transform.SetParent(barRect, false);
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var label = textObj.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.text = "FIGURINE\nIDLE";
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = 64;
            label.fontStyle = FontStyle.Bold;
            label.color = new Color(0.92f, 0.95f, 0.98f, 1f);
        }

        private Button BuildButton(string caption, Vector2 anchoredPos, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var btnObj = new GameObject(caption + "_Button", typeof(RectTransform), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(_root, false);

            var rect = btnObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(620f, 150f);

            var img = btnObj.GetComponent<Image>();
            img.color = color;

            var button = btnObj.GetComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.highlightedColor = new Color(color.r + 0.08f, color.g + 0.08f, color.b + 0.08f, 1f);
            colors.pressedColor = new Color(color.r * 0.8f, color.g * 0.8f, color.b * 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(onClick);

            // Button label.
            var textObj = new GameObject("Label", typeof(RectTransform));
            textObj.transform.SetParent(rect, false);
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var label = textObj.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.text = caption;
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = 48;
            label.fontStyle = FontStyle.Bold;
            label.color = Color.white;

            return button;
        }

        private void OnStartRun()
        {
            if (_transitioning)
            {
                return;
            }

            _transitioning = true;
            StartCoroutine(FadeOutAndStart());
        }

        /// <summary>
        /// UPGRADE LAB placeholder. Full functionality lands in Phase 4.
        /// </summary>
        private void OnUpgradeLab()
        {
            Debug.Log("[FigurineIdleGame] Upgrade Lab is reserved for Phase 4.");
        }

        private IEnumerator FadeOutAndStart()
        {
            float elapsed = 0f;
            float startAlpha = _group.alpha;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / fadeDuration);
                _group.alpha = Mathf.Lerp(startAlpha, 0f, t);
                yield return null;
            }

            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _root.gameObject.SetActive(false);

            // Hide menu and unfreeze the combat layer.
            _core.StartRun();
            _transitioning = false;
        }

        public void Show()
        {
            _transitioning = false;
            _root.gameObject.SetActive(true);
            _group.alpha = 1f;
            _group.interactable = true;
            _group.blocksRaycasts = true;
        }

        public void Hide()
        {
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _root.gameObject.SetActive(false);
        }
    }
}

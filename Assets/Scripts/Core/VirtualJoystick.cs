using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FigurineIdleGame.Core
{
    /// <summary>
    /// Fading touch/pointer joystick. Provides a normalized direction vector for
    /// the PlayerController. The handle follows the pointer within a fixed radius.
    /// After 2 seconds of no input the CanvasGroup fades to alpha 0, and it snaps
    /// back to full opacity instantly on a new touch. Works with touch and mouse.
    /// </summary>
    public class VirtualJoystick : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        private GameCore _core;

        [Header("Layout")]
        public float baseRadius = 160f;
        public float handleRadius = 70f;

        [Header("Fade")]
        public float idleFadeDelay = 2.0f;
        public float fadeSpeed = 4.0f;

        public Vector2 Direction { get; private set; }
        public bool IsPressed { get; private set; }

        private CanvasGroup _group;
        private RectTransform _baseRect;
        private RectTransform _handleRect;
        private Canvas _canvas;
        private float _idleTimer;
        private bool _interactable = true;

        public void Initialize(GameCore core)
        {
            _core = core;
            _canvas = core.MainCanvas;
            BuildUI();
        }

        private void BuildUI()
        {
            // Container anchored to the bottom-left, which is the natural thumb
            // zone for both Landscape and Portrait. Uses anchored layout so it
            // stays responsive across resolutions and orientations.
            var rootObj = new GameObject("JoystickRoot", typeof(RectTransform), typeof(CanvasGroup));
            rootObj.transform.SetParent(_canvas.transform, false);

            _group = rootObj.GetComponent<CanvasGroup>();
            _group.alpha = 1f;
            _group.interactable = true;
            _group.blocksRaycasts = true;

            var rootRect = rootObj.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            // Full-area input catcher so a touch anywhere positions the joystick.
            var catcherObj = new GameObject("InputCatcher", typeof(RectTransform), typeof(Image));
            catcherObj.transform.SetParent(rootRect, false);
            var catcherRect = catcherObj.GetComponent<RectTransform>();
            catcherRect.anchorMin = Vector2.zero;
            catcherRect.anchorMax = Vector2.one;
            catcherRect.offsetMin = Vector2.zero;
            catcherRect.offsetMax = Vector2.zero;
            var catcherImg = catcherObj.GetComponent<Image>();
            catcherImg.color = new Color(0f, 0f, 0f, 0f); // invisible but raycastable
            // Forward pointer events from the catcher to this component.
            var relay = catcherObj.AddComponent<JoystickEventRelay>();
            relay.Target = this;

            // Joystick base ring.
            var baseObj = new GameObject("JoystickBase", typeof(RectTransform), typeof(Image));
            baseObj.transform.SetParent(rootRect, false);
            _baseRect = baseObj.GetComponent<RectTransform>();
            _baseRect.anchorMin = new Vector2(0f, 0f);
            _baseRect.anchorMax = new Vector2(0f, 0f);
            _baseRect.pivot = new Vector2(0.5f, 0.5f);
            _baseRect.sizeDelta = new Vector2(baseRadius * 2f, baseRadius * 2f);
            _baseRect.anchoredPosition = new Vector2(240f, 240f);
            var baseImg = baseObj.GetComponent<Image>();
            baseImg.color = new Color(1f, 1f, 1f, 0.18f);
            baseImg.raycastTarget = false;

            // Joystick handle.
            var handleObj = new GameObject("JoystickHandle", typeof(RectTransform), typeof(Image));
            handleObj.transform.SetParent(_baseRect, false);
            _handleRect = handleObj.GetComponent<RectTransform>();
            _handleRect.anchorMin = new Vector2(0.5f, 0.5f);
            _handleRect.anchorMax = new Vector2(0.5f, 0.5f);
            _handleRect.pivot = new Vector2(0.5f, 0.5f);
            _handleRect.sizeDelta = new Vector2(handleRadius * 2f, handleRadius * 2f);
            _handleRect.anchoredPosition = Vector2.zero;
            var handleImg = handleObj.GetComponent<Image>();
            handleImg.color = new Color(0.20f, 0.70f, 0.55f, 0.85f);
            handleImg.raycastTarget = false;

            _idleTimer = idleFadeDelay;
        }

        public void SetInteractable(bool value)
        {
            _interactable = value;
            if (_group != null)
            {
                _group.blocksRaycasts = value;
            }

            if (!value)
            {
                ResetInput();
            }
        }

        private void Update()
        {
            // Fade out after idle delay; snap to full alpha is handled on press.
            if (!IsPressed)
            {
                _idleTimer -= Time.unscaledDeltaTime;
                if (_idleTimer <= 0f && _group != null && _group.alpha > 0f)
                {
                    _group.alpha = Mathf.MoveTowards(_group.alpha, 0f, fadeSpeed * Time.unscaledDeltaTime);
                }
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_interactable)
            {
                return;
            }

            IsPressed = true;
            // Snap fully visible instantly upon new touch interaction.
            if (_group != null)
            {
                _group.alpha = 1f;
            }
            _idleTimer = idleFadeDelay;

            // Reposition the base to where the pointer touched.
            Vector2 localPoint;
            RectTransform parentRect = _baseRect.parent as RectTransform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, eventData.position, GetEventCamera(eventData), out localPoint))
            {
                _baseRect.anchoredPosition = localPoint;
            }

            UpdateHandle(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_interactable || !IsPressed)
            {
                return;
            }

            _idleTimer = idleFadeDelay;
            UpdateHandle(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            ResetInput();
            _idleTimer = idleFadeDelay;
        }

        private void UpdateHandle(PointerEventData eventData)
        {
            Vector2 localPoint;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _baseRect, eventData.position, GetEventCamera(eventData), out localPoint))
            {
                return;
            }

            // Clamp the handle inside the base radius and derive normalized direction.
            Vector2 clamped = Vector2.ClampMagnitude(localPoint, baseRadius);
            _handleRect.anchoredPosition = clamped;
            Direction = clamped / baseRadius;
        }

        private void ResetInput()
        {
            IsPressed = false;
            Direction = Vector2.zero;
            if (_handleRect != null)
            {
                _handleRect.anchoredPosition = Vector2.zero;
            }
        }

        private Camera GetEventCamera(PointerEventData eventData)
        {
            // ScreenSpaceOverlay canvases use a null event camera.
            if (_canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }
            return eventData.enterEventCamera != null ? eventData.enterEventCamera : Camera.main;
        }
    }

    /// <summary>
    /// Relays pointer events from the full-screen input catcher to the joystick.
    /// </summary>
    public class JoystickEventRelay : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public VirtualJoystick Target;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Target != null) Target.OnPointerDown(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Target != null) Target.OnDrag(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (Target != null) Target.OnPointerUp(eventData);
        }
    }
}

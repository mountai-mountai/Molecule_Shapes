// Makes a world-space UI element visibly react to being pointed at: it grows slightly and (optionally)
// brightens. At VR distances the default uGUI hover tint alone is hard to read, so the size change is
// what actually tells the user "this is the thing you're about to click".
//
// Added automatically by PanelUi.MakeButton when the theme enables hover feedback.

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Molecule_Shapes.View
{
    public class HoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("Scale multiplier while pointed at (1 = no growth).")]
        public float HoverScaleFactor = 1.08f;
        [Tooltip("Seconds to ease between resting and hovered size.")]
        public float Duration = 0.08f;
        [Tooltip("Optional outline drawn while hovered. Alpha 0 = off.")]
        public Color OutlineColor = new Color(1f, 1f, 1f, 0f);
        [Tooltip("Outline thickness in UI units.")]
        public float OutlineWidth = 3f;

        private Vector3 _restScale;
        private Vector3 _targetScale;
        private Outline _outline;
        private Selectable _selectable;
        private bool _hovered;

        // A disabled control (e.g. Next/Hint outside a game) must not grow or outline - it should read
        // as unavailable, not as something you're about to press.
        private bool Interactable => _selectable == null || _selectable.IsInteractable();

        private void Awake()
        {
            _restScale = transform.localScale;
            _targetScale = _restScale;
            _selectable = GetComponent<Selectable>();

            if (OutlineColor.a > 0f)
            {
                Graphic g = GetComponent<Graphic>();
                if (g != null)
                {
                    _outline = gameObject.AddComponent<Outline>();
                    _outline.effectColor = OutlineColor;
                    _outline.effectDistance = new Vector2(OutlineWidth, OutlineWidth);
                    _outline.enabled = false;
                }
            }
        }

        // Re-read the resting scale if a layout pass changed it while we weren't hovered.
        private void OnEnable()
        {
            if (!_hovered) { _restScale = transform.localScale; _targetScale = _restScale; }
        }

        private void Update()
        {
            // If it became disabled while hovered, drop the highlight immediately.
            if (_hovered && !Interactable) ClearHover();

            if (transform.localScale == _targetScale) return;
            float step = Duration > 0f ? Time.unscaledDeltaTime / Duration : 1f;
            transform.localScale = Vector3.Lerp(transform.localScale, _targetScale, Mathf.Clamp01(step * 2f));
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!Interactable) return;
            _hovered = true;
            _targetScale = _restScale * HoverScaleFactor;
            if (_outline != null) _outline.enabled = true;
        }

        private void ClearHover()
        {
            _hovered = false;
            _targetScale = _restScale;
            if (_outline != null) _outline.enabled = false;
        }

        public void OnPointerExit(PointerEventData eventData) => ClearHover();
    }
}

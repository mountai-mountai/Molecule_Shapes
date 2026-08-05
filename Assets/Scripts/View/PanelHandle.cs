// Marker placed on a panel's grab bar. PanelGrabController raycasts for this and repositions PanelRoot
// (the whole panel, including its canvas) when the user grabs the handle.
//
// It also renders its own hover state: when a controller ray is pointed at it the bar brightens and
// thickens, so it's obvious the handle is what you're about to grab (hard to judge at a distance).

using UnityEngine;

namespace Molecule_Shapes.View
{
    public class PanelHandle : MonoBehaviour
    {
        [Tooltip("The transform that gets moved when this handle is grabbed (the panel's root object).")]
        public Transform PanelRoot;

        [Tooltip("Scale multiplier applied while a controller is pointing at this handle.")]
        public float HoverScale = 1.12f;
        [Tooltip("How much brighter the bar gets while hovered (0 = no change).")]
        [Range(0f, 1f)] public float HoverBrighten = 0.35f;

        private Renderer _renderer;
        private Color _baseColor;
        private Vector3 _baseScale;
        private bool _hovered;
        private bool _initialised;

        private void Awake() => Initialise();

        private void Initialise()
        {
            if (_initialised) return;
            _baseScale = transform.localScale;
            _renderer = GetComponent<Renderer>();
            if (_renderer != null)
            {
                Material m = _renderer.material;   // instance, safe to tint
                if (m.HasProperty("_BaseColor")) _baseColor = m.GetColor("_BaseColor");
                else if (m.HasProperty("_Color")) _baseColor = m.GetColor("_Color");
                else _baseColor = Color.white;
            }
            _initialised = true;
        }

        /// <summary>Called every frame by PanelGrabController for the handle under a controller ray.</summary>
        public void SetHovered(bool hovered)
        {
            Initialise();
            if (hovered == _hovered) return;
            _hovered = hovered;

            transform.localScale = hovered ? _baseScale * HoverScale : _baseScale;

            if (_renderer == null) return;
            Color c = hovered
                ? Color.Lerp(_baseColor, Color.white, HoverBrighten)
                : _baseColor;
            Material m = _renderer.material;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }
    }
}

// Toggles / crossfades between full VR and passthrough (MR), driven by the camera's background alpha.
//
//   blend 0 = full VR   -> background is opaque (vrBackgroundColor), passthrough hidden
//   blend 1 = full MR   -> background is transparent, the passthrough underlay shows your room
//   in between          -> the room is "ghosted" behind the VR background colour
//
// The camera-alpha approach means this needs NO Meta/OVR assembly reference: the passthrough layer just
// has to exist and be active, so it's referenced as a plain GameObject (SetActive) rather than the typed
// OVRPassthroughLayer. Opaque geometry (molecule, panels) always shows regardless of blend - only the
// background crossfades.
//
// Left-hand Y (secondaryButton): TAP = toggle MR<->VR; HOLD = scrub the blend continuously. A hold pushes
// away from whichever end you're at, and continues its prior direction from the middle.

using UnityEngine;
using UnityEngine.InputSystem;

namespace Molecule_Shapes.View
{
    public class PassthroughController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The GameObject holding OVRPassthroughLayer. Toggled active when blend > 0 (saves GPU at " +
                 "full VR). Referenced as a GameObject so no Meta assembly dependency is needed.")]
        [SerializeField] private GameObject passthroughLayerObject;
        [Tooltip("Camera whose background alpha is driven. Empty = Camera.main (the headset).")]
        [SerializeField] private Camera targetCamera;
        [Tooltip("Optional: read the VR background colour from this SceneBackgroundController. Keep its " +
                 "'Solid Color Background' OFF - this controller owns the camera background.")]
        [SerializeField] private SceneBackgroundController background;

        [Header("VR background")]
        [Tooltip("Background colour shown at full VR (blend 0). Ignored if a SceneBackgroundController is assigned.")]
        [SerializeField] private Color vrBackgroundColor = new Color(0.03f, 0.04f, 0.07f, 1f);

        [Header("State")]
        [Tooltip("0 = full VR (opaque), 1 = full passthrough. Starting value (1 = boot into MR).")]
        [Range(0f, 1f)] [SerializeField] private float blend = 1f;
        [Tooltip("Which look the scene boots into. Sets the starting blend.")]
        [SerializeField] private PassthroughPreset startPreset = PassthroughPreset.FullMr;

        [Header("Toggle / scrub")]
        [Tooltip("Seconds for a TAP to animate fully between VR and MR.")]
        [SerializeField] private float toggleDuration = 0.35f;
        [Tooltip("Blend units per second while HOLDING the button.")]
        [SerializeField] private float scrubSpeed = 1.2f;
        [Tooltip("A press shorter than this counts as a tap; longer becomes a scrub.")]
        [SerializeField] private float holdThreshold = 0.25f;

        [Header("Input")]
        [Tooltip("Button that toggles/scrubs. Default = left controller Y. Swap if it doesn't register on " +
                 "your controller profile (e.g. <XRController>{LeftHand}/primaryButton for X).")]
        [SerializeField] private string buttonBinding = "<XRController>{LeftHand}/secondaryButton";

        private InputAction _button;
        private float _pressTime;
        private bool _wasPressed;     // previous-frame button state, for edge detection
        private bool _scrubbing;
        private int _scrubDir = -1;   // direction the current scrub is moving
        private int _lastDir = -1;    // remembered so a mid-blend hold continues the same way
        private bool _animating;      // a tap toggle animation is running
        private float _target;

        private void Awake()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            blend = PassthroughPresets.BlendFor(startPreset);
            _button = new InputAction("PassthroughToggle", InputActionType.Button, buttonBinding);
        }

        private void OnEnable() => _button?.Enable();
        private void OnDisable() => _button?.Disable();

        // Poll the button each frame with our own edge detection - far more reliable than the Button
        // action's started/canceled callbacks, which don't consistently re-fire after the first press.
        private void Update()
        {
            bool pressed = _button != null && _button.IsPressed();
            bool justPressed = pressed && !_wasPressed;
            bool justReleased = !pressed && _wasPressed;

            if (justPressed)
            {
                _pressTime = Time.time;
                _scrubbing = false;
                _animating = false;      // a new press cancels any running tap animation
            }

            if (pressed && Time.time - _pressTime >= holdThreshold)
            {
                // Held long enough -> scrub the blend.
                if (!_scrubbing)
                {
                    _scrubbing = true;
                    if (blend >= 0.999f) _scrubDir = -1;         // pinned at MR -> head to VR
                    else if (blend <= 0.001f) _scrubDir = 1;     // pinned at VR -> head to MR
                    else _scrubDir = _lastDir != 0 ? _lastDir : -1;   // mid-blend -> continue prior direction
                }
                blend = Mathf.Clamp01(blend + _scrubDir * scrubSpeed * Time.deltaTime);
            }

            if (justReleased)
            {
                if (_scrubbing)
                {
                    _lastDir = _scrubDir;   // next hold from the middle continues this direction
                    _scrubbing = false;
                }
                else
                {
                    // Quick tap -> toggle to the opposite end.
                    _target = blend > 0.5f ? 0f : 1f;
                    _lastDir = _target > blend ? 1 : -1;
                    _animating = true;
                }
            }

            if (_animating && !pressed)
            {
                blend = Mathf.MoveTowards(blend, _target, Time.deltaTime / Mathf.Max(0.01f, toggleDuration));
                if (Mathf.Approximately(blend, _target)) _animating = false;
            }

            _wasPressed = pressed;
            Apply();
        }

        private void Apply()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera != null)
            {
                Color vr = background != null ? background.BackgroundColor : vrBackgroundColor;
                targetCamera.clearFlags = CameraClearFlags.SolidColor;
                targetCamera.backgroundColor = new Color(vr.r, vr.g, vr.b, 1f - blend);
            }

            // Disable the passthrough layer object at full VR to save GPU - but ONLY if this controller
            // doesn't live on or under it, otherwise we'd deactivate ourselves and freeze Update (one
            // toggle then dead). When they share an object we just leave the layer active; the opaque
            // camera background hides it anyway.
            if (passthroughLayerObject != null && !transform.IsChildOf(passthroughLayerObject.transform))
            {
                bool show = blend > 0.001f;
                if (passthroughLayerObject.activeSelf != show) passthroughLayerObject.SetActive(show);
            }
        }

        // --- Public API (so a UI button / haptic-on-solve etc. can drive it too) --------------------

        /// <summary>Animate a full toggle between VR and MR.</summary>
        public void Toggle()
        {
            _target = blend > 0.5f ? 0f : 1f;
            _lastDir = _target > blend ? 1 : -1;
            _animating = true;
            _scrubbing = false;
        }

        /// <summary>Animate to full MR (true) or full VR (false).</summary>
        public void SetPassthrough(bool on)
        {
            _target = on ? 1f : 0f;
            _lastDir = _target > blend ? 1 : -1;
            _animating = true;
            _scrubbing = false;
        }

        /// <summary>Step to the next preset (Full VR -> Ghost -> Full MR -> ...) and animate to it.</summary>
        public void CyclePreset()
        {
            PassthroughPreset next = PassthroughPresets.Next(PassthroughPresets.Nearest(blend));
            _target = PassthroughPresets.BlendFor(next);
            _lastDir = _target > blend ? 1 : -1;
            _animating = true;
            _scrubbing = false;
        }

        /// <summary>Set the blend directly (0 = VR, 1 = MR), no animation.</summary>
        public void SetBlend(float value)
        {
            blend = Mathf.Clamp01(value);
            _animating = false;
            _scrubbing = false;
        }

        public float Blend => blend;
    }
}

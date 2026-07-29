// Lets the user grab a panel by its handle bar and move it around in space. Uses the same lightweight
// pattern as MoleculeXRDragController (controller transform + an input action + a raycast) rather than
// XRGrabInteractable, so it needs no Rigidbody/collider setup beyond the handle's own collider and no
// XRI-specific types.
//
// Grip (Select) is the grab button here. It doesn't conflict with the molecule's XRGrabInteractable
// grab: pointing at a panel handle (a plain collider, not an XR interactable) means XRI's grip does
// nothing, so this controller handles it; pointing at the molecule means the raycast finds no handle,
// so this controller stays out of the way and XRI grabs the molecule.
//
// While held, the panel is simply parented to the controller so it follows the hand rigidly; on release
// it's unparented back to the scene and stays where you left it.
//
// Wire in the Inspector (same objects/actions the drag controller uses, but the GRIP action):
//   leftController / rightController  -> LeftHand / RightHand Controller transforms
//   leftGripAction / rightGripAction  -> XRI LeftHand Interaction/Select, XRI RightHand Interaction/Select

using UnityEngine;
using UnityEngine.InputSystem;
using Molecule_Shapes.Game;

namespace Molecule_Shapes.View
{
    [DefaultExecutionOrder(-9)]
    public class PanelGrabController : MonoBehaviour
    {
        [Header("Controller transforms (forward is the ray direction)")]
        [SerializeField] private Transform leftController;
        [SerializeField] private Transform rightController;

        [Header("Grip input actions (XRI <hand> Interaction / Select)")]
        [SerializeField] private InputActionReference leftGripAction;
        [SerializeField] private InputActionReference rightGripAction;

        [Header("Reach")]
        [Tooltip("Maximum distance the grab ray reaches for a panel handle (metres).")]
        [SerializeField] private float maxRayDistance = 5f;

        private readonly HandState _left = new();
        private readonly HandState _right = new();

        private class HandState
        {
            public Transform Grabbed;
            public bool WasPressed;
            public bool Upright;        // this grab keeps the panel vertical (not parented to the hand)
            public Vector3 LocalOffset; // panel offset expressed in the hand's yaw-only frame, at grab
            public float YawOffset;     // panel yaw relative to the hand's yaw, at grab
        }

        // Hand's heading about world-up only (ignores pitch/roll). Uses the flattened forward, falling
        // back to the flattened up vector when the controller points near-vertical.
        private static float YawDegrees(Transform t)
        {
            Vector3 f = t.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) { f = t.up; f.y = 0f; }
            if (f.sqrMagnitude < 1e-6f) return 0f;
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        private void OnEnable()
        {
            if (leftGripAction != null && leftGripAction.action != null) leftGripAction.action.Enable();
            if (rightGripAction != null && rightGripAction.action != null) rightGripAction.action.Enable();
        }

        private void Update()
        {
            UpdateHand(leftController, leftGripAction, _left);
            UpdateHand(rightController, rightGripAction, _right);
        }

        private void UpdateHand(Transform controller, InputActionReference gripRef, HandState state)
        {
            if (controller == null || gripRef == null || gripRef.action == null) return;

            bool pressed = gripRef.action.IsPressed();
            bool wasPressed = state.WasPressed;
            state.WasPressed = pressed;

            if (pressed && !wasPressed) TryGrab(controller, state);
            else if (state.Grabbed != null && !pressed) Release(state);

            // Upright grabs aren't parented to the hand (which would tilt the panel). Instead we follow a
            // yaw-only version of the hand: translate + rotate about world-up with it, but never pitch/roll.
            if (pressed && state.Grabbed != null && state.Upright)
            {
                float yaw = YawDegrees(controller);
                Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
                state.Grabbed.position = controller.position + yawRot * state.LocalOffset;
                state.Grabbed.rotation = Quaternion.Euler(0f, yaw + state.YawOffset, 0f);
            }
        }

        private void TryGrab(Transform controller, HandState state)
        {
            RaycastHit[] hits = Physics.RaycastAll(controller.position, controller.forward, maxRayDistance);
            PanelHandle nearest = null;
            float best = float.MaxValue;
            foreach (RaycastHit h in hits)
            {
                PanelHandle handle = h.collider.GetComponentInParent<PanelHandle>();
                if (handle != null && handle.PanelRoot != null && h.distance < best)
                {
                    best = h.distance;
                    nearest = handle;
                }
            }
            if (nearest == null) return;

            state.Grabbed = nearest.PanelRoot;
            state.Upright = AppSettings.Current.PanelsUpright;
            if (state.Upright)
            {
                // Record the panel's offset + facing in the hand's yaw-only frame so it tracks the hand's
                // heading (and translation) while staying vertical.
                float yaw = YawDegrees(controller);
                Quaternion invYaw = Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f));
                state.LocalOffset = invYaw * (state.Grabbed.position - controller.position);
                state.YawOffset = state.Grabbed.eulerAngles.y - yaw;
            }
            else
            {
                state.Grabbed.SetParent(controller, worldPositionStays: true);   // free 6-DOF follow
            }
        }

        private void Release(HandState state)
        {
            // Only the free (parented) grab needs unparenting; upright grabs were never parented.
            if (state.Grabbed != null && !state.Upright) state.Grabbed.SetParent(null, worldPositionStays: true);
            state.Grabbed = null;
        }
    }
}

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
            state.Grabbed.SetParent(controller, worldPositionStays: true);   // follow the hand rigidly
        }

        private void Release(HandState state)
        {
            if (state.Grabbed != null) state.Grabbed.SetParent(null, worldPositionStays: true);
            state.Grabbed = null;
        }
    }
}

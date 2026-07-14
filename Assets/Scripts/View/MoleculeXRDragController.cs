// XR equivalent of MoleculeDragController.
//
// Picking (which atom to grab) uses the controller's RAY - point at the atom, pull trigger.
// Dragging (moving the grabbed atom) uses the controller's HAND POSITION - the direction from
// the molecule center to the controller's position drives the atom direction. Move your hand
// up, atom goes up; move left, atom goes left. This is the kinetic model VR users expect.
//
// We don't use ray-sphere projection while dragging because in VR the controller is often
// INSIDE the constraint sphere (the molecule, scaled to ~0.03, has its sphere only ~30 cm wide),
// which makes ray-sphere intersection return the far-side exit point and produces sudden jumps.
//
// Offset compensation: same Quaternion.FromToRotation(handDir, atomDir) trick from the desktop
// drag, so the angular relationship between where your hand is and where the atom is gets
// captured at click time and preserved through the drag.
//
// Wired in the Inspector to the Starter Assets' XRI Default Input Actions:
//   leftTriggerAction  -> XRI LeftHand Interaction / Activate   (index trigger as a button)
//   rightTriggerAction -> XRI RightHand Interaction / Activate
// Controller transforms are the LeftHand Controller / RightHand Controller GameObjects under
// XR Origin / Camera Offset (forward direction is the ray direction, position is the hand pose).

using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Mathematics;
using Molecule_Shapes.Model;

namespace Molecule_Shapes.View
{
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(MoleculeController))]
    public class MoleculeXRDragController : MonoBehaviour
    {
        [Header("Controller transforms (forward is ray direction)")]
        [SerializeField] private Transform leftController;
        [SerializeField] private Transform rightController;

        [Header("Trigger input actions (XRI <hand> Interaction / Activate)")]
        [SerializeField] private InputActionReference leftTriggerAction;
        [SerializeField] private InputActionReference rightTriggerAction;

        [Header("Cycle bond order button")]
        [Tooltip("Controller button that cycles the active atom's bond order. Default = the right " +
                 "controller's B button. Change to <XRController>{LeftHand}/primaryButton etc. if you like.")]
        [SerializeField] private string cycleBondButtonBinding = "<XRController>{RightHand}/secondaryButton";

        [Header("Pick radii (model units)")]
        [Tooltip("Hit-test radius for atom spheres.")]
        [SerializeField] private float atomPickRadius = 1.4f;
        [Tooltip("Hit-test radius for lone-pair balloons.")]
        [SerializeField] private float lonePairPickRadius = 1.8f;

        public bool IsDragging => _leftDrag.Group != null || _rightDrag.Group != null;
        public bool IsLeftDragging => _leftDrag.Group != null;
        public bool IsRightDragging => _rightDrag.Group != null;

        private MoleculeController _controller;
        private InputAction _cycleBondAction;
        private readonly DragState _leftDrag = new();
        private readonly DragState _rightDrag = new();

        private class DragState
        {
            public PairGroup Group;
            public Quaternion OffsetRotation;
            public float ConstraintRadius;
            public bool WasPressed;
        }

        private void Awake()
        {
            _controller = GetComponent<MoleculeController>();
        }

        private void OnEnable()
        {
            if (leftTriggerAction != null && leftTriggerAction.action != null) leftTriggerAction.action.Enable();
            if (rightTriggerAction != null && rightTriggerAction.action != null) rightTriggerAction.action.Enable();

            // Code-bound so it needs no action asset wiring - press the controller's B button to cycle bond order.
            if (_cycleBondAction == null && !string.IsNullOrEmpty(cycleBondButtonBinding))
            {
                _cycleBondAction = new InputAction("CycleBondOrder", InputActionType.Button, cycleBondButtonBinding);
                _cycleBondAction.performed += OnCycleBondPressed;
            }
            _cycleBondAction?.Enable();
        }

        private void OnDisable()
        {
            // Don't disable shared trigger actions globally — other scripts may use them.
            _cycleBondAction?.Disable();
        }

        private void OnDestroy()
        {
            if (_cycleBondAction != null)
            {
                _cycleBondAction.performed -= OnCycleBondPressed;
                _cycleBondAction.Dispose();
                _cycleBondAction = null;
            }
        }

        private void OnCycleBondPressed(InputAction.CallbackContext ctx)
        {
            if (_controller != null) _controller.CycleBondOrder();
        }

        private void Update()
        {
            UpdateHand(leftController, leftTriggerAction, _leftDrag);
            UpdateHand(rightController, rightTriggerAction, _rightDrag);
        }

        private void UpdateHand(Transform controller, InputActionReference triggerRef, DragState state)
        {
            if (controller == null || triggerRef == null || triggerRef.action == null) return;

            bool pressed = triggerRef.action.IsPressed();
            bool wasPressed = state.WasPressed;
            state.WasPressed = pressed;

            if (pressed && !wasPressed)
            {
                TryBeginDrag(controller, state);
            }
            else if (state.Group != null)
            {
                if (pressed) UpdateDrag(controller, state);
                else EndDrag(state);
            }
        }

        private void TryBeginDrag(Transform controller, DragState state)
        {
            VsepRMolecule molecule = _controller.Molecule;
            if (molecule == null) return;

            // Controller ray in the molecule's LOCAL frame (so user rotation of the molecule is accounted for).
            Vector3 localOrigin = transform.InverseTransformPoint(controller.position);
            Vector3 localDir = transform.InverseTransformDirection(controller.forward).normalized;

            // Closest radial group whose visible sphere the ray hits.
            float closestT = float.PositiveInfinity;
            PairGroup picked = null;
            for (int i = 0; i < molecule.RadialGroups.Count; i++)
            {
                PairGroup pg = molecule.RadialGroups[i];
                float pickRadius = pg.IsLonePair ? lonePairPickRadius : atomPickRadius;
                if (RaySphereIntersect(localOrigin, localDir, ToVector3(pg.Position), pickRadius, out float t)
                    && t < closestT)
                {
                    closestT = t;
                    picked = pg;
                }
            }

            if (picked == null) return;

            state.ConstraintRadius = picked.IsLonePair ? PairGroup.LonePairDistance : PairGroup.BondedPairDistance;

            // Offset compensation: capture the angular relationship between hand and atom at
            // click time. Hand direction = direction from molecule center to the controller in
            // local space (not the ray direction). The offset preserves "click point on atom"
            // through the drag - if you grabbed the atom's edge, the edge stays under your hand.
            Vector3 handDir = SafeNormalize(localOrigin);
            Vector3 atomDir = SafeNormalize(ToVector3(picked.Position));
            state.OffsetRotation = Quaternion.FromToRotation(handDir, atomDir);

            state.Group = picked;
            state.Group.UserControlled = true;

            // Triggering a bonded atom makes it the target for Cycle Bond Order (lone pairs don't count).
            if (!picked.IsLonePair) _controller.SetActiveBondAtom(picked);
        }

        private void UpdateDrag(Transform controller, DragState state)
        {
            Vector3 localOrigin = transform.InverseTransformPoint(controller.position);

            // Guard against the controller sitting exactly at the molecule center (direction undefined).
            if (localOrigin.sqrMagnitude < 1e-4f) return;

            Vector3 handDir = localOrigin.normalized;
            Vector3 atomDir = state.OffsetRotation * handDir;

            Vector3 newPosLocal = atomDir * state.ConstraintRadius;
            state.Group.SetPosition(new float3(newPosLocal.x, newPosLocal.y, newPosLocal.z));
        }

        private void EndDrag(DragState state)
        {
            if (state.Group == null) return;
            state.Group.UserControlled = false;
            state.Group = null;
        }

        private static bool RaySphereIntersect(Vector3 rayOrigin, Vector3 rayDir, Vector3 sphereCenter, float radius, out float t)
        {
            Vector3 oc = rayOrigin - sphereCenter;
            float a = Vector3.Dot(rayDir, rayDir);
            float b = 2f * Vector3.Dot(oc, rayDir);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            float disc = b * b - 4f * a * c;
            if (disc < 0f) { t = 0f; return false; }
            float sqrtDisc = Mathf.Sqrt(disc);
            float t1 = (-b - sqrtDisc) / (2f * a);
            float t2 = (-b + sqrtDisc) / (2f * a);
            if (t1 >= 0f) { t = t1; return true; }
            if (t2 >= 0f) { t = t2; return true; }
            t = 0f;
            return false;
        }

        private static Vector3 ToVector3(float3 v) => new Vector3(v.x, v.y, v.z);
        private static Vector3 SafeNormalize(Vector3 v) => v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.right;
    }
}

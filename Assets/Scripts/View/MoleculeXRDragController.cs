// XR equivalent of MoleculeDragController. Same offset-compensation math (a rotation on the
// constraint sphere that preserves the spatial relationship between click point and atom
// center), but reads a CONTROLLER ray + index TRIGGER instead of a mouse ray + left button.
//
// Each hand independently can drag an atom or lone pair. The molecule's two-handed GRIP grab
// (XRGrabInteractable) is on a different button entirely (grip) so they don't directly conflict;
// don't squeeze grip and trigger on the same controller at the same time.
//
// Wired in the Inspector to the Starter Assets' XRI Default Input Actions:
//   leftTriggerAction  -> XRI LeftHand Interaction / Activate   (index trigger as a button)
//   rightTriggerAction -> XRI RightHand Interaction / Activate
// Controller transforms are the LeftHand Controller / RightHand Controller GameObjects under
// XR Origin / Camera Offset (forward direction is the ray direction).

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

        [Header("Pick radii (model units)")]
        [Tooltip("Hit-test radius for atom spheres.")]
        [SerializeField] private float atomPickRadius = 1.4f;
        [Tooltip("Hit-test radius for lone-pair balloons.")]
        [SerializeField] private float lonePairPickRadius = 1.8f;

        public bool IsDragging => _leftDrag.Group != null || _rightDrag.Group != null;

        private MoleculeController _controller;
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
        }

        private void OnDisable()
        {
            // Don't disable shared actions globally — other scripts may use them.
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

            // Same offset-compensation as the desktop drag: record the rotation from
            // "where the cursor would put the atom" to "where the atom actually is".
            Vector3 clickDir = CursorDirectionOnConstraintSphere(localOrigin, localDir, state.ConstraintRadius);
            Vector3 atomDir = SafeNormalize(ToVector3(picked.Position));
            state.OffsetRotation = Quaternion.FromToRotation(clickDir, atomDir);

            state.Group = picked;
            state.Group.UserControlled = true;
        }

        private void UpdateDrag(Transform controller, DragState state)
        {
            Vector3 localOrigin = transform.InverseTransformPoint(controller.position);
            Vector3 localDir = transform.InverseTransformDirection(controller.forward).normalized;

            Vector3 cursorDir = CursorDirectionOnConstraintSphere(localOrigin, localDir, state.ConstraintRadius);
            Vector3 atomDir = state.OffsetRotation * cursorDir;

            Vector3 newPosLocal = atomDir * state.ConstraintRadius;
            state.Group.SetPosition(new float3(newPosLocal.x, newPosLocal.y, newPosLocal.z));
        }

        private void EndDrag(DragState state)
        {
            if (state.Group == null) return;
            state.Group.UserControlled = false;
            state.Group = null;
        }

        // Direction from molecule center to where the cursor "projects" onto the constraint sphere.
        // Falls back to the closest point on the ray when the ray misses (so dragging past the silhouette
        // keeps moving instead of stalling).
        private static Vector3 CursorDirectionOnConstraintSphere(Vector3 rayOrigin, Vector3 rayDir, float radius)
        {
            if (RaySphereIntersect(rayOrigin, rayDir, Vector3.zero, radius, out float t))
            {
                Vector3 hit = rayOrigin + t * rayDir;
                return SafeNormalize(hit);
            }
            float u = -Vector3.Dot(rayOrigin, rayDir);
            if (u < 0f) u = 0f;
            Vector3 closest = rayOrigin + u * rayDir;
            return SafeNormalize(closest);
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

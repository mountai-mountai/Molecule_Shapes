// Click-and-drag an atom or lone pair. While held, the PairGroup is set UserControlled, so the
// model's StepForward / AttractToIdealDistance / AddPosition / AddVelocity all early-return for
// it - the user is in charge of its position. The rest of the molecule reacts via the normal
// attractor + Coulomb forces. On release, UserControlled is cleared and the attractor pulls
// everything back to the ideal geometry.
//
// Offset compensation: a naive drag would snap the atom's CENTER to wherever the cursor ray
// hits the constraint sphere ("jump to cursor"). Instead, on click we record the rotation that
// maps the cursor's projected direction onto the atom's actual direction (Quaternion.FromToRotation),
// and apply that same rotation to the cursor's projection each frame. The point you grabbed
// stays under your cursor for the entire drag.
//
// [DefaultExecutionOrder(-10)] ensures this script runs before MoleculeRotator, so the rotator
// can read IsDragging reliably and abstain when a drag is in progress.

using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Mathematics;
using Molecule_Shapes.Model;

namespace Molecule_Shapes.View
{
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(MoleculeController))]
    public class MoleculeDragController : MonoBehaviour
    {
        [Header("Pick radii (model units)")]
        [Tooltip("Click hit-test radius for atom spheres.")]
        [SerializeField] private float atomPickRadius = 1.4f;
        [Tooltip("Click hit-test radius for lone-pair balloons.")]
        [SerializeField] private float lonePairPickRadius = 1.8f;

        public bool IsDragging => _dragged != null;

        private MoleculeController _controller;
        private Camera _camera;

        private PairGroup _dragged;
        private Quaternion _offsetRotation;   // maps cursorDir -> atomDir while held
        private float _constraintRadius;      // distance to keep the atom from the molecule origin

        private void Awake()
        {
            _controller = GetComponent<MoleculeController>();
            _camera = Camera.main;
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || _camera == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                TryBeginDrag(mouse.position.ReadValue());
            }
            else if (_dragged != null)
            {
                if (mouse.leftButton.isPressed)
                {
                    UpdateDrag(mouse.position.ReadValue());
                }
                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    EndDrag();
                }
            }
        }

        private void TryBeginDrag(Vector2 screenPos)
        {
            VsepRMolecule molecule = _controller.Molecule;
            if (molecule == null) return;

            // Camera ray converted into the molecule's LOCAL frame (so user rotation of the
            // molecule transform is accounted for; atoms live in model/local coordinates).
            Ray worldRay = _camera.ScreenPointToRay(screenPos);
            Vector3 localOrigin = transform.InverseTransformPoint(worldRay.origin);
            Vector3 localDir = transform.InverseTransformDirection(worldRay.direction).normalized;

            // Find the closest radial group whose visible sphere the ray hits.
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

            // Constraint sphere radius for this pair group (atoms at bond length, lone pairs closer).
            _constraintRadius = picked.IsLonePair ? PairGroup.LonePairDistance : PairGroup.BondedPairDistance;

            // Offset = rotation from "where the cursor would have put the atom" to "where the atom actually is."
            Vector3 clickDir = CursorDirectionOnConstraintSphere(localOrigin, localDir, _constraintRadius);
            Vector3 atomDir = SafeNormalize(ToVector3(picked.Position));
            _offsetRotation = Quaternion.FromToRotation(clickDir, atomDir);

            _dragged = picked;
            _dragged.UserControlled = true;
        }

        private void UpdateDrag(Vector2 screenPos)
        {
            Ray worldRay = _camera.ScreenPointToRay(screenPos);
            Vector3 localOrigin = transform.InverseTransformPoint(worldRay.origin);
            Vector3 localDir = transform.InverseTransformDirection(worldRay.direction).normalized;

            Vector3 cursorDir = CursorDirectionOnConstraintSphere(localOrigin, localDir, _constraintRadius);
            Vector3 atomDir = _offsetRotation * cursorDir;

            Vector3 newPosLocal = atomDir * _constraintRadius;
            _dragged.SetPosition(new float3(newPosLocal.x, newPosLocal.y, newPosLocal.z));
        }

        private void EndDrag()
        {
            if (_dragged == null) return;
            _dragged.UserControlled = false;
            _dragged = null;
        }

        // Direction from the molecule center toward where the cursor "projects" onto the constraint
        // sphere. Falls back to the closest point on the ray when the ray misses the sphere, so
        // dragging the cursor past the silhouette behaves smoothly instead of stalling.
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

        // Smallest non-negative t such that |origin + t*dir - center| = radius. Returns true if it exists.
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

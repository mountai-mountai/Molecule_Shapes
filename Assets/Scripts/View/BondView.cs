using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Molecule_Shapes.Model;

namespace Molecule_Shapes.View
{
    // Renders a bond between two pair groups as 1, 2, or 3 parallel cylinders (single / double / triple),
    // stretched and oriented every frame. The multiple cylinders spread perpendicular to the bond AND to
    // the view direction, so a double/triple bond always reads as separate sticks from any angle (the
    // PhET behavior) instead of collapsing to one when viewed edge-on.
    public class BondView : MonoBehaviour
    {
        private Bond _bond;
        private float _thickness = 1f;
        private Material _material;
        private int _order;
        private Camera _camera;

        private readonly List<Transform> _cylinders = new();

        public BondView Initialize(Bond bond, float thickness, Material material)
        {
            _bond = bond;
            _thickness = thickness;
            _material = material;
            SetOrder(bond.Order);
            return this;
        }

        // Rebuilds the cylinder set when the bond order changes (1..3).
        public void SetOrder(int order)
        {
            order = Mathf.Clamp(order, 1, 3);
            if (order == _order && _cylinders.Count == order) return;

            foreach (Transform c in _cylinders) if (c != null) Destroy(c.gameObject);
            _cylinders.Clear();

            for (int i = 0; i < order; i++)
            {
                var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cyl.name = "BondCyl";
                Collider col = cyl.GetComponent<Collider>();
                if (col != null) Destroy(col);
                cyl.transform.SetParent(transform, false);
                cyl.GetComponent<MeshRenderer>().material = _material;
                _cylinders.Add(cyl.transform);
            }
            _order = order;
        }

        private void LateUpdate()
        {
            if (_bond == null || _cylinders.Count == 0) return;

            Vector3 a = ToVector3(_bond.A.Position);
            Vector3 b = ToVector3(_bond.B.Position);
            Vector3 mid = (a + b) * 0.5f;
            Vector3 diff = b - a;
            float length = diff.magnitude;
            if (length < 1e-4f) return;

            Vector3 dir = diff / length;
            Vector3 perp = ViewPerpendicular(dir);
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);
            // Unity's cylinder is 2 units tall, radius 0.5, so y-scale = length / 2.
            Vector3 scale = new Vector3(_thickness, length * 0.5f, _thickness);

            int n = _cylinders.Count;
            float spacing = _thickness * 1.35f;   // centre-to-centre gap between multiple bonds
            for (int i = 0; i < n; i++)
            {
                float offset = (i - (n - 1) * 0.5f) * spacing;
                _cylinders[i].localPosition = mid + perp * offset;
                _cylinders[i].localRotation = rot;
                _cylinders[i].localScale = scale;
            }
        }

        // Direction perpendicular to the bond and to the camera view, in this object's local space, so
        // the parallel bonds spread sideways relative to what the viewer sees.
        private Vector3 ViewPerpendicular(Vector3 dirLocal)
        {
            if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
            Vector3 viewLocal = _camera != null
                ? transform.InverseTransformDirection(_camera.transform.forward)
                : Vector3.forward;

            Vector3 perp = Vector3.Cross(dirLocal, viewLocal);
            if (perp.sqrMagnitude < 1e-5f) perp = Vector3.Cross(dirLocal, Vector3.up);
            if (perp.sqrMagnitude < 1e-5f) perp = Vector3.Cross(dirLocal, Vector3.right);
            return perp.normalized;
        }

        private static Vector3 ToVector3(float3 v) => new Vector3(v.x, v.y, v.z);
    }
}

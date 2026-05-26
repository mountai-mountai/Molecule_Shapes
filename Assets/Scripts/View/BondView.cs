using UnityEngine;
using Unity.Mathematics;
using Molecule_Shapes.Model;

namespace Molecule_Shapes.View
{
    // Stretches/orients a cylinder between the two pair groups of a Bond, every frame.
    public class BondView : MonoBehaviour
    {
        private Bond _bond;
        private float _thickness = 1f;

        public BondView Initialize(Bond bond, float thickness)
        {
            _bond = bond;
            _thickness = thickness;
            return this;
        }

        private void LateUpdate()
        {
            if (_bond == null) return;

            Vector3 a = ToVector3(_bond.A.Position);
            Vector3 b = ToVector3(_bond.B.Position);
            Vector3 diff = b - a;
            float length = diff.magnitude;

            transform.localPosition = (a + b) * 0.5f;

            if (length > 1e-4f)
                transform.localRotation = Quaternion.FromToRotation(Vector3.up, diff);

            // Unity's default cylinder is 2 units tall, radius 0.5, so y-scale = length / 2.
            transform.localScale = new Vector3(_thickness, length * 0.5f, _thickness);
        }

        private static Vector3 ToVector3(float3 v) => new Vector3(v.x, v.y, v.z);
    }
}

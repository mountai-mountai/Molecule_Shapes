using UnityEngine;
using Unity.Mathematics;
using Molecule_Shapes.Model;

namespace Molecule_Shapes.View
{
    // Renders a lone pair as a translucent ovoid ("balloon") whose long axis points radially
    // outward from the molecule center. A simplified stand-in for PhET's balloon mesh.
    //
    // NOTE: orients along the radial direction from the origin, which is correct for lone pairs on
    // the (origin) central atom. Terminal lone pairs on outer atoms would need the parent position.
    public class LonePairView : MonoBehaviour
    {
        private PairGroup _group;
        private float _diameter = 2f;
        private float _elongation = 1.6f;

        public LonePairView Initialize(PairGroup group, float diameter, float elongation)
        {
            _group = group;
            _diameter = diameter;
            _elongation = elongation;
            _group.PositionChanged += OnPositionChanged;
            OnPositionChanged(group.Position);
            return this;
        }

        private void OnPositionChanged(float3 p)
        {
            Vector3 pos = new Vector3(p.x, p.y, p.z);
            transform.localPosition = pos;

            Vector3 outward = pos.sqrMagnitude > 1e-6f ? pos.normalized : Vector3.up;
            transform.localRotation = Quaternion.FromToRotation(Vector3.up, outward);

            // Sphere primitive is unit-diameter; elongate along local up (the radial axis).
            transform.localScale = new Vector3(_diameter, _diameter * _elongation, _diameter);
        }

        private void OnDestroy()
        {
            if (_group != null) _group.PositionChanged -= OnPositionChanged;
        }
    }
}

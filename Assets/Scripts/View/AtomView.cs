using UnityEngine;
using Unity.Mathematics;
using Molecule_Shapes.Model;

namespace Molecule_Shapes.View
{
    // Syncs a sphere GameObject to a model PairGroup's position. Model units are used directly
    // (parent the view under a scaled root if you want to shrink the whole molecule).
    public class AtomView : MonoBehaviour
    {
        private PairGroup _group;

        public AtomView Bind(PairGroup group)
        {
            _group = group;
            _group.PositionChanged += OnPositionChanged;
            OnPositionChanged(group.Position);
            return this;
        }

        private void OnPositionChanged(float3 p)
        {
            transform.localPosition = new Vector3(p.x, p.y, p.z);
        }

        private void OnDestroy()
        {
            if (_group != null) _group.PositionChanged -= OnPositionChanged;
        }
    }
}

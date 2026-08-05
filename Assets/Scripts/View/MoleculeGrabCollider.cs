// Keeps the molecule's grab collider tight around the actual molecule instead of a fixed oversized box.
//
// Why: a 20-unit BoxCollider reaches ~17 units to its corners even when the molecule is a lone central
// atom, so its invisible bulk sits in front of the UI panels and swallows controller rays meant for
// buttons. This resizes a SphereCollider every time the molecule changes, so the grab volume is only
// ever as big as the farthest group plus a small margin - the ray passes through empty space to the UI.
//
// A sphere (not per-atom colliders) is the right trade here: one collider, one radius write on edit,
// no per-frame cost, and it matches the molecule's actual radial shape - every group sits at the same
// bond/lone-pair distance from the centre.
//
// Setup: put this on the Molecule object (alongside MoleculeController / XRGrabInteractable). Any
// existing BoxCollider is disabled automatically - XRGrabInteractable will use this sphere instead.

using UnityEngine;
using Molecule_Shapes.Model;

namespace Molecule_Shapes.View
{
    // Runs before XRGrabInteractable's Awake, which caches the colliders it will use - the sphere has to
    // exist by then or the grab would fall back to the (disabled) box.
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(MoleculeController))]
    public class MoleculeGrabCollider : MonoBehaviour
    {
        [Tooltip("Extra radius beyond the farthest group, in MODEL units (the same units as the bond " +
                 "distance of 10). Bigger = easier to grab but more likely to block UI rays.")]
        [SerializeField] private float marginModelUnits = 2.5f;

        [Tooltip("Smallest radius in model units, so a bare central atom is still grabbable.")]
        [SerializeField] private float minRadiusModelUnits = 4f;

        [Tooltip("Disable a BoxCollider on this object on start (it's the bulky one this replaces).")]
        [SerializeField] private bool disableBoxCollider = true;

        private MoleculeController _controller;
        private SphereCollider _sphere;
        private VsepRMolecule _molecule;

        private void Awake()
        {
            _controller = GetComponent<MoleculeController>();

            if (disableBoxCollider && TryGetComponent(out BoxCollider box) && box.enabled)
                box.enabled = false;   // replaced by the fitted sphere below

            _sphere = GetComponent<SphereCollider>();
            if (_sphere == null) _sphere = gameObject.AddComponent<SphereCollider>();
            _sphere.center = Vector3.zero;
            _sphere.isTrigger = false;   // XR Ray Interactor ignores triggers by default
        }

        private void Start()
        {
            _molecule = _controller.Molecule;
            if (_molecule != null)
            {
                _molecule.GroupAdded += OnGroupsChanged;
                _molecule.GroupRemoved += OnGroupsChanged;
            }
            Refit();
        }

        private void OnDestroy()
        {
            if (_molecule == null) return;
            _molecule.GroupAdded -= OnGroupsChanged;
            _molecule.GroupRemoved -= OnGroupsChanged;
        }

        private void OnGroupsChanged(PairGroup _) => Refit();

        /// <summary>Resizes the sphere to enclose the farthest radial group plus the margin.</summary>
        [ContextMenu("Refit Now")]
        public void Refit()
        {
            if (_sphere == null || _molecule == null) return;

            // Groups sit at a fixed distance from the centre (bond 10 / lone pair 7), so the farthest
            // one defines the radius. Using the model's distances avoids reading transforms every frame.
            float farthest = 0f;
            foreach (PairGroup g in _molecule.RadialGroups)
            {
                float d = g.IsLonePair ? PairGroup.LonePairDistance : PairGroup.BondedPairDistance;
                if (d > farthest) farthest = d;
            }

            float radius = Mathf.Max(minRadiusModelUnits, farthest + marginModelUnits);
            _sphere.radius = radius;   // local units; the molecule's own scale converts it to metres
        }
    }
}

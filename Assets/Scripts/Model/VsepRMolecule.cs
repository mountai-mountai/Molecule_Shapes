// Port of VSEPRMolecule.js - a molecule whose behavior distinguishes only lone pairs vs bonds
// (not bond order). Used in the "Model" screen.

using System.Collections.Generic;
using Unity.Mathematics;

namespace Molecule_Shapes.Model
{
    public class VsepRMolecule : Molecule
    {
        // Optional override of the displayed bond length. Null = use PairGroup.BondedPairDistance.
        public float? BondLengthOverride = null;

        // Optional target orientations for the CENTRAL atom's groups, replacing the textbook VSEPR
        // ideals. Set this (from RealGeometry) to make the molecule settle at a real molecule's measured
        // angles; null restores ideal model behaviour. Ordered lone-pairs-first, like the neighbour list.
        private IReadOnlyList<float3> _realOrientations;
        private int _realX = -1, _realE = -1;

        /// <summary>
        /// Aims the central atom's groups at measured orientations instead of the VSEPR ideals. The
        /// (x, e) it was built for is remembered and re-checked every frame: a bare count check is NOT
        /// enough, because different molecules can share a count (ammonia's 3 bonds + 1 lone pair is
        /// also 4 groups, so a plain tetrahedral molecule would silently inherit ammonia's geometry).
        /// Pass null to return to ideal model behaviour.
        /// </summary>
        public void SetRealOrientations(IReadOnlyList<float3> orientations, int x, int e)
        {
            _realOrientations = orientations;
            _realX = orientations != null ? x : -1;
            _realE = orientations != null ? e : -1;
        }

        public void ClearRealOrientations() => SetRealOrientations(null, -1, -1);

        /// <summary>True when the molecule really is being aimed at measured orientations - the override
        /// exists and the molecule still has exactly the composition it was built for.</summary>
        public bool IsUsingRealOrientations =>
            _realOrientations != null &&
            RadialAtoms.Count == _realX &&
            RadialLonePairs.Count == _realE &&
            _realOrientations.Count == _realX + _realE;

        // Least-squares attractor error from the most recent Update. 0 when there are fewer than two
        // radial groups (geometry is trivially settled). Exposed so the game layer can detect when the
        // molecule has actually "settled" into its ideal geometry and grade build accuracy.
        public float LastAttractorError { get; private set; }

        public VsepRMolecule() : base(isReal: false)
        {
        }

        public override void Update(float dt)
        {
            base.Update(dt);

            // Reset each frame; the central-atom branch below overwrites it when it runs.
            LastAttractorError = 0f;

            var radialGroups = RadialGroups;

            for (int i = 0; i < Atoms.Count; i++)
            {
                PairGroup atom = Atoms[i];

                if (GetNeighborCount(atom) > 1)
                {
                    if (atom.IsCentralAtom)
                    {
                        // attractive force toward the correct ideal positions
                        float error = GetLocalShape(atom).ApplyAttraction(dt);
                        LastAttractorError = error;

                        // Coulomb repulsion drives groups toward MAXIMAL separation - i.e. toward the
                        // textbook ideal. In real-molecule mode the measured orientations already are the
                        // answer, so letting repulsion pull against them just splits the difference and
                        // the molecule settles near the ideal angle instead of the measured one
                        // (water landing at ~109 rather than 104.5). Skip it while an override is active.
                        if (IsUsingRealOrientations)
                            continue;

                        // When near an ideal state, force Coulomb to ignore bond-vs-lone-pair distance differences.
                        float trueLengthsRatioOverride = math.max(0f, math.min(1f, math.log(error + 1f) - 0.5f));

                        for (int j = 0; j < radialGroups.Count; j++)
                        {
                            PairGroup group = radialGroups[j];
                            for (int k = 0; k < radialGroups.Count; k++)
                            {
                                PairGroup otherGroup = radialGroups[k];

                                if (otherGroup != group && group != CentralAtom)
                                {
                                    group.RepulseFrom(otherGroup, dt, trueLengthsRatioOverride);
                                }
                            }
                        }
                    }
                    else
                    {
                        // terminal lone pairs: angle-based attraction/repulsion locally
                        GetLocalShape(atom).ApplyAngleAttractionRepulsion(dt);
                    }
                }
            }
        }

        public override LocalShape GetLocalShape(PairGroup atom)
        {
            // Real-molecule mode: aim the central atom's groups at the measured orientations instead of
            // the VSEPR ideals. Falls back to ideal whenever the override doesn't match the current group
            // count (e.g. mid-edit), so a mismatch can never destabilise the attractor.
            if (atom.IsCentralAtom && IsUsingRealOrientations)
            {
                List<PairGroup> groups = GetNeighbors(atom);
                if (groups.Count == _realOrientations.Count)
                    return new LocalShape(LocalShape.VseprPermutations(groups), atom, groups,
                                          _realOrientations);
            }
            return GetLocalVseprShape(atom);
        }

        public override float GetMaximumBondLength()
        {
            return BondLengthOverride ?? PairGroup.BondedPairDistance;
        }
    }
}

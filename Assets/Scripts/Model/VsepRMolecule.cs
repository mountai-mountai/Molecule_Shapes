// Port of VSEPRMolecule.js - a molecule whose behavior distinguishes only lone pairs vs bonds
// (not bond order). Used in the "Model" screen.

using Unity.Mathematics;

namespace Molecule_Shapes.Model
{
    public class VsepRMolecule : Molecule
    {
        // Optional override of the displayed bond length. Null = use PairGroup.BondedPairDistance.
        public float? BondLengthOverride = null;

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
            return GetLocalVseprShape(atom);
        }

        public override float GetMaximumBondLength()
        {
            return BondLengthOverride ?? PairGroup.BondedPairDistance;
        }
    }
}

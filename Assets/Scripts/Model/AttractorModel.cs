// Port of AttractorModel.js - finds the closest VSEPR configuration (with rotation) to the current
// positions, then pushes the electron pairs toward those ideal positions.
//
// The SVD rotation step (computeRotationMatrixWithTranspose) is delegated to Core.RotationSolver,
// which uses the Mueller/Bender iterative rotation extraction instead of a 3x3 SVD.

using System.Collections.Generic;
using Unity.Mathematics;
using Molecule_Shapes.Core;

namespace Molecule_Shapes.Model
{
    public static class AttractorModel
    {
        // Result of matching current positions to ideal positions.
        public class ResultMapping
        {
            public readonly float Error;
            public readonly float3[] Target;      // ideal orientations rotated into the current frame
            public readonly Permutation Permutation;
            public readonly quaternion Rotation;  // maps ideal -> current

            public ResultMapping(float error, float3[] target, Permutation permutation, quaternion rotation)
            {
                Error = error;
                Target = target;
                Permutation = permutation;
                Rotation = rotation;
            }

            // Rotate a vector from the "current" frame's ideal into the rotated frame.
            public float3 RotateVector(float3 v) => math.mul(Rotation, v);
        }

        // Convenience: orientations (normalized positions) of an ordered list of pair groups.
        public static float3[] GetOrientationsFromOrigin(IReadOnlyList<PairGroup> groups)
        {
            var result = new float3[groups.Count];
            for (int i = 0; i < groups.Count; i++) result[i] = groups[i].Orientation;
            return result;
        }

        /// <summary>
        /// For each permutation, compute the best-fit rotation of the ideal orientations onto the
        /// current ones and keep the lowest-error mapping.
        /// </summary>
        public static ResultMapping FindClosestMatchingConfiguration(
            IReadOnlyList<float3> currentOrientations,
            IReadOnlyList<float3> idealOrientations,
            IReadOnlyList<Permutation> allowablePermutations,
            Permutation lastPermutation = null)
        {
            ResultMapping best = lastPermutation != null
                ? Evaluate(currentOrientations, idealOrientations, lastPermutation)
                : null;

            for (int p = 0; p < allowablePermutations.Count; p++)
            {
                ResultMapping result = Evaluate(currentOrientations, idealOrientations, allowablePermutations[p]);
                if (best == null || result.Error < best.Error) best = result;
            }
            return best;
        }

        private static ResultMapping Evaluate(
            IReadOnlyList<float3> currentOrientations,
            IReadOnlyList<float3> idealOrientations,
            Permutation permutation)
        {
            int n = currentOrientations.Count;

            // Permute the ideal columns: permutedIdeal[i] = idealOrientations[permutation.Indices[i]].
            var permutedIdeal = new float3[n];
            for (int i = 0; i < n; i++) permutedIdeal[i] = idealOrientations[permutation.Indices[i]];

            // Rotation mapping ideal -> current.
            quaternion rotation = RotationSolver.BestFitRotation(permutedIdeal, currentOrientations, quaternion.identity);

            var target = new float3[n];
            float error = 0f;
            for (int i = 0; i < n; i++)
            {
                target[i] = math.mul(rotation, permutedIdeal[i]);
                float3 diff = currentOrientations[i] - target[i];
                error += math.dot(diff, diff);
            }

            return new ResultMapping(error, target, permutation, rotation);
        }

        /// <summary>
        /// Applies an attraction toward the closest ideal configuration over the given timestep.
        /// Returns the chosen mapping and a least-squares-style error.
        /// </summary>
        public static (ResultMapping mapping, float error) ApplyAttractorForces(
            IReadOnlyList<PairGroup> groups, float dt,
            IReadOnlyList<float3> idealOrientations,
            IReadOnlyList<Permutation> allowablePermutations,
            float3 center, bool angleRepulsion, Permutation lastPermutation = null)
        {
            int n = groups.Count;

            var currentOrientations = new float3[n];
            for (int i = 0; i < n; i++) currentOrientations[i] = math.normalize(groups[i].Position - center);

            ResultMapping mapping = FindClosestMatchingConfiguration(
                currentOrientations, idealOrientations, allowablePermutations, lastPermutation);

            bool aroundCenterAtom = center.Equals(float3.zero);
            float totalDeltaMagnitude = 0f;

            for (int i = 0; i < n; i++)
            {
                PairGroup pair = groups[i];

                float3 targetOrientation = mapping.Target[i];
                float currentMagnitude = math.length(pair.Position - center);
                float3 targetPosition = targetOrientation * currentMagnitude + center;

                float3 delta = targetPosition - pair.Position;
                totalDeltaMagnitude += math.dot(delta, delta);

                // Squaring-the-distance behavior: more force when far, less when close.
                float strength = dt * 3f * math.length(delta);

                // velocity change for all pairs except an atom at the origin
                if (pair.IsLonePair || !pair.IsCentralAtom)
                {
                    if (aroundCenterAtom) pair.AddVelocity(delta * strength);
                }

                // direct position movement for faster convergence
                if (!pair.IsCentralAtom && aroundCenterAtom)
                {
                    pair.AddPosition(delta * (2f * dt));
                }

                // terminal lone pair: move more quickly with just this
                if (!pair.IsCentralAtom && !aroundCenterAtom)
                {
                    pair.AddPosition(delta * math.min(20f * dt, 1f));
                }
            }

            float error = math.sqrt(totalDeltaMagnitude);

            // Angle-based repulsion (pushes bond angles toward ideal).
            if (angleRepulsion && aroundCenterAtom)
            {
                for (int aIndex = 0; aIndex < n; aIndex++)
                {
                    for (int bIndex = aIndex + 1; bIndex < n; bIndex++)
                    {
                        PairGroup a = groups[aIndex];
                        PairGroup b = groups[bIndex];

                        float3 aOrientation = math.normalize(a.Position - center);
                        float3 bOrientation = math.normalize(b.Position - center);

                        float3 aTarget = math.normalize(mapping.Target[aIndex]);
                        float3 bTarget = math.normalize(mapping.Target[bIndex]);

                        float targetAngle = math.acos(math.clamp(math.dot(aTarget, bTarget), -1f, 1f));
                        float currentAngle = math.acos(math.clamp(math.dot(aOrientation, bOrientation), -1f, 1f));
                        float angleDifference = targetAngle - currentAngle;

                        float3 dirTowardsA = math.normalize(a.Position - b.Position);
                        float timeFactor = PairGroup.TimescaleImpulseFactor(dt);

                        // Dampen the push if the permutation switched (prevents oscillation).
                        float oscillationPreventionFactor =
                            (lastPermutation != null && !lastPermutation.Equals(mapping.Permutation)) ? 0.5f : 1f;

                        float extraClosePushFactor = math.clamp(
                            3f * math.pow(math.PI - currentAngle, 2f) / (math.PI * math.PI), 1f, 3f);

                        float3 push = dirTowardsA * (oscillationPreventionFactor *
                                                     timeFactor *
                                                     angleDifference *
                                                     PairGroup.AngleRepulsionScale *
                                                     (currentAngle < targetAngle ? 2.0f : 0.5f) *
                                                     extraClosePushFactor);
                        a.AddVelocity(push);
                        b.AddVelocity(-push);
                    }
                }
            }

            return (mapping, error);
        }
    }
}

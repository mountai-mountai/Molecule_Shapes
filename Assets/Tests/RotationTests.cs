using NUnit.Framework;
using Unity.Mathematics;
using Molecule_Shapes.Core;

namespace Molecule_Shapes.Tests
{
    // Validates the Kabsch / Mueller-Bender rotation solver in isolation, before anything
    // downstream (AttractorModel) relies on it.
    public class RotationTests
    {
        // A spread-out set of directions to align (avoids degenerate colinear cases).
        private static readonly float3[] BasePoints =
        {
            math.normalize(new float3(1, 0, 0)),
            math.normalize(new float3(0, 1, 0)),
            math.normalize(new float3(0, 0, 1)),
            math.normalize(new float3(1, 1, 1)),
        };

        [Test]
        public void RecoversKnownRotation()
        {
            // A known rotation: 50 deg about a tilted axis.
            quaternion known = quaternion.AxisAngle(math.normalize(new float3(0.3f, 1f, 0.5f)), math.radians(50f));

            var from = BasePoints;
            var to = new float3[from.Length];
            for (int i = 0; i < from.Length; i++) to[i] = math.mul(known, from[i]);

            quaternion solved = RotationSolver.BestFitRotation(from, to, quaternion.identity);

            // Compare by how well it maps from -> to (quaternions have a sign ambiguity, so compare action).
            for (int i = 0; i < from.Length; i++)
            {
                float3 mapped = math.mul(solved, from[i]);
                Assert.Less(math.distance(mapped, to[i]), 1e-3f, $"point {i} not aligned");
            }
        }

        [Test]
        public void IdenticalSets_GiveIdentity()
        {
            quaternion solved = RotationSolver.BestFitRotation(BasePoints, BasePoints, quaternion.identity);
            for (int i = 0; i < BasePoints.Length; i++)
            {
                float3 mapped = math.mul(solved, BasePoints[i]);
                Assert.Less(math.distance(mapped, BasePoints[i]), 1e-4f);
            }
        }

        [Test]
        public void Result_IsOrthonormal()
        {
            quaternion known = quaternion.AxisAngle(math.normalize(new float3(1f, 2f, -1f)), 1.1f);
            var from = BasePoints;
            var to = new float3[from.Length];
            for (int i = 0; i < from.Length; i++) to[i] = math.mul(known, from[i]);

            float3x3 r = new float3x3(RotationSolver.BestFitRotation(from, to, quaternion.identity));

            // R^T * R should be identity, det(R) should be +1.
            float3x3 shouldBeIdentity = math.mul(math.transpose(r), r);
            Assert.Less(math.length(shouldBeIdentity.c0 - new float3(1, 0, 0)), 1e-3f);
            Assert.Less(math.length(shouldBeIdentity.c1 - new float3(0, 1, 0)), 1e-3f);
            Assert.Less(math.length(shouldBeIdentity.c2 - new float3(0, 0, 1)), 1e-3f);
            Assert.Less(math.abs(math.determinant(r) - 1f), 1e-3f);
        }
    }
}

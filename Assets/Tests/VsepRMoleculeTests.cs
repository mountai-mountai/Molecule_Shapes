using NUnit.Framework;
using Unity.Mathematics;
using Molecule_Shapes.Model;

namespace Molecule_Shapes.Tests
{
    // Convergence tests for the full model physics (attractor + Coulomb). Pure C#, no scene needed,
    // so these run in Edit Mode by stepping the model in a plain loop.
    public class VsepRMoleculeTests
    {
        private const float TetrahedralAngleDeg = 109.4712f;

        private static VsepRMolecule BuildWithBondedAtoms(float3[] startDirections)
        {
            var molecule = new VsepRMolecule();

            var central = new PairGroup(float3.zero, isLonePair: false);
            molecule.AddCentralAtom(central);

            foreach (float3 dir in startDirections)
            {
                float3 pos = math.normalize(dir) * PairGroup.BondedPairDistance;
                var atom = new PairGroup(pos, isLonePair: false);
                molecule.AddGroupAndBond(atom, central, bondOrder: 1, bondLength: PairGroup.BondedPairDistance);
            }

            return molecule;
        }

        private static void Step(Molecule molecule, int steps, float dt)
        {
            for (int i = 0; i < steps; i++) molecule.Update(dt);
        }

        // angle (degrees) between two radial atoms as seen from the (origin) central atom
        private static float AngleBetweenDeg(PairGroup a, PairGroup b)
        {
            float3 ao = math.normalize(a.Position);
            float3 bo = math.normalize(b.Position);
            return math.degrees(math.acos(math.clamp(math.dot(ao, bo), -1f, 1f)));
        }

        [Test]
        public void FourBondedAtoms_ConvergeToTetrahedral()
        {
            // Deliberately asymmetric starting directions so the molecule must rearrange.
            var molecule = BuildWithBondedAtoms(new[]
            {
                new float3(1f, 0f, 0f),
                new float3(0f, 1f, 0.2f),
                new float3(-0.5f, -0.5f, 0.1f),
                new float3(0.3f, 0.3f, 1f),
            });

            Step(molecule, steps: 4000, dt: 0.02f);

            var atoms = molecule.RadialAtoms;
            Assert.AreEqual(4, atoms.Count);

            // All six pairwise bond angles should be the tetrahedral angle.
            for (int i = 0; i < atoms.Count; i++)
            {
                for (int j = i + 1; j < atoms.Count; j++)
                {
                    float angle = AngleBetweenDeg(atoms[i], atoms[j]);
                    Assert.AreEqual(TetrahedralAngleDeg, angle, 3.0f,
                        $"angle between atoms {i},{j} was {angle:F2} deg");
                }
            }
        }

        [Test]
        public void TwoBondedAtoms_ConvergeToLinear()
        {
            var molecule = BuildWithBondedAtoms(new[]
            {
                new float3(1f, 0f, 0f),
                new float3(0.7f, 0.7f, 0f), // not yet opposite
            });

            Step(molecule, steps: 3000, dt: 0.02f);

            var atoms = molecule.RadialAtoms;
            float angle = AngleBetweenDeg(atoms[0], atoms[1]);
            Assert.AreEqual(180f, angle, 3.0f, $"linear angle was {angle:F2} deg");
        }

        [Test]
        public void BondedAtoms_StayAtIdealDistance()
        {
            var molecule = BuildWithBondedAtoms(new[]
            {
                new float3(1f, 0f, 0f),
                new float3(0f, 1f, 0f),
                new float3(0f, 0f, 1f),
            });

            Step(molecule, steps: 3000, dt: 0.02f);

            foreach (PairGroup atom in molecule.RadialAtoms)
            {
                float distance = math.length(atom.Position);
                Assert.AreEqual(PairGroup.BondedPairDistance, distance, 0.5f,
                    $"atom drifted to distance {distance:F2}");
            }
        }
    }
}

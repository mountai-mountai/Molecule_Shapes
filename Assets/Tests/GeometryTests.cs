using NUnit.Framework;
using Molecule_Shapes.Model;
using Molecule_Shapes.Core;

namespace Molecule_Shapes.Tests
{
    // Edit Mode tests for the pure-data / pure-math layer. No GameObjects, no Play Mode.
    public class GeometryTests
    {
        [Test]
        public void TwoAtomsTwoLonePairs_IsBent()
        {
            var config = VseprConfiguration.GetConfiguration(2, 2);
            Assert.AreEqual(MoleculeGeometryKind.Bent, config.MoleculeGeometry.Kind);
        }

        [Test]
        public void Tetrahedral_HasFourBondSlots()
        {
            var config = VseprConfiguration.GetConfiguration(4, 0);
            Assert.AreEqual(4, config.BondOrientations.Count);
            Assert.AreEqual(0, config.LonePairOrientations.Count);
        }

        [Test]
        public void ElectronGeometry_TetrahedralStericNumberIsFour()
        {
            Assert.AreEqual(4, ElectronGeometry.GetConfiguration(4).StericNumber);
        }

        [Test]
        public void MoleculeGeometry_FiveAtomsIsTrigonalBipyramidal()
        {
            Assert.AreEqual(MoleculeGeometryKind.TrigonalBipyramidal,
                MoleculeGeometry.GetConfiguration(5, 0).Kind);
        }

        [Test]
        public void Permutation_IdentityHasSequentialIndices()
        {
            var p = Permutation.Identity(3);
            Assert.AreEqual(new[] { 0, 1, 2 }, p.Indices);
        }

        [Test]
        public void Permutation_AllPermutationsOfThree_AreSix()
        {
            Assert.AreEqual(6, Permutation.Permutations(3).Count);
        }
    }
}

// Port of LocalShape.js - the ideal local shape for a central atom and its neighbors, plus the
// permutation helpers that constrain how groups may map onto ideal orientation vectors.

using System.Collections.Generic;
using Unity.Mathematics;
using Molecule_Shapes.Core;

namespace Molecule_Shapes.Model
{
    public class LocalShape
    {
        // How we may map the groups onto the orientation vectors. Some combinations are disallowed.
        public readonly List<Permutation> AllowedPermutations;

        // All of our pair groups should be connected to this atom.
        public readonly PairGroup CentralAtom;

        public readonly IReadOnlyList<PairGroup> Groups;

        // Ideal orientations (unit vectors) for the groups, representing the ideal local shape.
        public readonly IReadOnlyList<float3> IdealOrientations;

        public LocalShape(List<Permutation> allowedPermutations, PairGroup centralAtom,
                          IReadOnlyList<PairGroup> groups, IReadOnlyList<float3> idealOrientations)
        {
            AllowedPermutations = allowedPermutations;
            CentralAtom = centralAtom;
            Groups = groups;
            IdealOrientations = idealOrientations;
        }

        // Attracts the atoms to their ideal shape (by adding velocity) and returns the current
        // least-squares-style error.
        public float ApplyAttraction(float dt)
        {
            return AttractorModel.ApplyAttractorForces(
                Groups, dt, IdealOrientations, AllowedPermutations,
                CentralAtom.Position, angleRepulsion: false).error;
        }

        // Forces pair-groups with similar angles away from each other (angle-based repulsion).
        public AttractorModel.ResultMapping ApplyAngleAttractionRepulsion(float dt, Permutation lastPermutation = null)
        {
            return AttractorModel.ApplyAttractorForces(
                Groups, dt, IdealOrientations, AllowedPermutations,
                CentralAtom.Position, angleRepulsion: true, lastPermutation).mapping;
        }

        // Given a list of permutations, return all permutations obtainable by permuting the
        // specified indices in every possible way.
        public static List<Permutation> PermuteListWithIndices(List<Permutation> permutations, List<int> indices)
        {
            // No changes possible if we can't move more than one element.
            if (indices.Count < 2) return permutations;

            var result = new List<Permutation>();
            foreach (Permutation permutation in permutations)
            {
                foreach (Permutation added in permutation.WithIndicesPermuted(indices))
                {
                    result.Add(added);
                }
            }
            return result;
        }

        // Allow switching of lone pairs with each other, and all bonds with each other (regardless of order).
        public static List<Permutation> VseprPermutations(IReadOnlyList<PairGroup> neighbors)
        {
            var permutations = new List<Permutation> { Permutation.Identity(neighbors.Count) };

            // partition neighbor indices into lone pairs and atoms (order preserved)
            var lonePairIndices = new List<int>();
            var atomIndices = new List<int>();
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (neighbors[i].IsLonePair) lonePairIndices.Add(i);
                else atomIndices.Add(i);
            }

            // permute away the lone pairs, then the bonded groups
            permutations = PermuteListWithIndices(permutations, lonePairIndices);
            permutations = PermuteListWithIndices(permutations, atomIndices);
            return permutations;
        }

        // Allow switching of lone pairs with each other, and bonds only with bonds of the SAME element.
        public static List<Permutation> RealPermutations(IReadOnlyList<PairGroup> neighbors)
        {
            var permutations = new List<Permutation> { Permutation.Identity(neighbors.Count) };

            // interchange lone pairs
            var lonePairIndices = new List<int>();
            for (int i = 0; i < neighbors.Count; i++)
                if (neighbors[i].IsLonePair) lonePairIndices.Add(i);
            permutations = PermuteListWithIndices(permutations, lonePairIndices);

            // interchange atoms that share the same chemical element
            var usedElements = new List<Element>();
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (neighbors[i].IsLonePair) continue;
                Element element = neighbors[i].Element;
                if (!usedElements.Contains(element)) usedElements.Add(element);
            }

            foreach (Element element in usedElements)
            {
                var sameElementIndices = new List<int>();
                for (int i = 0; i < neighbors.Count; i++)
                {
                    if (!neighbors[i].IsLonePair && neighbors[i].Element == element)
                        sameElementIndices.Add(i);
                }
                permutations = PermuteListWithIndices(permutations, sameElementIndices);
            }

            return permutations;
        }
    }
}

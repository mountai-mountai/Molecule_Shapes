// Port of VSEPRConfiguration.js - a configuration that distinguishes only lone pairs vs bonds
// (the "AXE method": X = radial atoms, E = radial lone pairs).

using System.Collections.Generic;
using Unity.Mathematics;
using Molecule_Shapes.Core;

namespace Molecule_Shapes.Model
{
    public class VseprConfiguration
    {
        public readonly int X; // number of radial atoms connected to the central atom
        public readonly int E; // number of radial lone pairs connected to the central atom

        public readonly MoleculeGeometry MoleculeGeometry;
        public readonly ElectronGeometry ElectronGeometry;

        public readonly List<float3> BondOrientations = new();
        public readonly List<float3> LonePairOrientations = new();
        public readonly IReadOnlyList<float3> AllOrientations;

        private VseprConfiguration(int x, int e)
        {
            X = x;
            E = e;

            MoleculeGeometry = MoleculeGeometry.GetConfiguration(x, e);
            ElectronGeometry = ElectronGeometry.GetConfiguration(x + e);

            var vectors = ElectronGeometry.UnitVectors;
            AllOrientations = vectors;

            // Fill lone-pair orientations first (highest-repulsion slots), then bonds.
            for (int i = 0; i < x + e; i++)
            {
                if (i < e) LonePairOrientations.Add(vectors[i]);
                else BondOrientations.Add(vectors[i]);
            }
        }

        // Cache keyed by (x, e); both are small (0..6).
        private static readonly Dictionary<int, VseprConfiguration> Cache = new();

        public static VseprConfiguration GetConfiguration(int x, int e)
        {
            int key = x * 100 + e;
            if (Cache.TryGetValue(key, out VseprConfiguration cached)) return cached;

            var configuration = new VseprConfiguration(x, e);
            Cache[key] = configuration;
            return configuration;
        }

        // Finds the ideal rotation matching all groups (bonds and lone pairs together).
        public AttractorModel.ResultMapping GetIdealGroupRotationToPositions(IReadOnlyList<PairGroup> groups)
        {
            UnityEngine.Debug.Assert(X + E == groups.Count);

            return AttractorModel.FindClosestMatchingConfiguration(
                AttractorModel.GetOrientationsFromOrigin(groups),
                ElectronGeometry.UnitVectors,
                LocalShape.VseprPermutations(groups));
        }

        // Finds the ideal rotation using only the bonded portions (ignores lone pairs).
        public AttractorModel.ResultMapping GetIdealBondRotationToPositions(IReadOnlyList<PairGroup> groups)
        {
            UnityEngine.Debug.Assert(X == groups.Count);

            return AttractorModel.FindClosestMatchingConfiguration(
                AttractorModel.GetOrientationsFromOrigin(groups),
                BondOrientations,
                Permutation.Permutations(BondOrientations.Count));
        }
    }
}

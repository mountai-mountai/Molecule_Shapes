// Port of ElectronGeometry.js - the "electron geometry": ideal unit-vector directions an ideal VSEPR
// configuration takes for a given steric number. Vectors are ordered so that higher-repulsion groups
// (lone pairs) occupy the earliest slots.
//
// Desktop-test version: hard-coded runtime tables (fastest to stand up). A ScriptableObject-authored
// variant is a later polish, per the porting guide.

using System.Collections.Generic;
using Unity.Mathematics;

namespace Molecule_Shapes.Model
{
    public enum ElectronGeometryKind
    {
        Empty,
        Diatomic,
        Linear,
        TrigonalPlanar,
        Tetrahedral,
        TrigonalBipyramidal,
        Octahedral
    }

    public class ElectronGeometry
    {
        public readonly ElectronGeometryKind Kind;
        public readonly string DisplayName;
        public readonly IReadOnlyList<float3> UnitVectors;

        // Steric number = how many radial groups (atoms + lone pairs) this geometry holds.
        public int StericNumber => UnitVectors.Count;

        private ElectronGeometry(ElectronGeometryKind kind, string displayName, float3[] unitVectors)
        {
            Kind = kind;
            DisplayName = displayName;
            UnitVectors = unitVectors;
        }

        // Tetrahedral elevation constant (matches ElectronGeometry.js TETRA_CONST).
        private static readonly float TetraConst = math.PI * -19.471220333f / 180f;

        public static readonly ElectronGeometry Empty = new(
            ElectronGeometryKind.Empty, "", new float3[0]);

        public static readonly ElectronGeometry Diatomic = new(
            ElectronGeometryKind.Diatomic, "Diatomic", new[]
            {
                new float3(1, 0, 0)
            });

        public static readonly ElectronGeometry Linear = new(
            ElectronGeometryKind.Linear, "Linear", new[]
            {
                new float3(1, 0, 0),
                new float3(-1, 0, 0)
            });

        public static readonly ElectronGeometry TrigonalPlanar = new(
            ElectronGeometryKind.TrigonalPlanar, "Trigonal Planar", new[]
            {
                new float3(1, 0, 0),
                new float3(math.cos(math.PI * 2f / 3f), math.sin(math.PI * 2f / 3f), 0),
                new float3(math.cos(math.PI * 4f / 3f), math.sin(math.PI * 4f / 3f), 0)
            });

        public static readonly ElectronGeometry Tetrahedral = new(
            ElectronGeometryKind.Tetrahedral, "Tetrahedral", new[]
            {
                new float3(0, 0, 1),
                new float3(math.cos(0f) * math.cos(TetraConst), math.sin(0f) * math.cos(TetraConst), math.sin(TetraConst)),
                new float3(math.cos(math.PI * 2f / 3f) * math.cos(TetraConst), math.sin(math.PI * 2f / 3f) * math.cos(TetraConst), math.sin(TetraConst)),
                new float3(math.cos(math.PI * 4f / 3f) * math.cos(TetraConst), math.sin(math.PI * 4f / 3f) * math.cos(TetraConst), math.sin(TetraConst))
            });

        public static readonly ElectronGeometry TrigonalBipyramidal = new(
            ElectronGeometryKind.TrigonalBipyramidal, "Trigonal Bipyramidal", new[]
            {
                // equatorial (fills up with lone pairs first)
                new float3(0, 1, 0),
                new float3(0, math.cos(math.PI * 2f / 3f), math.sin(math.PI * 2f / 3f)),
                new float3(0, math.cos(math.PI * 4f / 3f), math.sin(math.PI * 4f / 3f)),

                // axial
                new float3(1, 0, 0),
                new float3(-1, 0, 0)
            });

        public static readonly ElectronGeometry Octahedral = new(
            ElectronGeometryKind.Octahedral, "Octahedral", new[]
            {
                // opposites first
                new float3(0, 0, 1),
                new float3(0, 0, -1),
                new float3(0, 1, 0),
                new float3(0, -1, 0),
                new float3(1, 0, 0),
                new float3(-1, 0, 0)
            });

        // Indexed by steric number (number of radial groups). Declared after the fields above so
        // C#'s textual static-initialization order has already populated them.
        private static readonly ElectronGeometry[] ByNumberOfGroups =
        {
            Empty,               // 0
            Diatomic,            // 1
            Linear,              // 2
            TrigonalPlanar,      // 3
            Tetrahedral,         // 4
            TrigonalBipyramidal, // 5
            Octahedral           // 6
        };

        // numberOfGroups == steric number (radial atoms + radial lone pairs).
        public static ElectronGeometry GetConfiguration(int numberOfGroups)
        {
            UnityEngine.Debug.Assert(numberOfGroups >= 0 && numberOfGroups <= 6,
                $"ElectronGeometry only defined for 0..6 groups, got {numberOfGroups}");
            return ByNumberOfGroups[numberOfGroups];
        }
    }
}

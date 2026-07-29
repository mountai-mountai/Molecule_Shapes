// A target molecule, expressed by its VSEPR counts (X = radial atoms, E = radial lone pairs).
// Everything the game asks of the player reduces to "match this goal", and every goal derives its
// shape name and AXE formula from the same model lookup the live sim uses (MoleculeGeometry), so the
// game can never disagree with what the molecule actually forms.

using System.Collections.Generic;
using Molecule_Shapes.Model;

namespace Molecule_Shapes.Game
{
    public sealed class MoleculeGoal
    {
        public readonly int X;   // radial atoms
        public readonly int E;   // radial lone pairs

        public MoleculeGoal(int x, int e)
        {
            X = x;
            E = e;
        }

        public int StericNumber => X + E;

        // Throws if (X,E) is invalid - construct goals from ValidConfigurations so this never happens.
        public MoleculeGeometry Geometry => MoleculeGeometry.GetConfiguration(X, E);

        public string GeometryName => Geometry.DisplayName;

        // Electron geometry is fixed by the steric number (X+E): Linear/Trigonal Planar/Tetrahedral/
        // Trigonal Bipyramidal/Octahedral. Two molecules share an electron geometry iff same steric number.
        public string ElectronGeometryName => ElectronGeometry.GetConfiguration(StericNumber).DisplayName;

        // "AX4", "AX2E2", "AX3E", etc. Uses A for the central atom, X for bonded atoms, E for lone pairs.
        public string AxeFormula
        {
            get
            {
                string atoms = X > 0 ? "AX" + (X > 1 ? X.ToString() : "") : "A";
                string lone = E > 0 ? "E" + (E > 1 ? E.ToString() : "") : "";
                return atoms + lone;
            }
        }

        // Characteristic bond angle(s) for prompt display in BuildFromAngles / hints. Descriptive only.
        public string ApproxAngles => CharacteristicAngles(Geometry.Kind);

        public override string ToString() => $"{AxeFormula} ({GeometryName})";

        public override bool Equals(object obj) => obj is MoleculeGoal g && g.X == X && g.E == E;
        public override int GetHashCode() => (X * 31) ^ E;

        // --- Valid configuration table -------------------------------------------------------------
        // Every (X,E) the model accepts (mirrors MoleculeGeometry.GetConfiguration; X+E <= 6). Excludes
        // the trivial X=1 "Diatomic" case as it isn't a meaningful shape challenge.
        public static readonly IReadOnlyList<MoleculeGoal> ValidConfigurations = new List<MoleculeGoal>
        {
            new(2, 0), new(2, 1), new(2, 2), new(2, 3), new(2, 4), // Linear / Bent / Linear...
            new(3, 0), new(3, 1), new(3, 2), new(3, 3),            // Trig. Planar / Trig. Pyramidal / T-shaped
            new(4, 0), new(4, 1), new(4, 2),                       // Tetrahedral / Seesaw / Square Planar
            new(5, 0), new(5, 1),                                  // Trig. Bipyramidal / Square Pyramidal
            new(6, 0)                                              // Octahedral
        };

        // True when (x,e) names a valid molecular geometry. Used to grade GeometryName matches without
        // letting MoleculeGeometry.GetConfiguration throw on an in-progress, not-yet-valid molecule.
        public static bool TryGetGeometry(int x, int e, out MoleculeGeometry geometry)
        {
            foreach (MoleculeGoal g in ValidConfigurations)
            {
                if (g.X == x && g.E == e)
                {
                    geometry = g.Geometry;
                    return true;
                }
            }
            // X=0/1 are valid in the model but excluded above; resolve them directly for completeness.
            if (x is >= 0 and <= 1)
            {
                geometry = MoleculeGeometry.GetConfiguration(x, e == 0 ? 0 : e);
                return true;
            }
            geometry = null;
            return false;
        }

        // Describes the characteristic bond angle(s) - including counts and mixed angle types where
        // that's what makes the shape recognizable (e.g. "four 90° angles" for square planar, or
        // "90° and 120° angles" for trigonal bipyramidal). Descriptive only; used for prompts/hints.
        private static string CharacteristicAngles(MoleculeGeometryKind kind) => kind switch
        {
            MoleculeGeometryKind.Linear => "a single 180° angle",
            MoleculeGeometryKind.Bent => "one bent angle (≈104–118°)",
            MoleculeGeometryKind.TrigonalPlanar => "three 120° angles",
            MoleculeGeometryKind.TrigonalPyramidal => "three ≈107° angles",
            MoleculeGeometryKind.TShaped => "≈90° angles in a T",
            MoleculeGeometryKind.Tetrahedral => "109.5° angles",
            MoleculeGeometryKind.Seesaw => "both 90° and 120° angles",
            MoleculeGeometryKind.SquarePlanar => "four 90° angles",
            MoleculeGeometryKind.TrigonalBipyramidal => "90°, 120°, and 180° angles",
            MoleculeGeometryKind.SquarePyramidal => "90° angles in a square pyramid",
            MoleculeGeometryKind.Octahedral => "all 90° angles",
            _ => "—"
        };
    }
}

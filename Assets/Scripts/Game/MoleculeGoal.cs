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

        // Characteristic bond angle(s) for prompt display in BuildFromAngles / hints.
        //
        // Keyed to the ELECTRON geometry (steric number), because that's what sets the angles - and it's
        // what the model actually renders. Two consequences, both deliberate:
        //   * the numbers quoted always match what the sim displays (no "104-118 degrees" prompt against
        //     a molecule sitting at the ideal 109.5 - measured/real angles belong to Real Molecule mode);
        //   * the description is unique per steric number, so a prompt can't describe two different
        //     answers (e.g. seesaw and trigonal bipyramidal both show 90/120/180).
        public string ApproxAngles => IdealAngles(X, E);

        /// <summary>Number of bond angles the molecule actually shows: every pair of bonded atoms.</summary>
        public int BondAngleCount => X * (X - 1) / 2;

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

        // The ideal VSEPR bond angle(s) a molecule actually shows, quoted the way chemistry courses do:
        // the characteristic angles between BONDED atoms. Two rules keep these honest:
        //   * only angles this molecule really has - a 2-bond molecule shows one angle, so AX2E3 (linear)
        //     is "a 180° angle", never the parent geometry's full 90/120/180 set;
        //   * 180° is only listed where it's a real, distinct bond angle - octahedral and square planar
        //     are taught as 90°, so quoting "90° and 180°" over-attributes.
        // Plurality follows the actual count of bond angles.
        public static string IdealAngles(int x, int e)
        {
            int steric = x + e;
            if (x < 2) return "—";                     // no angle exists with fewer than two bonds
            bool one = x * (x - 1) / 2 == 1;           // exactly one bond angle

            // Two bonds: the angle is set by the parent electron geometry.
            if (x == 2)
            {
                string v = steric switch
                {
                    2 => "180",
                    3 => "120",
                    4 => "109.5",
                    _ => "180"                          // AX2E3 - both bonds axial
                };
                return $"a {v}° angle";
            }

            return steric switch
            {
                3 => Plural("120", one),                                  // trigonal planar
                4 => Plural("109.5", one),                                // tetrahedral / pyramidal
                5 => x == 3 ? "90° angles"                                // T-shaped
                            : "90° and 120° angles",                      // trig bipyramidal / seesaw
                6 => "90° angles",                                        // octahedral / square planar /
                _ => "—"                                                  // square pyramidal
            };
        }

        private static string Plural(string value, bool singular) =>
            singular ? $"a {value}° angle" : $"{value}° angles";
    }
}

// The real molecules students are asked to build, by FORMULA (no AXE notation - teachers asked for the
// formula so students derive the shape themselves). Pure C#, so it's testable alongside the rest of the
// game layer.
//
// Each entry is just the VSEPR counts around the central atom: X bonded atoms, E lone pairs. Bond order
// is deliberately not part of the answer - a double bond is still one electron domain, so CO2 (two
// double bonds) and, say, a two-single-bond molecule are the same VSEPR problem.
//
// Lists come from the teacher workshop:
//   Easy    CH4  H2O  BH3  NH3  CO2
//   Medium  SF6  AsF5 XeF4 PCl5
//   Hard    SO2  ClF3 SF4  BrF5
// Tiers are CUMULATIVE (Medium includes Easy, Hard includes both), matching how the shape pools work,
// so a Hard round covers everything. Flip Cumulative to false for strictly per-level lists.

using System.Collections.Generic;

namespace Molecule_Shapes.Game
{
    public sealed class RealMoleculeSpec
    {
        public readonly string Formula;    // "CH4" - shown in the prompt
        public readonly string Name;       // "Methane"
        public readonly int X;             // bonded atoms around the central atom
        public readonly int E;             // lone pairs on the central atom
        public readonly ChallengeDifficulty Tier;

        public RealMoleculeSpec(string formula, string name, int x, int e, ChallengeDifficulty tier)
        {
            Formula = formula; Name = name; X = x; E = e; Tier = tier;
        }

        public MoleculeGoal Goal => new MoleculeGoal(X, E);
        public string GeometryName => Goal.GeometryName;
        public override string ToString() => $"{Formula} ({Name})";

        /// <summary>When true a difficulty includes every easier tier's molecules as well.</summary>
        public const bool Cumulative = true;

        public static readonly IReadOnlyList<RealMoleculeSpec> All = new List<RealMoleculeSpec>
        {
            // Easy - TEKS on-level
            new("CH4", "Methane",           4, 0, ChallengeDifficulty.Easy),   // tetrahedral
            new("H2O", "Water",             2, 2, ChallengeDifficulty.Easy),   // bent
            new("BH3", "Borane",            3, 0, ChallengeDifficulty.Easy),   // trigonal planar
            new("NH3", "Ammonia",           3, 1, ChallengeDifficulty.Easy),   // trigonal pyramidal
            new("CO2", "Carbon dioxide",    2, 0, ChallengeDifficulty.Easy),   // linear (two double bonds)

            // Medium - advanced / AP
            new("SF6",  "Sulfur hexafluoride",   6, 0, ChallengeDifficulty.Medium), // octahedral
            new("AsF5", "Arsenic pentafluoride", 5, 0, ChallengeDifficulty.Medium), // trigonal bipyramidal
            new("XeF4", "Xenon tetrafluoride",   4, 2, ChallengeDifficulty.Medium), // square planar
            new("PCl5", "Phosphorus pentachloride", 5, 0, ChallengeDifficulty.Medium), // trigonal bipyramidal

            // Hard - octet-expanding / lone-pair-rich
            new("SO2",  "Sulfur dioxide",        2, 1, ChallengeDifficulty.Hard),   // bent
            new("ClF3", "Chlorine trifluoride",  3, 2, ChallengeDifficulty.Hard),   // T-shaped
            new("SF4",  "Sulfur tetrafluoride",  4, 1, ChallengeDifficulty.Hard),   // seesaw
            new("BrF5", "Bromine pentafluoride", 5, 1, ChallengeDifficulty.Hard)    // square pyramidal
        };

        /// <summary>The molecules eligible at a difficulty (cumulative by default).</summary>
        public static List<RealMoleculeSpec> ForDifficulty(ChallengeDifficulty difficulty)
        {
            int cap = Rank(difficulty);
            var result = new List<RealMoleculeSpec>();
            foreach (RealMoleculeSpec m in All)
            {
                int r = Rank(m.Tier);
                if (Cumulative ? r <= cap : r == cap) result.Add(m);
            }
            if (result.Count == 0) result.AddRange(All);   // never hand back an empty round
            return result;
        }

        private static int Rank(ChallengeDifficulty d) => d switch
        {
            ChallengeDifficulty.Easy => 0,
            ChallengeDifficulty.Medium => 1,
            _ => 2
        };
    }
}

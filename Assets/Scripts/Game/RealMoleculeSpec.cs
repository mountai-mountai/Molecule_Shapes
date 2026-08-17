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

        /// <summary>Measured bond angle in degrees - the primary bond-bond angle. Must describe the SAME
        /// angle(s) the model's ideal describes, so the comparison never over- or under-attributes.</summary>
        public readonly float RealAngle;

        /// <summary>Second distinct measured angle where a shape has two (seesaw's axial pair), else &lt;= 0.</summary>
        public readonly float RealSecondaryAngle;

        /// <summary>Where a student would actually meet this molecule.</summary>
        public readonly string FoundIn;

        // Ideal and measured angles are written out EXPLICITLY, and always describe the same angles in
        // the same order, so the comparison can't over- or under-attribute. Shapes with two distinct
        // angles name which is which (a square pyramid's apical-basal is not its basal-basal), and an
        // angle the ideal description doesn't mention never appears only on the measured side.
        public readonly string IdealAnglesText;
        public readonly string RealAnglesText;

        public RealMoleculeSpec(string formula, string name, int x, int e, ChallengeDifficulty tier,
                                float realAngle, string idealAngles, string realAngles, string foundIn,
                                float realSecondaryAngle = -1f)
        {
            Formula = formula; Name = name; X = x; E = e; Tier = tier;
            RealAngle = realAngle; RealSecondaryAngle = realSecondaryAngle;
            IdealAnglesText = idealAngles; RealAnglesText = realAngles; FoundIn = foundIn;
        }

        public string RealAngles => RealAnglesText;
        public string IdealAngles => IdealAnglesText;

        /// <summary>True when the measured geometry departs from the textbook ideal.</summary>
        public bool DiffersFromIdeal => IdealAnglesText != RealAnglesText;

        public MoleculeGoal Goal => new MoleculeGoal(X, E);
        public string GeometryName => Goal.GeometryName;

        /// <summary>Why the measured angle departs from the ideal - the pedagogical payoff of the
        /// Model/Real comparison (lone pairs repel more strongly than bonding pairs).</summary>
        public string WhyDiffers => E > 0
            ? "Lone pairs repel more strongly than bonding pairs, squeezing the bond angle below the ideal."
            : "No lone pairs, so the measured angle matches the ideal.";

        public override string ToString() => $"{Formula} ({Name})";

        /// <summary>When true a difficulty includes every easier tier's molecules as well.</summary>
        public const bool Cumulative = true;

        public static readonly IReadOnlyList<RealMoleculeSpec> All = new List<RealMoleculeSpec>
        {
            // Easy - TEKS on-level. Measured angles quote the SAME angle the ideal does, so molecules
            // whose real geometry matches the model (no lone pairs) show identical values on both views.
            new("CH4", "Methane", 4, 0, ChallengeDifficulty.Easy, 109.5f,
                idealAngles: "109.5°", realAngles: "109.5°",
                foundIn: "The main component of natural gas, and the gas released by wetlands and livestock."),
            new("H2O", "Water", 2, 2, ChallengeDifficulty.Easy, 104.5f,
                idealAngles: "109.5°", realAngles: "104.5°",
                foundIn: "Oceans, rain, and every living cell - the most familiar bent molecule there is."),
            new("BH3", "Borane", 3, 0, ChallengeDifficulty.Easy, 120f,
                idealAngles: "120°", realAngles: "120°",
                foundIn: "A laboratory reagent; too reactive to sit around, so it's usually handled as diborane."),
            new("NH3", "Ammonia", 3, 1, ChallengeDifficulty.Easy, 107f,
                idealAngles: "109.5°", realAngles: "107°",
                foundIn: "Fertilizer and household cleaners - the sharp smell in glass cleaner."),
            new("CO2", "Carbon dioxide", 2, 0, ChallengeDifficulty.Easy, 180f,
                idealAngles: "180°", realAngles: "180°",
                foundIn: "Exhaled breath, the fizz in soda, and the greenhouse gas driving climate change."),

            // Medium - advanced / AP
            new("SF6", "Sulfur hexafluoride", 6, 0, ChallengeDifficulty.Medium, 90f,
                idealAngles: "90°", realAngles: "90°",
                foundIn: "An insulating gas inside high-voltage electrical switchgear."),
            new("AsF5", "Arsenic pentafluoride", 5, 0, ChallengeDifficulty.Medium, 90f,
                idealAngles: "90° axial-equatorial, 120° equatorial",
                realAngles:  "90° axial-equatorial, 120° equatorial",
                foundIn: "A strong Lewis acid used as a fluorinating agent in the lab.",
                realSecondaryAngle: 120f),
            new("XeF4", "Xenon tetrafluoride", 4, 2, ChallengeDifficulty.Medium, 90f,
                idealAngles: "90°", realAngles: "90°",
                foundIn: "One of the first noble-gas compounds ever made (1962) - proof they do react."),
            new("PCl5", "Phosphorus pentachloride", 5, 0, ChallengeDifficulty.Medium, 90f,
                idealAngles: "90° axial-equatorial, 120° equatorial",
                realAngles:  "90° axial-equatorial, 120° equatorial",
                foundIn: "An industrial chlorinating agent used in making other chemicals.",
                realSecondaryAngle: 120f),

            // Hard - octet-expanding / lone-pair-rich
            new("SO2", "Sulfur dioxide", 2, 1, ChallengeDifficulty.Hard, 119.5f,
                idealAngles: "120°", realAngles: "119.5°",
                foundIn: "A volcanic gas and a preservative on dried fruit; a cause of acid rain."),
            new("ClF3", "Chlorine trifluoride", 3, 2, ChallengeDifficulty.Hard, 87.5f,
                idealAngles: "90° axial-equatorial", realAngles: "87.5° axial-equatorial",
                foundIn: "Ferociously reactive; used to clean semiconductor manufacturing equipment."),
            // The 173° axial-axial angle is deliberately left out of BOTH descriptions: the ideal text
            // doesn't quote 180°, so quoting 173° only on the measured side would invent a difference.
            new("SF4", "Sulfur tetrafluoride", 4, 1, ChallengeDifficulty.Hard, 101.6f,
                idealAngles: "90° axial-equatorial, 120° equatorial",
                realAngles:  "87.8° axial-equatorial, 101.6° equatorial",
                foundIn: "A fluorinating agent in chemical synthesis, including some pharmaceuticals.",
                realSecondaryAngle: 173f),
            new("BrF5", "Bromine pentafluoride", 5, 1, ChallengeDifficulty.Hard, 84.8f,
                idealAngles: "90° apical-basal, 90° basal-basal",
                realAngles:  "84.8° apical-basal, 89.5° basal-basal",
                foundIn: "A powerful oxidizer, studied as a rocket propellant oxidant.")
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

// Produces challenges for a given objective + difficulty. Seeded for reproducibility (daily challenge,
// deterministic tests). Pure C#.

using System;
using System.Collections.Generic;

namespace Molecule_Shapes.Game
{
    public sealed class ChallengeGenerator
    {
        private readonly Random _rng;
        private MoleculeGoal _lastGoal;   // avoid posing the same goal twice in a row

        public ChallengeGenerator(int seed) => _rng = new Random(seed);
        public ChallengeGenerator() : this(Environment.TickCount) { }

        public Challenge Next(LearningObjective objective, ChallengeDifficulty difficulty)
        {
            List<MoleculeGoal> pool = GoalsForDifficulty(difficulty);

            MoleculeGoal goal = PickDistinct(pool);
            _lastGoal = goal;

            // Identify objectives need wrong choices; pull a few other goals with distinct answer labels,
            // drawn from THIS difficulty's pool so the options never include shapes above the level.
            IReadOnlyList<MoleculeGoal> distractors = IsIdentify(objective)
                ? PickDistractorsFor(objective, goal, pool, count: 3)
                : null;

            Challenge challenge = Challenge.Create(objective, goal, distractors);
            return challenge.WithShuffledOptions(_rng);
        }

        // Builds a challenge for a SPECIFIC goal (used by finite rounds), with distractors constrained to
        // the difficulty pool. Distinct from Next(), which picks the goal randomly.
        public Challenge ForGoal(LearningObjective objective, MoleculeGoal goal, ChallengeDifficulty difficulty)
        {
            _lastGoal = goal;
            List<MoleculeGoal> pool = GoalsForDifficulty(difficulty);
            IReadOnlyList<MoleculeGoal> distractors = IsIdentify(objective)
                ? PickDistractorsFor(objective, goal, pool, count: 3)
                : null;
            return Challenge.Create(objective, goal, distractors).WithShuffledOptions(_rng);
        }

        private static bool IsIdentify(LearningObjective o) =>
            o is LearningObjective.IdentifyMolecularGeometry
              or LearningObjective.IdentifyElectronGeometry
              or LearningObjective.IdentifyAxe
              or LearningObjective.IdentifyBoth;

        // --- Difficulty pools (TEKS-aligned) --------------------------------------------------------

        // Explicit canonical config per shape so Easy stays the five on-level TEKS shapes (no exotic
        // high-lone-pair configs that merely share a shape name). Pools are CUMULATIVE: Medium = Easy +
        // its own shapes; Hard/Mixed = everything. Sizes (~5 / 7 / 11) also line up with the intended
        // per-level question counts.
        private static readonly MoleculeGoal[] EasyTier =   // bent, linear, trig planar, trig pyramidal, tetrahedral
        {
            new(2, 0),  // Linear (CO2)
            new(2, 2),  // Bent (H2O)
            new(3, 0),  // Trigonal Planar (BH3)
            new(3, 1),  // Trigonal Pyramidal (NH3)
            new(4, 0)   // Tetrahedral (CH4)
        };
        private static readonly MoleculeGoal[] MediumTier = // + trigonal bipyramidal, octahedral
        {
            new(5, 0),  // Trigonal Bipyramidal
            new(6, 0)   // Octahedral
        };
        private static readonly MoleculeGoal[] HardTier =   // + the octet-expanding / lone-pair-rich shapes
        {
            new(3, 2),  // T-shaped (ClF3)
            new(4, 1),  // Seesaw (SF4)
            new(4, 2),  // Square Planar (XeF4)
            new(5, 1)   // Square Pyramidal (BrF5)
        };

        public static List<MoleculeGoal> GoalsForDifficulty(ChallengeDifficulty difficulty)
        {
            var result = new List<MoleculeGoal>(EasyTier);
            if (difficulty != ChallengeDifficulty.Easy) result.AddRange(MediumTier);
            if (difficulty is ChallengeDifficulty.Hard or ChallengeDifficulty.Mixed) result.AddRange(HardTier);
            return result;
        }

        // --- Selection helpers ----------------------------------------------------------------------

        private MoleculeGoal PickDistinct(List<MoleculeGoal> pool)
        {
            if (pool.Count == 1) return pool[0];
            MoleculeGoal pick;
            do { pick = pool[_rng.Next(pool.Count)]; }
            while (pool.Count > 1 && pick.Equals(_lastGoal));
            return pick;
        }

        // Wrong choices with answer labels DISTINCT from the goal's and from each other, so the multiple
        // choice always has 4 different options. The "answer" differs per objective (shape name vs
        // electron-geometry name vs AXE), so we dedupe by the same key the challenge will label with.
        private List<MoleculeGoal> PickDistractorsFor(LearningObjective objective, MoleculeGoal goal,
                                                      List<MoleculeGoal> pool, int count)
        {
            Func<MoleculeGoal, string> key = AnswerKey(objective);

            var byKey = new List<MoleculeGoal>();
            var seen = new HashSet<string> { key(goal) };
            foreach (MoleculeGoal g in pool)
                if (seen.Add(key(g))) byKey.Add(g);   // one representative per distinct answer, within the level

            var chosen = new List<MoleculeGoal>();
            while (chosen.Count < count && byKey.Count > 0)
            {
                int i = _rng.Next(byKey.Count);
                chosen.Add(byKey[i]);
                byKey.RemoveAt(i);
            }
            return chosen;
        }

        private static Func<MoleculeGoal, string> AnswerKey(LearningObjective o) => o switch
        {
            LearningObjective.IdentifyElectronGeometry => g => g.ElectronGeometryName,
            LearningObjective.IdentifyAxe => g => g.AxeFormula,
            LearningObjective.IdentifyBoth => g => $"{g.GeometryName} ({g.AxeFormula})",
            _ => g => g.GeometryName   // IdentifyMolecularGeometry
        };
    }
}

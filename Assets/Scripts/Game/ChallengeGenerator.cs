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

            // Identify objectives need wrong choices; pull a few other goals with different answers.
            IReadOnlyList<MoleculeGoal> distractors = null;
            if (objective is LearningObjective.IdentifyName
                or LearningObjective.IdentifyAxe
                or LearningObjective.IdentifyBoth)
            {
                distractors = PickDistractors(goal, count: 3);
            }

            Challenge challenge = Challenge.Create(objective, goal, distractors);
            return challenge.WithShuffledOptions(_rng);
        }

        // --- Difficulty pools -----------------------------------------------------------------------

        // Difficulty pools are CUMULATIVE: a harder tier includes every goal from the easier tiers plus
        // its own, so raising difficulty only adds options (never shrinks the set to a back-and-forth
        // handful). Easy = tier 0 only; Medium = tiers 0-1; Hard = tiers 0-2 (i.e. everything); Mixed =
        // everything as well. This keeps every mode's option count healthy.
        public static List<MoleculeGoal> GoalsForDifficulty(ChallengeDifficulty difficulty)
        {
            int cap = DifficultyRank(difficulty);
            var result = new List<MoleculeGoal>();
            foreach (MoleculeGoal g in MoleculeGoal.ValidConfigurations)
                if (TierRank(g) <= cap) result.Add(g);

            // Safety: never hand back an empty pool.
            if (result.Count == 0)
                foreach (MoleculeGoal g in MoleculeGoal.ValidConfigurations) result.Add(g);
            return result;
        }

        // 0 = easiest tier, 2 = hardest. Mixed maps to the top so it includes everything.
        private static int DifficultyRank(ChallengeDifficulty d) => d switch
        {
            ChallengeDifficulty.Easy => 0,
            ChallengeDifficulty.Medium => 1,
            _ => 2   // Hard and Mixed both include all tiers
        };

        // Classifies a goal into a difficulty tier rank (0 easiest .. 2 hardest).
        private static int TierRank(MoleculeGoal g)
        {
            if (g.E == 0) return g.StericNumber <= 4 ? 0 : 1;   // no lone pairs
            if (g.E == 1) return 1;                             // one lone pair
            return 2;                                           // two or more lone pairs
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

        private List<MoleculeGoal> PickDistractors(MoleculeGoal goal, int count)
        {
            // Draw from the full valid set so wrong answers can come from any tier; require a distinct
            // (X,E) from the goal. The Challenge dedupes by answer label afterward.
            var candidates = new List<MoleculeGoal>();
            foreach (MoleculeGoal g in MoleculeGoal.ValidConfigurations)
                if (!g.Equals(goal)) candidates.Add(g);

            var chosen = new List<MoleculeGoal>();
            while (chosen.Count < count && candidates.Count > 0)
            {
                int i = _rng.Next(candidates.Count);
                chosen.Add(candidates[i]);
                candidates.RemoveAt(i);
            }
            return chosen;
        }
    }
}

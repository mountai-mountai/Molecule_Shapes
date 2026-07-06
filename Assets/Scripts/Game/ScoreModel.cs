// Running tally for a session: total score, current/best streak, and the most recent breakdown.
// Pure C#. Persistence (high scores) is intentionally out of scope here - a view layer can read
// Total/BestStreak and write them to PlayerPrefs.

namespace Molecule_Shapes.Game
{
    public sealed class ScoreModel
    {
        public int Total { get; private set; }
        public int Streak { get; private set; }
        public int BestStreak { get; private set; }
        public int Solved { get; private set; }
        public ScoreBreakdown LastBreakdown { get; private set; }

        // Records a solve. `streakLevel` for the breakdown is the streak BEFORE incrementing, so the
        // caller passes the current Streak; we then bump it.
        public ScoreBreakdown RegisterSolve(ScoreRules rules, float elapsedSeconds, int edits,
                                            int minimumEdits, int hintsUsed, float accuracy01)
        {
            ScoreBreakdown b = rules.Compute(elapsedSeconds, edits, minimumEdits, hintsUsed,
                                             accuracy01, Streak);
            int gained = b.Total < 0 ? 0 : b.Total;   // never go negative on a solve
            Total += gained;
            Solved++;
            Streak++;
            if (Streak > BestStreak) BestStreak = Streak;
            LastBreakdown = b;
            return b;
        }

        // A miss / timeout / skip breaks the streak but doesn't subtract score.
        public void BreakStreak() => Streak = 0;

        // Deducts points for a wrong answer, never letting the total drop below 0. Returns the amount
        // actually removed (may be less than requested if the total hit the floor).
        public int Penalize(int points)
        {
            int p = System.Math.Max(0, points);
            int removed = System.Math.Min(Total, p);
            Total -= removed;
            return removed;
        }

        public void Reset()
        {
            Total = 0;
            Streak = 0;
            BestStreak = 0;
            Solved = 0;
            LastBreakdown = default;
        }
    }
}

// Configurable scoring. Each contributing factor (speed, attempts, accuracy, hints) is independently
// toggleable, so you can dial a "basic mode" (just base points) up to a fully-weighted score, or let
// difficulty presets do it for you. Pure C# but [Serializable] so a MonoBehaviour can expose it in the
// Inspector for live tuning.

using System;

namespace Molecule_Shapes.Game
{
    [Serializable]
    public class ScoreRules
    {
        public int basePoints = 100;

        // Speed: award up to `timeBonusMax` points, decaying to 0 over `timeBonusWindowSeconds`.
        public bool useTimeBonus = false;
        public int timeBonusMax = 100;
        public float timeBonusWindowSeconds = 30f;

        // Attempts: subtract `attemptPenalty` for each edit beyond the theoretical minimum (X + E).
        public bool useAttemptPenalty = false;
        public int attemptPenalty = 10;

        // Accuracy: scale a bonus by how cleanly the molecule settled (0..1). Needs RequireSettled.
        public bool useAccuracyBonus = false;
        public int accuracyBonusMax = 50;

        // Hints: subtract `hintPenalty` per hint used.
        public bool useHintPenalty = false;
        public int hintPenalty = 25;

        // Wrong Identify answer: subtract this from the running total (floored at 0). 0 = off.
        public int wrongAnswerPenalty = 50;

        public int streakBonusPerLevel = 0;   // added per current streak level (0 = off)

        // The "start basic" preset: base points only.
        public static ScoreRules Basic() => new ScoreRules();

        // Difficulty-scaled presets that progressively enable more factors.
        public static ScoreRules ForDifficulty(ChallengeDifficulty difficulty)
        {
            switch (difficulty)
            {
                case ChallengeDifficulty.Easy:
                    return new ScoreRules { basePoints = 100, useTimeBonus = true, timeBonusMax = 50 };
                case ChallengeDifficulty.Medium:
                    return new ScoreRules
                    {
                        basePoints = 150, useTimeBonus = true, timeBonusMax = 100,
                        useAttemptPenalty = true, streakBonusPerLevel = 10
                    };
                default:   // Hard
                    return new ScoreRules
                    {
                        basePoints = 250, useTimeBonus = true, timeBonusMax = 150,
                        useAttemptPenalty = true, attemptPenalty = 15,
                        useAccuracyBonus = true, useHintPenalty = true, streakBonusPerLevel = 20
                    };
            }
        }

        // Computes the points for a single solve. Inputs the game already tracks; unused factors fall
        // out via their toggles. `accuracy01` in [0,1]; `streakLevel` is the streak BEFORE this solve.
        public ScoreBreakdown Compute(float elapsedSeconds, int edits, int minimumEdits,
                                      int hintsUsed, float accuracy01, int streakLevel)
        {
            var b = new ScoreBreakdown { basePoints = basePoints };

            if (useTimeBonus && timeBonusWindowSeconds > 0f)
            {
                float t = Clamp01(1f - elapsedSeconds / timeBonusWindowSeconds);
                b.timeBonus = (int)Math.Round(timeBonusMax * t);
            }

            if (useAttemptPenalty)
            {
                int extra = Math.Max(0, edits - Math.Max(1, minimumEdits));
                b.attemptPenalty = -extra * attemptPenalty;
            }

            if (useAccuracyBonus)
                b.accuracyBonus = (int)Math.Round(accuracyBonusMax * Clamp01(accuracy01));

            if (useHintPenalty)
                b.hintPenalty = -hintsUsed * hintPenalty;

            if (streakBonusPerLevel > 0)
                b.streakBonus = streakLevel * streakBonusPerLevel;

            return b;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    // Itemized result of one solve so the HUD can show "+100 base, +40 speed, -10 attempts".
    public struct ScoreBreakdown
    {
        public int basePoints;
        public int timeBonus;
        public int attemptPenalty;   // <= 0
        public int accuracyBonus;
        public int hintPenalty;      // <= 0
        public int streakBonus;

        public int Total => basePoints + timeBonus + attemptPenalty + accuracyBonus + hintPenalty + streakBonus;

        // Human-readable itemization, e.g. "Base 100   Time +40   Attempts −10   Hints −25".
        // Only nonzero factors are listed (base always shows). Uses a real minus sign for negatives.
        // `multiline` puts each factor on its own line for a taller feedback area.
        public string Describe(bool multiline = false)
        {
            var parts = new System.Collections.Generic.List<string> { $"Base {basePoints}" };
            if (timeBonus != 0) parts.Add($"Time {Signed(timeBonus)}");
            if (accuracyBonus != 0) parts.Add($"Accuracy {Signed(accuracyBonus)}");
            if (streakBonus != 0) parts.Add($"Streak {Signed(streakBonus)}");
            if (attemptPenalty != 0) parts.Add($"Attempts {Signed(attemptPenalty)}");
            if (hintPenalty != 0) parts.Add($"Hints {Signed(hintPenalty)}");
            string sep = multiline ? "\n" : "   ";
            return string.Join(sep, parts);
        }

        private static string Signed(int v) => v >= 0 ? $"+{v}" : $"−{-v}";  // U+2212 minus for negatives
    }
}

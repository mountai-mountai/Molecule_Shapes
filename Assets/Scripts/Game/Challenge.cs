// One posed challenge: a goal + how it's presented + how it's judged. Pure C#.
//
// Build challenges are judged by IsSatisfiedBy(molecule) each frame; Identify challenges are judged by
// CheckAnswer(optionIndex). A single class covers both so the session/HUD have one type to hold.

using System.Collections.Generic;
using Molecule_Shapes.Model;

namespace Molecule_Shapes.Game
{
    public sealed class Challenge
    {
        public readonly LearningObjective Objective;
        public readonly MoleculeGoal Goal;
        public readonly TaskMode Task;
        public readonly MatchMode Match;

        // Whether the molecule must also have settled (low attractor error) to count as solved.
        // Off for count-only objectives so a build "wins" the instant the right groups are present;
        // on for BuildFromAngles where the formed shape's accuracy is the point.
        public readonly bool RequireSettled;
        public readonly float SettleErrorThreshold;

        public readonly string Prompt;

        // Identify-only: the choices shown and the index of the correct one. Empty for Build tasks.
        public readonly IReadOnlyList<string> Options;
        public readonly int CorrectOptionIndex;

        // Minimum number of edit operations to build the goal from an empty central atom (X + E).
        // Used by attempt-penalty scoring.
        public int MinimumEdits => Goal.X + Goal.E;

        private Challenge(LearningObjective objective, MoleculeGoal goal, TaskMode task, MatchMode match,
                          bool requireSettled, float settleThreshold, string prompt,
                          IReadOnlyList<string> options, int correctOptionIndex)
        {
            Objective = objective;
            Goal = goal;
            Task = task;
            Match = match;
            RequireSettled = requireSettled;
            SettleErrorThreshold = settleThreshold;
            Prompt = prompt;
            Options = options ?? System.Array.Empty<string>();
            CorrectOptionIndex = correctOptionIndex;
        }

        // --- Build judging --------------------------------------------------------------------------

        // True when the live molecule currently satisfies this (Build) challenge.
        public bool IsSatisfiedBy(VsepRMolecule molecule)
        {
            if (Task != TaskMode.Build || molecule == null) return false;

            int x = molecule.RadialAtoms.Count;
            int e = molecule.RadialLonePairs.Count;

            bool countsOk;
            if (Match == MatchMode.ExactCounts)
            {
                countsOk = x == Goal.X && e == Goal.E;
            }
            else // GeometryName: any valid config whose shape name matches the goal's
            {
                countsOk = MoleculeGoal.TryGetGeometry(x, e, out MoleculeGeometry geo)
                           && geo.Kind == Goal.Geometry.Kind;
            }
            if (!countsOk) return false;

            if (RequireSettled && molecule.LastAttractorError > SettleErrorThreshold) return false;
            return true;
        }

        // --- Identify judging -----------------------------------------------------------------------

        public bool CheckAnswer(int optionIndex) =>
            Task == TaskMode.Identify && optionIndex == CorrectOptionIndex;

        // --- Factory --------------------------------------------------------------------------------
        // Builds a fully-formed challenge for an objective. `distractors` supplies the wrong choices for
        // Identify objectives (ignored for Build); they should be other valid goals.
        public static Challenge Create(LearningObjective objective, MoleculeGoal goal,
                                       IReadOnlyList<MoleculeGoal> distractors = null)
        {
            switch (objective)
            {
                case LearningObjective.BuildFromAxe:
                    return Build(objective, goal, MatchMode.ExactCounts, false,
                        $"Build {goal.AxeFormula}.");

                case LearningObjective.BuildFromName:
                    return Build(objective, goal, MatchMode.GeometryName, false,
                        $"Build a {goal.GeometryName} molecule.");

                case LearningObjective.BuildFromAngles:
                    // Shape accuracy matters here, so require the molecule to settle.
                    return Build(objective, goal, MatchMode.GeometryName, true,
                        $"Build a molecule with bond angles of {goal.ApproxAngles}.");

                case LearningObjective.BuildRealMolecule:
                    // STUB: real-molecule presets (elements + measured angles) land with the RealMolecule
                    // screen. For now this behaves like an exact-count VSEPR build of the same geometry.
                    return Build(objective, goal, MatchMode.ExactCounts, false,
                        $"Build the molecule with formula {goal.AxeFormula}.");

                case LearningObjective.IdentifyName:
                    return Identify(objective, goal, distractors, g => g.GeometryName,
                        "What is this molecule's geometry?");

                case LearningObjective.IdentifyAxe:
                    return Identify(objective, goal, distractors, g => g.AxeFormula,
                        "What is this molecule's AXE formula?");

                case LearningObjective.IdentifyBoth:
                    return Identify(objective, goal, distractors, g => $"{g.GeometryName} ({g.AxeFormula})",
                        "Identify this molecule:");

                default:
                    return Build(objective, goal, MatchMode.ExactCounts, false, $"Build {goal.AxeFormula}.");
            }
        }

        private static Challenge Build(LearningObjective objective, MoleculeGoal goal, MatchMode match,
                                       bool requireSettled, string prompt)
        {
            return new Challenge(objective, goal, TaskMode.Build, match, requireSettled,
                0.05f, prompt, null, -1);
        }

        private static Challenge Identify(LearningObjective objective, MoleculeGoal goal,
                                          IReadOnlyList<MoleculeGoal> distractors,
                                          System.Func<MoleculeGoal, string> label, string prompt)
        {
            var options = new List<string> { label(goal) };
            if (distractors != null)
            {
                foreach (MoleculeGoal d in distractors)
                {
                    string l = label(d);
                    if (!options.Contains(l)) options.Add(l);   // avoid duplicate labels (e.g. two Linear)
                }
            }
            // The session shuffles options; here option 0 is the correct one. The session records the
            // post-shuffle correct index, so we hand back a ready list with index 0 correct.
            return new Challenge(objective, goal, TaskMode.Identify, MatchMode.GeometryName, false, 0f,
                prompt, options, correctOptionIndex: 0);
        }

        // Returns a copy with options reordered and CorrectOptionIndex updated (Identify only).
        public Challenge WithShuffledOptions(System.Random rng)
        {
            if (Task != TaskMode.Identify || Options.Count <= 1) return this;

            string correct = Options[CorrectOptionIndex];
            var shuffled = new List<string>(Options);
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }
            return new Challenge(Objective, Goal, Task, Match, RequireSettled, SettleErrorThreshold,
                Prompt, shuffled, shuffled.IndexOf(correct));
        }
    }
}

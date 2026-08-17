using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using Molecule_Shapes.Model;
using Molecule_Shapes.Game;

namespace Molecule_Shapes.Tests
{
    // EditMode tests for the pure-C# gamification layer. No scene, no physics needed for the
    // count-based logic (RadialAtoms/RadialLonePairs update the instant groups are bonded).
    public class GameTests
    {
        // Builds a molecule with exactly x bonded atoms and e lone pairs around the central atom.
        private static VsepRMolecule BuildMolecule(int x, int e)
        {
            var molecule = new VsepRMolecule();
            var central = new PairGroup(float3.zero, isLonePair: false);
            molecule.AddCentralAtom(central);

            for (int i = 0; i < e; i++)
            {
                var dir = math.normalize(new float3(i + 1, i + 2, i + 3));
                var lp = new PairGroup(dir * PairGroup.LonePairDistance, isLonePair: true);
                molecule.AddGroupAndBond(lp, central, bondOrder: 0, bondLength: PairGroup.LonePairDistance);
            }
            for (int i = 0; i < x; i++)
            {
                var dir = math.normalize(new float3(-i - 1, i + 1, -i - 2));
                var atom = new PairGroup(dir * PairGroup.BondedPairDistance, isLonePair: false);
                molecule.AddGroupAndBond(atom, central, bondOrder: 1, bondLength: PairGroup.BondedPairDistance);
            }
            return molecule;
        }

        // --- Goal table -----------------------------------------------------------------------------

        [Test]
        public void AllValidConfigurations_ResolveToAGeometry()
        {
            foreach (MoleculeGoal g in MoleculeGoal.ValidConfigurations)
            {
                Assert.DoesNotThrow(() => { var _ = g.Geometry; }, $"{g} threw");
                Assert.IsNotEmpty(g.GeometryName);
                Assert.IsNotEmpty(g.AxeFormula);
            }
        }

        [Test]
        public void AxeFormula_FormatsCounts()
        {
            Assert.AreEqual("AX4", new MoleculeGoal(4, 0).AxeFormula);
            Assert.AreEqual("AX2E2", new MoleculeGoal(2, 2).AxeFormula);
            Assert.AreEqual("AX3E", new MoleculeGoal(3, 1).AxeFormula);
        }

        // --- Match granularity (the keystone) -------------------------------------------------------

        [Test]
        public void BuildFromAxe_RequiresExactCounts()
        {
            Challenge c = Challenge.Create(LearningObjective.BuildFromAxe, new MoleculeGoal(2, 2));

            Assert.IsTrue(c.IsSatisfiedBy(BuildMolecule(2, 2)), "exact AX2E2 should satisfy");
            Assert.IsFalse(c.IsSatisfiedBy(BuildMolecule(2, 1)), "AX2E should not satisfy AX2E2");
            Assert.IsFalse(c.IsSatisfiedBy(BuildMolecule(4, 0)), "AX4 should not satisfy AX2E2");
        }

        [Test]
        public void BuildMolecularGeometry_AcceptsAnyConfigurationWithThatShape()
        {
            // Linear is both AX2 (e=0) and AX2E3 (e=3) - a name match must accept either.
            Challenge c = Challenge.Create(LearningObjective.BuildMolecularGeometry, new MoleculeGoal(2, 0));

            Assert.IsTrue(c.IsSatisfiedBy(BuildMolecule(2, 0)), "AX2 is Linear");
            Assert.IsTrue(c.IsSatisfiedBy(BuildMolecule(2, 3)), "AX2E3 is also Linear");
            Assert.IsFalse(c.IsSatisfiedBy(BuildMolecule(2, 1)), "AX2E is Bent, not Linear");
            Assert.IsFalse(c.IsSatisfiedBy(BuildMolecule(3, 0)), "AX3 is Trigonal Planar, not Linear");
        }

        [Test]
        public void BuildElectronGeometry_MatchesByStericNumber()
        {
            // Tetrahedral electron geometry = steric 4: AX4, AX3E, AX2E2 all count; steric 3 does not.
            Challenge c = Challenge.Create(LearningObjective.BuildElectronGeometry, new MoleculeGoal(4, 0));

            Assert.IsTrue(c.IsSatisfiedBy(BuildMolecule(4, 0)), "AX4 is tetrahedral electron geometry");
            Assert.IsTrue(c.IsSatisfiedBy(BuildMolecule(3, 1)), "AX3E is also tetrahedral electron geometry");
            Assert.IsTrue(c.IsSatisfiedBy(BuildMolecule(2, 2)), "AX2E2 is also tetrahedral electron geometry");
            Assert.IsFalse(c.IsSatisfiedBy(BuildMolecule(3, 0)), "steric 3 is trigonal-planar electron geometry");
        }

        // --- Identify -------------------------------------------------------------------------------

        [Test]
        public void IdentifyMolecularGeometry_HasCorrectOptionAmongChoices()
        {
            var gen = new ChallengeGenerator(seed: 7);
            Challenge c = gen.Next(LearningObjective.IdentifyMolecularGeometry, ChallengeDifficulty.Hard);

            Assert.AreEqual(TaskMode.Identify, c.Task);
            Assert.GreaterOrEqual(c.Options.Count, 2);
            Assert.IsTrue(c.CheckAnswer(c.CorrectOptionIndex));
            Assert.AreEqual(c.Goal.GeometryName, c.Options[c.CorrectOptionIndex]);
        }

        [Test]
        public void IdentifyElectronGeometry_OptionsAreDistinctAndCorrect()
        {
            var gen = new ChallengeGenerator(seed: 11);
            Challenge c = gen.Next(LearningObjective.IdentifyElectronGeometry, ChallengeDifficulty.Hard);

            Assert.AreEqual(TaskMode.Identify, c.Task);
            Assert.IsTrue(c.CheckAnswer(c.CorrectOptionIndex));
            Assert.AreEqual(c.Goal.ElectronGeometryName, c.Options[c.CorrectOptionIndex]);
            // No duplicate labels among the choices.
            CollectionAssert.AllItemsAreUnique(c.Options);
        }

        // --- Generator ------------------------------------------------------------------------------

        [Test]
        public void Generator_IsDeterministicForAGivenSeed()
        {
            var a = new ChallengeGenerator(seed: 42);
            var b = new ChallengeGenerator(seed: 42);
            for (int i = 0; i < 20; i++)
            {
                Challenge ca = a.Next(LearningObjective.BuildFromAxe, ChallengeDifficulty.Hard);
                Challenge cb = b.Next(LearningObjective.BuildFromAxe, ChallengeDifficulty.Hard);
                Assert.AreEqual(ca.Goal, cb.Goal, $"seed-42 sequences diverged at {i}");
            }
        }

        [Test]
        public void EasyPool_IsExactlyTheFiveTeksShapes()
        {
            var expected = new HashSet<MoleculeGeometryKind>
            {
                MoleculeGeometryKind.Linear, MoleculeGeometryKind.Bent, MoleculeGeometryKind.TrigonalPlanar,
                MoleculeGeometryKind.TrigonalPyramidal, MoleculeGeometryKind.Tetrahedral
            };
            var actual = new HashSet<MoleculeGeometryKind>();
            foreach (MoleculeGoal g in ChallengeGenerator.GoalsForDifficulty(ChallengeDifficulty.Easy))
                actual.Add(g.Geometry.Kind);

            Assert.IsTrue(expected.SetEquals(actual), "Easy pool must be exactly the five TEKS shapes");
        }

        [Test]
        public void FiniteRound_CoversEachShapeOnceThenCompletes()
        {
            var session = new GameSession();
            bool completed = false;
            session.RoundCompleted += (_, __) => completed = true;
            session.StartChallengeRun(LearningObjective.BuildMolecularGeometry, ChallengeDifficulty.Easy,
                                      ScoreRules.Basic(), seed: 3);

            int length = session.RoundLength;
            Assert.AreEqual(5, length, "Easy round = the five TEKS shapes");

            var seen = new HashSet<MoleculeGeometryKind>();
            for (int q = 0; q < length; q++)
            {
                Challenge c = session.Current;
                Assert.IsNotNull(c, $"question {q} should be posed");
                seen.Add(c.Goal.Geometry.Kind);
                session.Tick(0.1f, BuildMolecule(c.Goal.X, c.Goal.E));   // build the goal -> solves
                Assert.IsTrue(session.CurrentSolved, $"building the goal should solve question {q}");
                session.NextChallenge();
            }

            Assert.IsTrue(completed, "round completes after the last question");
            Assert.AreEqual(5, session.CorrectThisRound);
            Assert.AreEqual(5, seen.Count, "each of the five shapes appears exactly once");
        }

        [Test]
        public void SkippingEveryQuestion_ReportsZeroCorrect()
        {
            // Pressing Next through a whole round must not look like a win: the round still completes,
            // but it reports 0 correct so the celebration listeners stay quiet.
            var session = new GameSession();
            int reportedCorrect = -1, reportedTotal = -1;
            session.RoundCompleted += (c, t) => { reportedCorrect = c; reportedTotal = t; };
            session.StartChallengeRun(LearningObjective.BuildMolecularGeometry, ChallengeDifficulty.Easy,
                                      ScoreRules.Basic(), seed: 5);

            for (int q = 0; q < session.RoundLength + 1; q++) session.NextChallenge();

            Assert.AreEqual(0, reportedCorrect, "nothing was solved, so nothing is correct");
            Assert.AreEqual(5, reportedTotal);
            Assert.AreEqual(0, session.Score.Total, "skipping should not score");
        }

        [Test]
        public void BuildFromAngles_AcceptsAnyShapeWithThoseAngles()
        {
            // Angles come from the ELECTRON geometry, so "90/120/180" (steric 5) is satisfied by
            // trigonal bipyramidal AND seesaw AND T-shaped - all genuinely show those angles.
            Challenge c = Challenge.Create(LearningObjective.BuildFromAngles, new MoleculeGoal(5, 0));

            Assert.IsTrue(c.IsSatisfiedBy(BuildMolecule(5, 0)), "trigonal bipyramidal");
            Assert.IsTrue(c.IsSatisfiedBy(BuildMolecule(4, 1)), "seesaw shares those angles");
            Assert.IsTrue(c.IsSatisfiedBy(BuildMolecule(3, 2)), "T-shaped shares those angles");
            Assert.IsFalse(c.IsSatisfiedBy(BuildMolecule(4, 0)), "steric 4 is 109.5, not 90/120/180");
        }

        [Test]
        public void AnglePrompts_AreUniquePerStericNumber()
        {
            // A prompt must never describe two different electron geometries.
            var byDescription = new Dictionary<string, int>();
            foreach (MoleculeGoal g in MoleculeGoal.ValidConfigurations)
            {
                if (byDescription.TryGetValue(g.ApproxAngles, out int steric))
                    Assert.AreEqual(steric, g.StericNumber, $"'{g.ApproxAngles}' describes two steric numbers");
                else
                    byDescription[g.ApproxAngles] = g.StericNumber;
            }
        }

        // --- Real molecules -------------------------------------------------------------------------

        [Test]
        public void RealMolecules_ResolveToTheExpectedShapes()
        {
            AssertShape("CH4", MoleculeGeometryKind.Tetrahedral);
            AssertShape("H2O", MoleculeGeometryKind.Bent);
            AssertShape("BH3", MoleculeGeometryKind.TrigonalPlanar);
            AssertShape("NH3", MoleculeGeometryKind.TrigonalPyramidal);
            AssertShape("CO2", MoleculeGeometryKind.Linear);
            AssertShape("SF6", MoleculeGeometryKind.Octahedral);
            AssertShape("XeF4", MoleculeGeometryKind.SquarePlanar);
            AssertShape("ClF3", MoleculeGeometryKind.TShaped);
            AssertShape("SF4", MoleculeGeometryKind.Seesaw);
            AssertShape("BrF5", MoleculeGeometryKind.SquarePyramidal);
        }

        private static void AssertShape(string formula, MoleculeGeometryKind expected)
        {
            RealMoleculeSpec spec = null;
            foreach (RealMoleculeSpec m in RealMoleculeSpec.All)
                if (m.Formula == formula) spec = m;

            Assert.IsNotNull(spec, $"{formula} missing from the real-molecule table");
            Assert.AreEqual(expected, spec.Goal.Geometry.Kind, $"{formula} resolved to the wrong shape");
        }

        [Test]
        public void RealGeometry_ReproducesTheMeasuredBondAngles()
        {
            // The whole point of Real mode: the vectors fed to the attractor must actually subtend the
            // measured angle, so water settles at 104.5 rather than the ideal 109.5.
            AssertBondAngle("H2O", 104.5f);
            AssertBondAngle("NH3", 107f);
            AssertBondAngle("SO2", 119f);
            AssertBondAngle("BrF5", 84.8f);
        }

        // Smallest angle between two BOND directions (the trailing X entries, lone pairs come first).
        private static void AssertBondAngle(string formula, float expectedDegrees)
        {
            RealMoleculeSpec spec = null;
            foreach (RealMoleculeSpec m in RealMoleculeSpec.All) if (m.Formula == formula) spec = m;
            Assert.IsNotNull(spec, $"{formula} missing");

            var vectors = RealGeometry.Build(spec.X, spec.E, spec.RealAngle, spec.RealSecondaryAngle);
            Assert.IsNotNull(vectors, $"{formula} should have a real construction");
            Assert.AreEqual(spec.X + spec.E, vectors.Count, $"{formula} vector count must match X+E");

            float smallest = 180f;
            for (int i = spec.E; i < vectors.Count; i++)
                for (int j = i + 1; j < vectors.Count; j++)
                {
                    float dot = math.clamp(math.dot(math.normalize(vectors[i]), math.normalize(vectors[j])), -1f, 1f);
                    smallest = math.min(smallest, math.degrees(math.acos(dot)));
                }

            Assert.AreEqual(expectedDegrees, smallest, 0.5f, $"{formula} bond angle");
        }

        [Test]
        public void RealAndIdealDescriptions_DescribeTheSameAngles()
        {
            // The two sides of the Model/Real comparison must name the same angles in the same order -
            // otherwise the panel invents a difference (SF4 quoting a 173° axial angle the ideal side
            // never mentions) or hides one (BrF5 collapsing apical-basal and basal-basal into one value).
            foreach (RealMoleculeSpec m in RealMoleculeSpec.All)
            {
                int idealParts = m.IdealAnglesText.Split(',').Length;
                int realParts = m.RealAnglesText.Split(',').Length;
                Assert.AreEqual(idealParts, realParts,
                    $"{m.Formula}: ideal '{m.IdealAnglesText}' and real '{m.RealAnglesText}' " +
                    "describe different numbers of angles");

                // Where an angle is labelled (axial-equatorial, apical-basal...), both sides use it.
                foreach (string label in new[] { "axial-equatorial", "equatorial", "apical-basal", "basal-basal" })
                    Assert.AreEqual(m.IdealAnglesText.Contains(label), m.RealAnglesText.Contains(label),
                        $"{m.Formula}: '{label}' appears on only one side of the comparison");
            }
        }

        [Test]
        public void IdealAngles_DontOverAttribute()
        {
            // Octahedral is taught as 90 degrees - quoting "90 and 180" over-attributes.
            Assert.AreEqual("90° angles", new MoleculeGoal(6, 0).ApproxAngles);   // SF6
            Assert.AreEqual("90° angles", new MoleculeGoal(4, 2).ApproxAngles);   // XeF4, square planar
            // A two-bond molecule shows exactly one angle, phrased in the singular.
            Assert.AreEqual("a 109.5° angle", new MoleculeGoal(2, 2).ApproxAngles);  // H2O
            Assert.AreEqual("a 180° angle", new MoleculeGoal(2, 0).ApproxAngles);    // CO2
            // Trigonal bipyramidal keeps both of its distinct angles.
            Assert.AreEqual("90° and 120° angles", new MoleculeGoal(5, 0).ApproxAngles);
        }

        [Test]
        public void RealMoleculeChallenge_PromptsWithFormulaAndNeedsExactCounts()
        {
            RealMoleculeSpec water = null;
            foreach (RealMoleculeSpec m in RealMoleculeSpec.All) if (m.Formula == "H2O") water = m;
            Challenge c = Challenge.CreateReal(water);

            StringAssert.Contains("H2O", c.Prompt);
            StringAssert.DoesNotContain("AX", c.Prompt);                  // formula only, never AXE
            Assert.IsTrue(c.IsSatisfiedBy(BuildMolecule(2, 2)), "H2O is 2 bonded + 2 lone pairs");
            Assert.IsFalse(c.IsSatisfiedBy(BuildMolecule(2, 1)), "SO2's counts must not satisfy H2O");
        }

        [Test]
        public void DifficultyPools_AreCumulative()
        {
            List<MoleculeGoal> easy = ChallengeGenerator.GoalsForDifficulty(ChallengeDifficulty.Easy);
            List<MoleculeGoal> medium = ChallengeGenerator.GoalsForDifficulty(ChallengeDifficulty.Medium);
            List<MoleculeGoal> hard = ChallengeGenerator.GoalsForDifficulty(ChallengeDifficulty.Hard);

            foreach (MoleculeGoal g in easy) Assert.Contains(g, medium, "Medium must include every Easy goal");
            foreach (MoleculeGoal g in medium) Assert.Contains(g, hard, "Hard must include every Medium goal");
            Assert.Greater(hard.Count, medium.Count);
            Assert.Greater(medium.Count, easy.Count);
        }

        // --- Scoring --------------------------------------------------------------------------------

        [Test]
        public void BasicRules_AwardOnlyBasePoints()
        {
            ScoreRules rules = ScoreRules.Basic();
            ScoreBreakdown b = rules.Compute(elapsedSeconds: 5f, edits: 10, minimumEdits: 2,
                                             hintsUsed: 3, accuracy01: 0.5f, streakLevel: 4);
            Assert.AreEqual(rules.basePoints, b.Total, "basic rules should ignore all bonuses/penalties");
        }

        [Test]
        public void TimeBonus_DecaysToZeroOverWindow()
        {
            var rules = new ScoreRules { basePoints = 0, useTimeBonus = true, timeBonusMax = 100, timeBonusWindowSeconds = 10f };
            Assert.AreEqual(100, rules.Compute(0f, 1, 1, 0, 1f, 0).Total, "instant solve -> full time bonus");
            Assert.AreEqual(0, rules.Compute(10f, 1, 1, 0, 1f, 0).Total, "window elapsed -> no time bonus");
        }

        [Test]
        public void AttemptPenalty_ChargesPerExtraEdit()
        {
            var rules = new ScoreRules { basePoints = 100, useAttemptPenalty = true, attemptPenalty = 10 };
            // goal needs 2 edits; player made 5 -> 3 extra -> -30.
            Assert.AreEqual(70, rules.Compute(0f, edits: 5, minimumEdits: 2, hintsUsed: 0, accuracy01: 1f, streakLevel: 0).Total);
        }

        [Test]
        public void ScoreModel_TracksStreakAndResets()
        {
            var score = new ScoreModel();
            ScoreRules rules = ScoreRules.Basic();
            score.RegisterSolve(rules, 1f, 2, 2, 0, 1f);
            score.RegisterSolve(rules, 1f, 2, 2, 0, 1f);
            Assert.AreEqual(2, score.Streak);
            Assert.AreEqual(2, score.BestStreak);
            score.BreakStreak();
            Assert.AreEqual(0, score.Streak);
            Assert.AreEqual(2, score.BestStreak, "best streak should persist after a break");
        }

        // --- Timer ----------------------------------------------------------------------------------

        [Test]
        public void CountdownTimer_ExpiresAfterDuration()
        {
            var t = new GameTimer();
            t.StartCountDown(2f);
            t.Tick(1f);
            Assert.IsFalse(t.Expired);
            Assert.AreEqual(1f, t.Remaining, 1e-4f);
            t.Tick(1.5f);
            Assert.IsTrue(t.Expired);
            Assert.AreEqual(0f, t.Remaining, 1e-4f);
        }

        // --- Full session flow ----------------------------------------------------------------------

        [Test]
        public void Session_BuildChallenge_SolvesAndScoresWhenMoleculeMatches()
        {
            var session = new GameSession();
            session.StartChallengeRun(LearningObjective.BuildFromAxe, ChallengeDifficulty.Easy,
                                      ScoreRules.Basic(), seed: 99);

            Challenge posed = session.Current;
            Assert.IsNotNull(posed, "a challenge should be posed on start");

            // Build a molecule matching the posed goal, then tick once.
            VsepRMolecule molecule = BuildMolecule(posed.Goal.X, posed.Goal.E);
            session.Tick(0.02f, molecule);

            Assert.IsTrue(session.CurrentSolved, "matching molecule should solve the challenge");
            Assert.AreEqual(1, session.Score.Solved);
            Assert.Greater(session.Score.Total, 0);
        }

        [Test]
        public void Session_BuildChallenge_DoesNotSolveForWrongMolecule()
        {
            var session = new GameSession();
            session.StartChallengeRun(LearningObjective.BuildFromAxe, ChallengeDifficulty.Easy,
                                      ScoreRules.Basic(), seed: 99);

            Challenge posed = session.Current;
            // Build a deliberately wrong configuration (one extra atom, clamped to valid range).
            int wrongX = posed.Goal.X >= 6 ? posed.Goal.X - 1 : posed.Goal.X + 1;
            VsepRMolecule molecule = BuildMolecule(wrongX, posed.Goal.E);
            session.Tick(0.02f, molecule);

            Assert.IsFalse(session.CurrentSolved);
            Assert.AreEqual(0, session.Score.Solved);
        }
    }
}

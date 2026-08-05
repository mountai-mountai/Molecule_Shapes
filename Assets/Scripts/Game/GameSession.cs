// The session state machine. Owns the current mode/objective/difficulty, the active challenge, the
// score, the timer, and per-challenge counters (edits, hints). Pure C# - a thin MonoBehaviour
// (GameSessionController) drives it with Time.deltaTime and a reference to the live molecule, and the
// HUD subscribes to its events. No Unity dependency here keeps the whole flow EditMode-testable.

using System;
using System.Collections.Generic;
using Molecule_Shapes.Model;

namespace Molecule_Shapes.Game
{
    public sealed class GameSession
    {
        public GameMode Mode { get; private set; } = GameMode.Sandbox;
        public LearningObjective Objective { get; private set; } = LearningObjective.BuildFromAxe;
        public ChallengeDifficulty Difficulty { get; private set; } = ChallengeDifficulty.Easy;

        public ScoreRules Rules { get; set; } = ScoreRules.Basic();
        public ScoreModel Score { get; } = new ScoreModel();
        public GameTimer Timer { get; } = new GameTimer();

        public Challenge Current { get; private set; }
        public bool CurrentSolved { get; private set; }
        public int EditsThisChallenge { get; private set; }
        public int HintsThisChallenge { get; private set; }

        private ChallengeGenerator _generator;
        private int _seed;
        private bool _useCountdown;
        private float _countdownSeconds;

        // --- Round state (a finite quiz covering each item in the level once) ------------------------
        // Shape objectives run off _playlist; Build Real Molecule runs off _realPlaylist (formulas).
        private readonly List<MoleculeGoal> _playlist = new();
        private readonly List<RealMoleculeSpec> _realPlaylist = new();
        private bool _realRound;
        private int _posed;                                     // challenges posed so far this round
        public bool RoundActive { get; private set; }
        public int RoundLength => _realRound ? _realPlaylist.Count : _playlist.Count;
        public int RoundPosed => _posed;                       // current question number (1..RoundLength)
        public int CorrectThisRound { get; private set; }

        // --- Events (the HUD listens to these) ------------------------------------------------------
        public event Action<Challenge> ChallengeStarted;       // a fresh challenge was posed
        public event Action<Challenge, ScoreBreakdown> ChallengeSolved;
        public event Action<Challenge, bool> AnswerJudged;     // Identify: (challenge, wasCorrect)
        public event Action<Challenge> ChallengeTimedOut;
        // (correct, total) for the finished round, so listeners can tell a clean run from a skipped one -
        // pressing Next through every question must not trigger a celebration.
        public event Action<int, int> RoundCompleted;
        public event Action StateChanged;                      // mode/objective/difficulty changed

        // --- Configuration --------------------------------------------------------------------------

        public void ConfigureTimer(bool useCountdown, float countdownSeconds)
        {
            _useCountdown = useCountdown;
            _countdownSeconds = countdownSeconds;
        }

        // Enters Sandbox - free play, no scored challenge.
        public void EnterSandbox()
        {
            Mode = GameMode.Sandbox;
            Current = null;
            RoundActive = false;
            _playlist.Clear();
            _realPlaylist.Clear();
            _realRound = false;
            _posed = 0;
            Timer.Reset();
            StateChanged?.Invoke();
        }

        // Starts a scored Challenge run: a finite round whose questions are the level's shapes in random
        // order (each once), so students see every shape. `rules == null` uses the difficulty preset.
        public void StartChallengeRun(LearningObjective objective, ChallengeDifficulty difficulty,
                                      ScoreRules rules = null, int? seed = null)
        {
            Mode = GameMode.Challenge;
            Objective = objective;
            Difficulty = difficulty;
            Rules = rules ?? ScoreRules.ForDifficulty(difficulty);
            _seed = seed ?? Environment.TickCount;
            _generator = new ChallengeGenerator(_seed);
            Score.Reset();

            BuildPlaylist(difficulty);
            RoundActive = true;
            CorrectThisRound = 0;
            _posed = 0;

            StateChanged?.Invoke();
            NextChallenge();
        }

        // Shuffles the level's items into the round playlist (Fisher-Yates, seeded for reproducibility).
        // Build Real Molecule draws from the real-molecule formulas; every other objective from shapes.
        private void BuildPlaylist(ChallengeDifficulty difficulty)
        {
            _realRound = Objective == LearningObjective.BuildRealMolecule;
            var rng = new Random(_seed);

            _playlist.Clear();
            _realPlaylist.Clear();

            if (_realRound)
            {
                _realPlaylist.AddRange(RealMoleculeSpec.ForDifficulty(difficulty));
                Shuffle(_realPlaylist, rng);
            }
            else
            {
                _playlist.AddRange(ChallengeGenerator.GoalsForDifficulty(difficulty));
                Shuffle(_playlist, rng);
            }
        }

        private static void Shuffle<T>(List<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // --- Challenge flow -------------------------------------------------------------------------

        public void NextChallenge()
        {
            if (Mode != GameMode.Challenge || !RoundActive) return;   // round finished -> Start a new one
            if (_generator == null) _generator = new ChallengeGenerator(_seed);

            // End of a finite round: report and stop (the HUD shows the summary / plays the celebration).
            if (_posed >= RoundLength)
            {
                int total = RoundLength;
                RoundActive = false;
                Current = null;
                Timer.Pause();
                RoundCompleted?.Invoke(CorrectThisRound, total);
                return;
            }

            Current = _realRound
                ? Challenge.CreateReal(_realPlaylist[_posed])                          // "Build CH4"
                : _generator.ForGoal(Objective, _playlist[_posed], Difficulty);        // next shape
            _posed++;

            CurrentSolved = false;
            EditsThisChallenge = 0;
            HintsThisChallenge = 0;

            if (_useCountdown) Timer.StartCountDown(_countdownSeconds);
            else Timer.StartCountUp();

            ChallengeStarted?.Invoke(Current);
        }

        // Called by the view whenever the player adds/removes a radial group (Build tasks only).
        public void NotifyEdit()
        {
            if (Mode == GameMode.Challenge && Current is { Task: TaskMode.Build } && !CurrentSolved)
                EditsThisChallenge++;
        }

        // Returns a short hint string and records that a hint was used (penalty applies if enabled).
        public string UseHint()
        {
            if (Current == null) return string.Empty;
            HintsThisChallenge++;
            return Current.Objective switch
            {
                LearningObjective.BuildMolecularGeometry => $"Hint: that shape is {Current.Goal.AxeFormula}.",
                LearningObjective.BuildElectronGeometry => $"Hint: {Current.Goal.ElectronGeometryName} electron geometry means {Current.Goal.StericNumber} electron domains.",
                LearningObjective.BuildFromAxe => $"Hint: {Current.Goal.AxeFormula} is {Current.Goal.GeometryName}.",
                LearningObjective.BuildFromAngles => $"Hint: those angles mean {Current.Goal.StericNumber} electron domains ({Current.Goal.ElectronGeometryName}).",
                LearningObjective.BuildRealMolecule => $"Hint: {Current.Goal.X} bonded atom(s) and {Current.Goal.E} lone pair(s) → {Current.Goal.GeometryName}.",
                _ => $"Hint: it has {Current.Goal.X} bonded atom(s) and {Current.Goal.E} lone pair(s)."
            };
        }

        // Advances the clock and, for Build tasks, checks whether the molecule now satisfies the goal.
        public void Tick(float dt, VsepRMolecule molecule)
        {
            if (Mode != GameMode.Challenge || Current == null) return;

            Timer.Tick(dt);

            if (!CurrentSolved && Current.Task == TaskMode.Build && Current.IsSatisfiedBy(molecule))
            {
                RegisterSolve(Accuracy01(molecule));
                return;
            }

            if (!CurrentSolved && Timer.Expired)
            {
                CurrentSolved = true;          // lock it; player advances with NextChallenge
                Score.BreakStreak();
                ChallengeTimedOut?.Invoke(Current);
                CompleteRoundIfFinished();
            }
        }

        // After the LAST question of a round resolves, finish the round automatically - the player
        // shouldn't have to press Next just to see the summary.
        private void CompleteRoundIfFinished()
        {
            if (!RoundActive || _posed < RoundLength) return;
            int total = RoundLength;
            RoundActive = false;
            Current = null;
            Timer.Pause();
            RoundCompleted?.Invoke(CorrectThisRound, total);
        }

        // Identify answer submission.
        public void SubmitAnswer(int optionIndex)
        {
            if (Mode != GameMode.Challenge || Current is not { Task: TaskMode.Identify } || CurrentSolved)
                return;

            bool correct = Current.CheckAnswer(optionIndex);

            if (correct)
            {
                AnswerJudged?.Invoke(Current, true);
                RegisterSolve(accuracy01: 1f);
            }
            else
            {
                if (Rules.wrongAnswerPenalty > 0) Score.Penalize(Rules.wrongAnswerPenalty);
                Score.BreakStreak();
                AnswerJudged?.Invoke(Current, false);   // fired after the penalty so the HUD reads the new total
            }
        }

        private void RegisterSolve(float accuracy01)
        {
            CurrentSolved = true;
            CorrectThisRound++;
            ScoreBreakdown b = Score.RegisterSolve(Rules, Timer.Elapsed, EditsThisChallenge,
                Current.MinimumEdits, HintsThisChallenge, accuracy01);
            Timer.Pause();
            ChallengeSolved?.Invoke(Current, b);
            CompleteRoundIfFinished();          // last question solved -> round ends on its own
        }

        // Maps the molecule's settling error to a 0..1 accuracy (1 = perfectly settled). Tunable.
        private static float Accuracy01(VsepRMolecule molecule) => 1f / (1f + molecule.LastAttractorError);
    }
}

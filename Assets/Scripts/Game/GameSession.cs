// The session state machine. Owns the current mode/objective/difficulty, the active challenge, the
// score, the timer, and per-challenge counters (edits, hints). Pure C# - a thin MonoBehaviour
// (GameSessionController) drives it with Time.deltaTime and a reference to the live molecule, and the
// HUD subscribes to its events. No Unity dependency here keeps the whole flow EditMode-testable.

using System;
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

        // --- Events (the HUD listens to these) ------------------------------------------------------
        public event Action<Challenge> ChallengeStarted;       // a fresh challenge was posed
        public event Action<Challenge, ScoreBreakdown> ChallengeSolved;
        public event Action<Challenge, bool> AnswerJudged;     // Identify: (challenge, wasCorrect)
        public event Action<Challenge> ChallengeTimedOut;
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
            Timer.Reset();
            StateChanged?.Invoke();
        }

        // Starts a scored Challenge run. `rules == null` uses the difficulty preset.
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
            StateChanged?.Invoke();
            NextChallenge();
        }

        // --- Challenge flow -------------------------------------------------------------------------

        public void NextChallenge()
        {
            if (Mode != GameMode.Challenge) return;
            if (_generator == null) _generator = new ChallengeGenerator(_seed);

            Current = _generator.Next(Objective, Difficulty);
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
                LearningObjective.BuildFromAngles => $"Hint: angles {Current.Goal.ApproxAngles} → {Current.Goal.GeometryName}.",
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
            }
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
            ScoreBreakdown b = Score.RegisterSolve(Rules, Timer.Elapsed, EditsThisChallenge,
                Current.MinimumEdits, HintsThisChallenge, accuracy01);
            Timer.Pause();
            ChallengeSolved?.Invoke(Current, b);
        }

        // Maps the molecule's settling error to a 0..1 accuracy (1 = perfectly settled). Tunable.
        private static float Accuracy01(VsepRMolecule molecule) => 1f / (1f + molecule.LastAttractorError);
    }
}

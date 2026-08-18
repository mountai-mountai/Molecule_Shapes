// The MonoBehaviour bridge between the pure-C# GameSession and the scene. It:
//   - owns a GameSession and ticks it every frame with the live molecule,
//   - sets up the molecule when a challenge starts (clear it for Build, show it for Identify),
//   - counts the player's radial edits as "attempts" by listening to the molecule's events.
// Platform-agnostic: it works in both the desktop and VR scenes. HUDs (IMGUI on desktop, world-space
// uGUI on VR) talk to it through the public API and read GameSessionController.Session for state.

using UnityEngine;
using Molecule_Shapes.Model;
using Molecule_Shapes.Game;

namespace Molecule_Shapes.View
{
    [RequireComponent(typeof(MoleculeController))]
    public class GameSessionController : MonoBehaviour
    {
        [Header("Start state")]
        [Tooltip("If on, begin a scored Challenge run on start; otherwise begin in Sandbox (free play).")]
        [SerializeField] private bool autoStartChallenge = false;
        [SerializeField] private LearningObjective startObjective = LearningObjective.BuildFromAxe;
        [SerializeField] private ChallengeDifficulty startDifficulty = ChallengeDifficulty.Easy;

        [Header("Determinism")]
        [Tooltip("Use a fixed seed for reproducible challenge sequences (e.g. a daily challenge).")]
        [SerializeField] private bool useFixedSeed = false;
        [SerializeField] private int seed = 12345;

        [Header("Timer")]
        [SerializeField] private bool useCountdown = false;
        [SerializeField] private float countdownSeconds = 30f;

        [Header("Scoring")]
        [Tooltip("Use the difficulty's scoring preset instead of the custom rules below.")]
        [SerializeField] private bool useDifficultyPresetScoring = true;
        [SerializeField] private ScoreRules customScoreRules = ScoreRules.Basic();

        [Header("Identify mode")]
        [Tooltip("In Identify challenges, if the player alters the rendered molecule, revert it back to the " +
                 "question's molecule after a short pause - so the thing they're identifying can't drift.")]
        [SerializeField] private bool autoRevertDuringIdentify = true;
        [Tooltip("Seconds of no edits before the molecule snaps back to the question's configuration.")]
        [SerializeField] private float revertDelay = 1.5f;

        public GameSession Session { get; private set; }

        private MoleculeController _controller;
        private VsepRMolecule _molecule;
        private bool _suppressEditCount;   // true while we change the molecule programmatically
        private bool _revertPending;
        private float _revertTimer;

        private void Awake()
        {
            _controller = GetComponent<MoleculeController>();
            Session = new GameSession();
            Session.ConfigureTimer(useCountdown, countdownSeconds);
            Session.ChallengeStarted += OnChallengeStarted;
        }

        private void Start()
        {
            // MoleculeController.Awake has already created the molecule and added the central atom.
            _molecule = _controller.Molecule;
            _molecule.GroupAdded += OnRadialEdit;
            _molecule.GroupRemoved += OnRadialEdit;

            if (autoStartChallenge) StartChallenge(startObjective, startDifficulty);
            else Session.EnterSandbox();
        }

        private void OnDestroy()
        {
            if (_molecule != null)
            {
                _molecule.GroupAdded -= OnRadialEdit;
                _molecule.GroupRemoved -= OnRadialEdit;
            }
            if (Session != null) Session.ChallengeStarted -= OnChallengeStarted;
        }

        private void Update()
        {
            if (Session.Mode == GameMode.Challenge)
                Session.Tick(Time.deltaTime, _molecule);

            TickAutoRevert(Time.deltaTime);
        }

        // In Identify challenges, snap the molecule back to the question's configuration once the player
        // stops editing (debounced by revertDelay), so they can't lose track of what they're identifying.
        private void TickAutoRevert(float dt)
        {
            if (!_revertPending) return;
            if (!autoRevertDuringIdentify || !IsIdentifyActive()) { _revertPending = false; return; }

            _revertTimer -= dt;
            if (_revertTimer > 0f) return;

            _revertPending = false;
            if (DiffersFromGoal())
            {
                _suppressEditCount = true;
                _controller.SetConfiguration(Session.Current.Goal.X, Session.Current.Goal.E);
                _suppressEditCount = false;
            }
        }

        private bool IsIdentifyActive() =>
            Session.Mode == GameMode.Challenge && Session.Current is { Task: TaskMode.Identify } && !Session.CurrentSolved;

        private bool DiffersFromGoal()
        {
            if (Session.Current == null) return false;
            return _molecule.RadialAtoms.Count != Session.Current.Goal.X
                   || _molecule.RadialLonePairs.Count != Session.Current.Goal.E;
        }

        // --- Public API for HUDs --------------------------------------------------------------------

        public void StartChallenge(LearningObjective objective, ChallengeDifficulty difficulty)
        {
            ScoreRules rules = useDifficultyPresetScoring ? null : customScoreRules;
            int? s = useFixedSeed ? seed : (int?)null;
            Session.StartChallengeRun(objective, difficulty, rules, s);
        }

        public void EnterSandbox() => Session.EnterSandbox();
        public void NextChallenge() => Session.NextChallenge();
        public string UseHint() => Session.UseHint();
        public void SubmitAnswer(int optionIndex) => Session.SubmitAnswer(optionIndex);

        // --- Internal -------------------------------------------------------------------------------

        private void OnChallengeStarted(Challenge challenge)
        {
            _revertPending = false;

            // A challenge is always posed against the IDEAL model. If the player had loaded a real
            // molecule, its measured orientations and bond length would otherwise carry into the game
            // and skew every angle (a tetrahedral build inheriting ammonia's 107°), so clear them.
            _molecule.ClearRealOrientations();
            _molecule.BondLengthOverride = null;

            // Programmatic molecule changes here must not be counted as player attempts.
            _suppressEditCount = true;
            if (challenge.Task == TaskMode.Build)
                _controller.ResetMolecule();                       // player builds from scratch
            else // Identify: present the target molecule for the player to name
                _controller.SetConfiguration(challenge.Goal.X, challenge.Goal.E);
            _suppressEditCount = false;
        }

        private void OnRadialEdit(PairGroup group)
        {
            if (_suppressEditCount || group.IsCentralAtom) return;
            Session.NotifyEdit();

            // Identify challenges: schedule a revert so the rendered molecule can't be permanently changed.
            if (autoRevertDuringIdentify && IsIdentifyActive() && DiffersFromGoal())
            {
                _revertPending = true;
                _revertTimer = revertDelay;
            }
        }
    }
}

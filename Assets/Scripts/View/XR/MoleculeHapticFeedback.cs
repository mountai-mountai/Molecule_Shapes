// Controller haptics for the VR scene. Two independent effects:
//
//  1. Solve pulse - a satisfying buzz on both controllers when a challenge is solved (and an optional
//     softer buzz on a wrong Identify answer or a timeout).
//
//  2. Energy haptic - while you deform the molecule, the controllers buzz with an intensity that scales
//     to how much "potential energy" is stored in the system. As you drag an atom away from its stable
//     VSEPR position (or cram atoms together), the strain rises and so does the buzz; let go and it
//     eases off as the molecule springs back to equilibrium.
//
// How haptics are "customized": an impulse is just an AMPLITUDE (0..1, how strong) for a DURATION
// (seconds). There's no true continuous-haptics API, so a steady buzz is made by re-sending a short
// impulse every `pulseInterval` seconds with the amplitude we want right now. That's the whole trick -
// we recompute amplitude each interval from the molecule's strain and keep re-issuing it.
//
// Setup: drop this on the Molecule object (next to MoleculeController). Drag the LeftHand / RightHand
// Controller objects (which carry a HapticImpulsePlayer, from the XRI Starter Assets) into the two
// slots; if left empty it tries to find them automatically.

using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using Molecule_Shapes.Model;
using Molecule_Shapes.Game;

namespace Molecule_Shapes.View
{
    [RequireComponent(typeof(MoleculeController))]
    public class MoleculeHapticFeedback : MonoBehaviour
    {
        private enum EnergySource
        {
            DisplacementFromIdeal,   // how far atoms are pushed from their stable positions (0 at rest) - recommended
            RepulsionCrowding        // excess closeness: 0 at rest, rises only when groups get nearer than Crowd Reference Distance
        }

        [Header("Controllers")]
        [Tooltip("HapticImpulsePlayer on the LeftHand Controller (XRI Starter Assets). Auto-found if empty.")]
        [SerializeField] private HapticImpulsePlayer leftHand;
        [SerializeField] private HapticImpulsePlayer rightHand;

        [Header("Solve pulse")]
        [SerializeField] private bool hapticOnSolve = true;
        [SerializeField, Range(0f, 1f)] private float solveAmplitude = 0.8f;
        [SerializeField] private float solveDuration = 0.12f;
        [Tooltip("Number of buzzes in the solve pattern (2 = a satisfying double-tap).")]
        [SerializeField] private int solvePulses = 2;
        [SerializeField] private float solveGap = 0.08f;

        [Header("Wrong answer / timeout pulse")]
        [SerializeField] private bool hapticOnFail = true;
        [SerializeField, Range(0f, 1f)] private float failAmplitude = 0.35f;
        [SerializeField] private float failDuration = 0.25f;

        [Header("Energy (molecule strain) haptic")]
        [SerializeField] private bool energyHapticEnabled = true;
        [Tooltip("DisplacementFromIdeal = how far atoms are pushed from their stable positions (0 at rest). " +
                 "RepulsionCrowding = how much closer than Crowd Reference Distance any pair gets (0 at rest, " +
                 "spikes when you shove atoms together). Both are ~0 when the molecule is relaxed.")]
        [SerializeField] private EnergySource energySource = EnergySource.DisplacementFromIdeal;
        [Tooltip("Strain at/below which there's no buzz.")]
        [SerializeField] private float lowThreshold = 0.5f;
        [Tooltip("Strain at/above which the buzz is at maximum.")]
        [SerializeField] private float highThreshold = 8f;
        [Tooltip("RepulsionCrowding only: pairs closer than this (model units) count as crowded. Keep it " +
                 "below the resting minimum spacing (~14 for atoms) so there's no buzz at equilibrium.")]
        [SerializeField] private float crowdReferenceDistance = 9f;
        [SerializeField, Range(0f, 1f)] private float minAmplitude = 0.05f;
        [SerializeField, Range(0f, 1f)] private float maxAmplitude = 0.6f;
        [Tooltip("Seconds between energy re-pulses. Smaller = smoother buzz, more frequent calls.")]
        [SerializeField] private float pulseInterval = 0.05f;
        [Tooltip("On: both controllers buzz from ambient strain (feel the molecule spring back after release). " +
                 "Off: only the hand currently dragging an atom buzzes.")]
        [SerializeField] private bool buzzBothFromAmbientStrain = true;

        private MoleculeController _controller;
        private MoleculeXRDragController _drag;
        private GameSessionController _game;
        private float _energyTimer;
        private bool _subscribed;

        private void Awake()
        {
            _controller = GetComponent<MoleculeController>();
            _drag = GetComponent<MoleculeXRDragController>();
            _game = GetComponent<GameSessionController>();
        }

        private void Start()
        {
            AutoFindHands();
            Subscribe();
        }

        private void OnDestroy() => Unsubscribe();

        private void Update()
        {
            if (energyHapticEnabled) UpdateEnergyHaptic(Time.deltaTime);
        }

        // --- Solve / fail pulses --------------------------------------------------------------------

        private void Subscribe()
        {
            GameSession s = _game != null ? _game.Session : null;
            if (_subscribed || s == null) return;
            s.ChallengeSolved += OnSolved;
            s.AnswerJudged += OnAnswerJudged;
            s.ChallengeTimedOut += OnTimedOut;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            GameSession s = _game != null ? _game.Session : null;
            if (!_subscribed || s == null) return;
            s.ChallengeSolved -= OnSolved;
            s.AnswerJudged -= OnAnswerJudged;
            s.ChallengeTimedOut -= OnTimedOut;
            _subscribed = false;
        }

        private void OnSolved(Challenge c, ScoreBreakdown b)
        {
            if (hapticOnSolve) StartCoroutine(SolvePattern());
        }

        private void OnAnswerJudged(Challenge c, bool correct)
        {
            if (!correct && hapticOnFail) FailBuzz();   // correct answers route through OnSolved
        }

        private void OnTimedOut(Challenge c)
        {
            if (hapticOnFail) FailBuzz();
        }

        private IEnumerator SolvePattern()
        {
            int n = Mathf.Max(1, solvePulses);
            for (int i = 0; i < n; i++)
            {
                Pulse(leftHand, solveAmplitude, solveDuration);
                Pulse(rightHand, solveAmplitude, solveDuration);
                if (i < n - 1) yield return new WaitForSeconds(solveDuration + solveGap);
            }
        }

        private void FailBuzz()
        {
            Pulse(leftHand, failAmplitude, failDuration);
            Pulse(rightHand, failAmplitude, failDuration);
        }

        // --- Energy haptic --------------------------------------------------------------------------

        private void UpdateEnergyHaptic(float dt)
        {
            _energyTimer += dt;
            if (_energyTimer < pulseInterval) return;
            _energyTimer = 0f;

            VsepRMolecule m = _controller.Molecule;
            if (m == null) return;

            float strain = ReadStrain(m);
            float t = Mathf.InverseLerp(lowThreshold, highThreshold, strain);
            if (t <= 0f) return;

            float amp = Mathf.Lerp(minAmplitude, maxAmplitude, t);
            float dur = pulseInterval * 1.6f;   // slight overlap so the buzz feels continuous

            bool left = _drag != null && _drag.IsLeftDragging;
            bool right = _drag != null && _drag.IsRightDragging;
            if (buzzBothFromAmbientStrain) { left = true; right = true; }

            if (left) Pulse(leftHand, amp, dur);
            if (right) Pulse(rightHand, amp, dur);
        }

        private float ReadStrain(VsepRMolecule m)
        {
            if (energySource == EnergySource.DisplacementFromIdeal)
                return m.LastAttractorError;

            // Excess-crowding: sum how far below the reference distance each pair is. Zero at rest
            // (all pairs are farther apart than the reference), rising as groups are shoved together.
            var groups = m.RadialGroups;
            float crowding = 0f;
            for (int i = 0; i < groups.Count; i++)
            {
                for (int j = i + 1; j < groups.Count; j++)
                {
                    float d = Vector3.Distance(ToV3(groups[i].Position), ToV3(groups[j].Position));
                    if (d < crowdReferenceDistance) crowding += crowdReferenceDistance - d;
                }
            }
            return crowding;
        }

        // --- Helpers --------------------------------------------------------------------------------

        private static void Pulse(HapticImpulsePlayer player, float amplitude, float duration)
        {
            if (player != null && amplitude > 0f) player.SendHapticImpulse(amplitude, duration);
        }

        private void AutoFindHands()
        {
            if (leftHand != null && rightHand != null) return;
            var players = FindObjectsByType<HapticImpulsePlayer>(FindObjectsSortMode.InstanceID);
            if (players.Length == 0)
            {
                Debug.LogWarning("MoleculeHapticFeedback: no HapticImpulsePlayer found. Assign the " +
                                 "LeftHand / RightHand Controller objects to get haptics.", this);
                return;
            }
            if (leftHand == null && players.Length > 0) leftHand = players[0];
            if (rightHand == null && players.Length > 1) rightHand = players[1];
        }

        private static Vector3 ToV3(Unity.Mathematics.float3 v) => new Vector3(v.x, v.y, v.z);
    }
}

// Plays sound feedback for the game: a "correct" chime on a solve, a gentle "try again" tone on a wrong
// answer / timeout, and a celebration flourish when a round completes. Gated by the Sound setting and
// scaled by Master Volume (AppSettings).
//
// Drop-in clips: assign your own AudioClip to the Correct/Incorrect/Celebration slots in the Inspector
// (Unity imports .wav / .ogg / .mp3 - .wav or .ogg preferred for short SFX). Leave a slot empty and a
// simple built-in tone is generated at runtime, so it works before you supply assets. Needs an
// AudioListener in the scene - the XR Origin's Main Camera already has one.

using UnityEngine;
using Molecule_Shapes.Game;

namespace Molecule_Shapes.View
{
    [RequireComponent(typeof(GameSessionController))]
    public class GameAudio : MonoBehaviour
    {
        [Header("Clips (optional - empty = built-in default tone)")]
        [SerializeField] private AudioClip correctClip;
        [SerializeField] private AudioClip incorrectClip;
        [SerializeField] private AudioClip celebrationClip;

        [Header("Volume")]
        [Tooltip("Base playback volume, further scaled by the Master Volume setting.")]
        [Range(0f, 1f)] [SerializeField] private float volume = 0.8f;

        private GameSessionController _controller;
        private AudioSource _source;
        private bool _subscribed;
        private AudioClip _defCorrect, _defIncorrect, _defCelebration;

        private GameSession Session => _controller != null ? _controller.Session : null;

        private void Awake()
        {
            _controller = GetComponent<GameSessionController>();
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;   // 2D UI sound, not positional

            // Built-in defaults: a rising two-note = correct, a falling two-note = try again, an
            // ascending arpeggio = celebration.
            _defCorrect     = ToneClip("sfx_correct",    new[] { 660f, 990f }, 0.14f);
            _defIncorrect   = ToneClip("sfx_incorrect",  new[] { 300f, 200f }, 0.18f);
            _defCelebration = ToneClip("sfx_celebration", new[] { 523f, 659f, 784f, 1046f }, 0.55f);
        }

        private void OnEnable() => TrySubscribe();
        private void Start() => TrySubscribe();     // Session exists after the controller's Awake
        private void OnDisable() => Unsubscribe();

        private void TrySubscribe()
        {
            if (_subscribed || Session == null) return;
            Session.ChallengeSolved += OnSolved;
            Session.AnswerJudged += OnAnswerJudged;
            Session.ChallengeTimedOut += OnTimedOut;
            Session.RoundCompleted += OnRoundCompleted;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Session == null) return;
            Session.ChallengeSolved -= OnSolved;
            Session.AnswerJudged -= OnAnswerJudged;
            Session.ChallengeTimedOut -= OnTimedOut;
            Session.RoundCompleted -= OnRoundCompleted;
            _subscribed = false;
        }

        private void OnSolved(Challenge c, ScoreBreakdown b) => Play(correctClip, _defCorrect);
        private void OnAnswerJudged(Challenge c, bool correct) { if (!correct) Play(incorrectClip, _defIncorrect); }
        private void OnTimedOut(Challenge c) => Play(incorrectClip, _defIncorrect);
        private void OnRoundCompleted() => Play(celebrationClip, _defCelebration);

        private void Play(AudioClip preferred, AudioClip fallback)
        {
            AppSettings s = AppSettings.Current;
            if (!s.SoundEnabled) return;
            AudioClip clip = preferred != null ? preferred : fallback;
            if (clip != null) _source.PlayOneShot(clip, volume * s.MasterVolume);
        }

        // Sequential sine notes with a short decay envelope - a usable placeholder sound with no assets.
        private static AudioClip ToneClip(string name, float[] notes, float seconds)
        {
            const int rate = 44100;
            int total = Mathf.Max(1, (int)(rate * seconds));
            var data = new float[total];
            int per = Mathf.Max(1, total / notes.Length);
            for (int n = 0; n < notes.Length; n++)
            {
                int start = n * per;
                int end = (n == notes.Length - 1) ? total : Mathf.Min(total, start + per);
                for (int i = start; i < end; i++)
                {
                    float t = (i - start) / (float)rate;
                    float env = Mathf.Min(1f, (end - i) / (rate * 0.03f));   // fade the tail
                    data[i] = Mathf.Sin(2f * Mathf.PI * notes[n] * t) * 0.4f * env;
                }
            }
            var clip = AudioClip.Create(name, total, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

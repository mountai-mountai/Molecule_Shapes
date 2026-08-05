// Plays sound feedback for the game: a "correct" chime on a solve, a "try again" tone on a wrong answer
// or timeout, and a celebration flourish when a round completes.
//
// Each category offers a LIST of choices the player picks from in the Settings panel (saved to
// PlayerPrefs), always including "None" for silence:
//     option 0        = None (silent)
//     options 1..n    = your custom clips from the Inspector arrays below (if any)
//     then            = the built-in generated tones, so there are always real choices with no assets
//
// Drop-in clips: assign AudioClips to the Correct/Incorrect/Celebration arrays. Unity imports .wav,
// .ogg and .mp3 (.wav or .ogg preferred for short SFX). Needs an AudioListener in the scene - the XR
// Origin's Main Camera already has one.

using System.Collections.Generic;
using UnityEngine;
using Molecule_Shapes.Game;
using SoundCategory = Molecule_Shapes.Game.AppSettings.SoundCategory;

namespace Molecule_Shapes.View
{
    [RequireComponent(typeof(GameSessionController))]
    public class GameAudio : MonoBehaviour
    {
        [Header("Custom clips (optional - listed before the built-in ones)")]
        [Tooltip("Played when a challenge is solved. Leave empty to use the built-in tones.")]
        [SerializeField] private AudioClip[] correctClips;
        [Tooltip("Played on a wrong answer or a timeout.")]
        [SerializeField] private AudioClip[] incorrectClips;
        [Tooltip("Played when a round is completed.")]
        [SerializeField] private AudioClip[] celebrationClips;

        [Header("Volume")]
        [Tooltip("Base playback volume, further scaled by the Master Volume setting.")]
        [Range(0f, 1f)] [SerializeField] private float volume = 0.8f;

        private GameSessionController _controller;
        private AudioSource _source;
        private bool _subscribed;

        // Per-category option list; index 0 is always ("None", null).
        private readonly Dictionary<SoundCategory, List<(string name, AudioClip clip)>> _options = new();

        private GameSession Session => _controller != null ? _controller.Session : null;

        private void Awake()
        {
            _controller = GetComponent<GameSessionController>();
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;   // 2D UI sound, not positional
            EnsureOptions();
        }

        // Built lazily so it doesn't matter whether this Awake or the settings panel's runs first
        // (Unity doesn't order Awake between components).
        private void EnsureOptions()
        {
            if (_options.Count > 0) return;

            BuildOptions(SoundCategory.Correct, correctClips, new[]
            {
                ("Chime",  Tone("sfx_correct_chime",  new[] { 660f, 990f }, 0.14f)),
                ("Bell",   Tone("sfx_correct_bell",   new[] { 880f, 1320f }, 0.20f)),
                ("Blip",   Tone("sfx_correct_blip",   new[] { 1200f }, 0.07f))
            });
            BuildOptions(SoundCategory.Incorrect, incorrectClips, new[]
            {
                ("Soft",   Tone("sfx_wrong_soft",  new[] { 300f, 200f }, 0.18f)),
                ("Buzz",   Tone("sfx_wrong_buzz",  new[] { 160f, 160f }, 0.22f)),
                ("Low",    Tone("sfx_wrong_low",   new[] { 220f }, 0.12f))
            });
            BuildOptions(SoundCategory.Celebration, celebrationClips, new[]
            {
                ("Fanfare", Tone("sfx_win_fanfare", new[] { 523f, 659f, 784f, 1046f }, 0.55f)),
                ("Sparkle", Tone("sfx_win_sparkle", new[] { 1046f, 1318f, 1568f }, 0.40f)),
                ("Rise",    Tone("sfx_win_rise",    new[] { 392f, 523f, 659f, 784f, 1046f }, 0.70f))
            });
        }

        private void OnEnable() => TrySubscribe();
        private void Start() => TrySubscribe();     // Session exists after the controller's Awake
        private void OnDisable() => Unsubscribe();

        private void BuildOptions(SoundCategory category, AudioClip[] custom, (string, AudioClip)[] builtIn)
        {
            var list = new List<(string, AudioClip)> { ("None", null) };
            if (custom != null)
                foreach (AudioClip c in custom)
                    if (c != null) list.Add((c.name, c));
            list.AddRange(builtIn);
            _options[category] = list;
        }

        // --- Settings panel API ---------------------------------------------------------------------

        /// <summary>Display names of the choices for a category ("None" first).</summary>
        public IReadOnlyList<string> OptionNames(SoundCategory category)
        {
            EnsureOptions();
            var names = new List<string>();
            foreach ((string name, AudioClip _) in _options[category]) names.Add(name);
            return names;
        }

        public int OptionCount(SoundCategory category)
        {
            EnsureOptions();
            return _options[category].Count;
        }

        /// <summary>Plays a category's currently-selected sound, ignoring the on/off setting (so the
        /// Settings panel can audition a choice as you step through it).</summary>
        public void Preview(SoundCategory category) => PlayIndex(category, force: true);

        // --- Playback -------------------------------------------------------------------------------

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

        private void OnSolved(Challenge c, ScoreBreakdown b) => PlayIndex(SoundCategory.Correct);
        private void OnAnswerJudged(Challenge c, bool correct) { if (!correct) PlayIndex(SoundCategory.Incorrect); }
        private void OnTimedOut(Challenge c) => PlayIndex(SoundCategory.Incorrect);
        private void OnRoundCompleted() => PlayIndex(SoundCategory.Celebration);

        private void PlayIndex(SoundCategory category, bool force = false)
        {
            AppSettings s = AppSettings.Current;
            if (!force && !s.SoundEnabled) return;
            EnsureOptions();
            if (!_options.TryGetValue(category, out var list) || list.Count == 0) return;

            int i = s.GetSoundIndex(category);
            if (i <= 0 || i >= list.Count) return;      // 0 = None, or a stale index -> silent

            AudioClip clip = list[i].clip;
            if (clip != null && _source != null) _source.PlayOneShot(clip, volume * s.MasterVolume);
        }

        // Sequential sine notes with a short decay envelope - a usable placeholder with no assets.
        private static AudioClip Tone(string name, float[] notes, float seconds)
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

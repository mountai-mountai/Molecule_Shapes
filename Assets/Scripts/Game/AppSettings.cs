// User-facing app settings - the single place every "toggle X" preference lives. Pure C# data (no
// UnityEngine import here) so the schema stays testable and portable; persistence is delegated to
// SettingsStore (PlayerPrefs). Access the shared instance via AppSettings.Current; change a field then
// call Save() to persist and notify listeners.
//
// Field status for Phase 1:
//   PanelsUpright, ShowAxeModes   - LIVE (read by PanelGrabController / the game panel)
//   AllowTerminalLonePairs, Sound - persisted now; their features are wired in later phases
//   the rest                      - forward-declared so the saved schema is stable across phases

namespace Molecule_Shapes.Game
{
    public class AppSettings
    {
        // --- Live in Phase 1 ---
        public bool PanelsUpright = true;     // keep world-space panels vertical while they're moved
        public bool ShowAxeModes = false;     // advanced: expose the AXE-based learning objectives

        // --- Persisted in Phase 1, behaviour lands in later phases ---
        public bool AllowTerminalLonePairs = false;   // let lone pairs be added to terminal atoms
        public bool SoundEnabled = true;              // correct/incorrect game audio

        // --- Forward-declared (kept so PlayerPrefs schema doesn't churn) ---
        public bool CelebrationSound = true;
        public float MasterVolume = 1f;
        public int LosingPhraseIndex = 0;             // index into LosingPhrases

        /// <summary>Selectable "try again" style phrases (replaces "Wrong").</summary>
        public static readonly string[] LosingPhrases = { "Try Again", "Almost!", "Not quite" };

        public string LosingPhrase
        {
            get
            {
                int n = LosingPhrases.Length;
                int i = ((LosingPhraseIndex % n) + n) % n;   // safe wrap for any index
                return LosingPhrases[i];
            }
        }

        /// <summary>Raised after Save() so live listeners (panels) can react to a changed setting.</summary>
        public event System.Action Changed;

        /// <summary>Persist to disk and notify listeners.</summary>
        public void Save()
        {
            SettingsStore.Save(this);
            Changed?.Invoke();
        }

        // --- Shared instance ---
        private static AppSettings _current;
        public static AppSettings Current => _current ??= SettingsStore.Load();

        /// <summary>Re-read from disk (e.g. for tests, or after an external reset).</summary>
        public static void Reload() => _current = SettingsStore.Load();
    }
}

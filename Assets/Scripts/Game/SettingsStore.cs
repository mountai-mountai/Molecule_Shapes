// PlayerPrefs-backed persistence for AppSettings. Kept separate from AppSettings so the settings data
// stays a plain testable object and the storage mechanism can change later (e.g. a JSON file) without
// touching every reader.

using UnityEngine;

namespace Molecule_Shapes.Game
{
    public static class SettingsStore
    {
        private const string Prefix = "ms.settings.";

        public static AppSettings Load()
        {
            var s = new AppSettings();
            s.PanelsUpright          = GetBool("panelsUpright", s.PanelsUpright);
            s.ShowAxeModes           = GetBool("showAxe", s.ShowAxeModes);
            s.AllowTerminalLonePairs = GetBool("terminalLP", s.AllowTerminalLonePairs);
            s.SoundEnabled           = GetBool("sound", s.SoundEnabled);
            s.CorrectSoundIndex      = PlayerPrefs.GetInt(Prefix + "sndCorrect", s.CorrectSoundIndex);
            s.IncorrectSoundIndex    = PlayerPrefs.GetInt(Prefix + "sndIncorrect", s.IncorrectSoundIndex);
            s.CelebrationSoundIndex  = PlayerPrefs.GetInt(Prefix + "sndCelebration", s.CelebrationSoundIndex);
            s.MasterVolume           = PlayerPrefs.GetFloat(Prefix + "volume", s.MasterVolume);
            s.LosingPhraseIndex      = PlayerPrefs.GetInt(Prefix + "losePhrase", s.LosingPhraseIndex);
            return s;
        }

        public static void Save(AppSettings s)
        {
            SetBool("panelsUpright", s.PanelsUpright);
            SetBool("showAxe", s.ShowAxeModes);
            SetBool("terminalLP", s.AllowTerminalLonePairs);
            SetBool("sound", s.SoundEnabled);
            PlayerPrefs.SetInt(Prefix + "sndCorrect", s.CorrectSoundIndex);
            PlayerPrefs.SetInt(Prefix + "sndIncorrect", s.IncorrectSoundIndex);
            PlayerPrefs.SetInt(Prefix + "sndCelebration", s.CelebrationSoundIndex);
            PlayerPrefs.SetFloat(Prefix + "volume", s.MasterVolume);
            PlayerPrefs.SetInt(Prefix + "losePhrase", s.LosingPhraseIndex);
            PlayerPrefs.Save();
        }

        private static bool GetBool(string key, bool def) => PlayerPrefs.GetInt(Prefix + key, def ? 1 : 0) != 0;
        private static void SetBool(string key, bool val) => PlayerPrefs.SetInt(Prefix + key, val ? 1 : 0);
    }
}

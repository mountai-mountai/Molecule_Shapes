// World-space settings panel for the VR scene. Binds controls to AppSettings and persists every change
// (PlayerPrefs via SettingsStore), so choices carry across sessions. Same theme/placement/movable/live-
// rebuild pattern as the other panels.
//
// Rows:
//   Panels stay upright     - LIVE (PanelGrabController reads it)
//   Terminal lone pairs     - saved; feature wired in a later phase
//   Sound on/off            - LIVE (GameAudio)
//   Correct / Try again / Celebration sound  - LIVE, pick per category (incl. "None"); auditions on change
//   "Try again" phrase      - LIVE (the game panel uses it for wrong answers)
//   [Advanced] Show AXE modes + a "?" that explains AXE notation - LIVE (game panel's Mode list)

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Molecule_Shapes.Game;
using SoundCategory = Molecule_Shapes.Game.AppSettings.SoundCategory;

namespace Molecule_Shapes.View
{
    public class MoleculeXRSettingsPanel : MonoBehaviour
    {
        [Header("Theme")]
        [SerializeField] private PanelStyle style;

        [Header("References")]
        [Tooltip("Supplies the selectable sound lists. Empty = found on this object, else anywhere in the scene.")]
        [SerializeField] private GameAudio gameAudio;

        [Header("Placement / axis (world space)")]
        [Tooltip("Offset from this object's position (default: further right, past the controls panel).")]
        [SerializeField] private Vector3 panelWorldOffset = new Vector3(0.72f, 0f, 0f);
        [SerializeField] private Vector3 panelEulerAngles = Vector3.zero;
        [SerializeField] private Vector2 panelSizeMeters = new Vector2(0.36f, 0.80f);
        [SerializeField] private float canvasScale = 0.001f;

        [Header("Movable")]
        [SerializeField] private bool movable = true;
        [SerializeField] private float handleThickness = 0.02f;
        [Range(0.05f, 1f)] [SerializeField] private float handleWidthFraction = 1f;
        [SerializeField] private Color handleColor = new Color(0.30f, 0.40f, 0.55f, 1f);

        [Header("Layout (UI units)")]
        [SerializeField] private float titleHeight = 48f;
        [SerializeField] private float sectionHeight = 30f;
        [SerializeField] private float rowHeight = 44f;
        [SerializeField] private float checkboxSize = 32f;
        [Tooltip("Height of each ◀ value ▶ selector row.")]
        [SerializeField] private float selectorHeight = 42f;
        [SerializeField] private float selectorArrowWidth = 40f;
        [SerializeField] private float selectorValueWidth = 150f;
        [Tooltip("Smallest font a selector value shrinks to so long names stay inside their box.")]
        [SerializeField] private int selectorMinFontSize = 11;
        [Tooltip("Height of the AXE help text when it's shown.")]
        [SerializeField] private float helpHeight = 120f;

        private const string AxeHelpText =
            "AXE notation describes the shape around the central atom:\n" +
            "A = the central atom,  X = each bonded atom,  E = each lone pair.\n" +
            "So water is AX2E2 - 2 bonded atoms and 2 lone pairs (bent).";

        private PanelStyle _style;
        private GameObject _panelRoot;
        private Text _phraseText, _correctText, _incorrectText, _celebrationText, _helpText;

        private void Awake()
        {
            ResolveAudio();
            BuildPanel();
        }

        private void OnDestroy() { if (_panelRoot != null) Destroy(_panelRoot); }

        private void OnValidate()
        {
            if (Application.isPlaying && _panelRoot != null) RebuildNow();
        }

        [ContextMenu("Rebuild Panel")]
        public void RebuildNow()
        {
            if (!Application.isPlaying) return;
            Vector3? pos = null; Quaternion rot = Quaternion.identity;
            if (_panelRoot != null) { pos = _panelRoot.transform.position; rot = _panelRoot.transform.rotation; Destroy(_panelRoot); }
            BuildPanel();
            if (pos.HasValue) _panelRoot.transform.SetPositionAndRotation(pos.Value, rot);
        }

        private void ResolveAudio()
        {
            if (gameAudio != null) return;
            gameAudio = GetComponent<GameAudio>();
            if (gameAudio == null) gameAudio = FindAnyObjectByType<GameAudio>(FindObjectsInactive.Exclude);
        }

        private void EnsureStyle() => _style = style != null ? style : (_style != null ? _style : PanelStyle.CreateDefault());

        private void BuildPanel()
        {
            EnsureStyle();
            AppSettings s = AppSettings.Current;

            _panelRoot = new GameObject("MoleculeXRSettingsPanel Root");
            _panelRoot.transform.SetPositionAndRotation(transform.position + panelWorldOffset, Quaternion.Euler(panelEulerAngles));

            var canvasGO = new GameObject("MoleculeXRSettingsPanel Canvas");
            canvasGO.transform.SetParent(_panelRoot.transform, false);
            canvasGO.transform.localScale = Vector3.one * canvasScale;

            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();

            RectTransform rt = canvasGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(panelSizeMeters.x / canvasScale, panelSizeMeters.y / canvasScale);

            AddBackground(canvasGO.transform);

            Transform col = PanelUi.MakeColumn(canvasGO.transform, _style, _style.rowSpacing, fillParent: true);

            PanelUi.MakeLabel(col, _style, "Settings", _style.titleFontSize, FontStyle.Bold,
                              TextAnchor.MiddleCenter, _style.textColor, titleHeight);

            PanelUi.MakeToggleRow(col, _style, "Panels stay upright", s.PanelsUpright,
                v => { s.PanelsUpright = v; s.Save(); }, rowHeight, checkboxSize, _style.textColor);

            // --- Sound ---
            PanelUi.MakeLabel(col, _style, "Sound", _style.sectionFontSize, FontStyle.Bold,
                              TextAnchor.MiddleLeft, _style.subtitleColor, sectionHeight);

            PanelUi.MakeToggleRow(col, _style, "Sound on", s.SoundEnabled,
                v => { s.SoundEnabled = v; s.Save(); }, rowHeight, checkboxSize, _style.textColor);

            _correctText     = MakeSoundSelector(col, "Correct", SoundCategory.Correct);
            _incorrectText   = MakeSoundSelector(col, "Try again", SoundCategory.Incorrect);
            _celebrationText = MakeSoundSelector(col, "Celebration", SoundCategory.Celebration);

            // --- Wording ---
            PanelUi.MakeLabel(col, _style, "\"Try again\" phrase", _style.subtitleFontSize, FontStyle.Normal,
                              TextAnchor.MiddleCenter, _style.subtitleColor, sectionHeight);
            _phraseText = PanelUi.MakeSelectorRow(col, _style, selectorHeight, selectorArrowWidth,
                selectorValueWidth, selectorMinFontSize,
                () => StepPhrase(-1), () => StepPhrase(+1));

            // --- Advanced ---
            PanelUi.MakeLabel(col, _style, "Advanced", _style.sectionFontSize, FontStyle.Bold,
                              TextAnchor.MiddleLeft, _style.subtitleColor, sectionHeight);

            // Terminal lone pairs sits here (above the AXE row) so the AXE toggle and the help text its
            // "?" reveals stay adjacent - nothing gets inserted between the button and its own text.
            PanelUi.MakeToggleRow(col, _style, "Terminal lone pairs", s.AllowTerminalLonePairs,
                v => { s.AllowTerminalLonePairs = v; s.Save(); }, rowHeight, checkboxSize, _style.textColor);

            Toggle axeToggle = PanelUi.MakeToggleRow(col, _style, "Show AXE modes", s.ShowAxeModes,
                v => { s.ShowAxeModes = v; s.Save(); }, rowHeight, checkboxSize, _style.textColor);

            // "?" sits in the same row as the AXE toggle and reveals what AXE notation means, for
            // classes that don't teach the notation explicitly.
            PanelUi.MakeButton(axeToggle.transform.parent, _style, "?",
                               () => { if (_helpText != null) _helpText.gameObject.SetActive(!_helpText.gameObject.activeSelf); },
                               fixedWidth: checkboxSize);

            _helpText = PanelUi.MakeLabel(col, _style, AxeHelpText, _style.subtitleFontSize, FontStyle.Normal,
                                          TextAnchor.UpperLeft, _style.subtitleColor, helpHeight);
            _helpText.gameObject.SetActive(false);

            PanelUi.AddFlexibleSpacer(col);

            if (movable) PanelUi.AddHandle(_panelRoot.transform, panelSizeMeters, handleThickness, handleColor, handleWidthFraction);

            RefreshValues();
            PanelUi.ForceRebuild(col);
        }

        // A captioned ◀ value ▶ row for one sound category.
        private Text MakeSoundSelector(Transform col, string caption, SoundCategory category)
        {
            PanelUi.MakeLabel(col, _style, caption, _style.subtitleFontSize, FontStyle.Normal,
                              TextAnchor.MiddleCenter, _style.subtitleColor, sectionHeight);
            return PanelUi.MakeSelectorRow(col, _style, selectorHeight, selectorArrowWidth,
                selectorValueWidth, selectorMinFontSize,
                () => StepSound(category, -1), () => StepSound(category, +1));
        }

        private void StepSound(SoundCategory category, int dir)
        {
            AppSettings s = AppSettings.Current;
            int count = gameAudio != null ? gameAudio.OptionCount(category) : 1;
            if (count <= 0) return;

            int i = ((s.GetSoundIndex(category) + dir) % count + count) % count;   // wrap both directions
            s.SetSoundIndex(category, i);
            s.Save();
            RefreshValues();
            if (gameAudio != null) gameAudio.Preview(category);   // audition the choice
        }

        private void StepPhrase(int dir)
        {
            AppSettings s = AppSettings.Current;
            int n = AppSettings.LosingPhrases.Length;
            s.LosingPhraseIndex = ((s.LosingPhraseIndex + dir) % n + n) % n;
            s.Save();
            RefreshValues();
        }

        private void RefreshValues()
        {
            AppSettings s = AppSettings.Current;
            if (_phraseText != null) _phraseText.text = s.LosingPhrase;
            SetSoundLabel(_correctText, SoundCategory.Correct);
            SetSoundLabel(_incorrectText, SoundCategory.Incorrect);
            SetSoundLabel(_celebrationText, SoundCategory.Celebration);
        }

        private void SetSoundLabel(Text target, SoundCategory category)
        {
            if (target == null) return;
            if (gameAudio == null) { target.text = "—"; return; }

            var names = gameAudio.OptionNames(category);
            int i = AppSettings.Current.GetSoundIndex(category);
            target.text = (i >= 0 && i < names.Count) ? names[i] : "None";
        }

        private void AddBackground(Transform parent)
        {
            var bg = new GameObject("Background", typeof(RectTransform));
            bg.transform.SetParent(parent, false);
            RectTransform r = bg.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            Image img = bg.AddComponent<Image>();
            img.raycastTarget = false;
            PanelUi.StyleAsPanel(img, _style);
        }
    }
}

// World-space game HUD for the VR scene. Drives the gamified layer via GameSessionController and shows
// live score/timer/prompt. The LOOK comes from a shared PanelStyle theme; placement/axis, layout sizes,
// and content options are per-panel. Editing any field while playing rebuilds the panel live (or
// right-click -> Rebuild Panel).

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Molecule_Shapes.Game;

namespace Molecule_Shapes.View
{
    [RequireComponent(typeof(GameSessionController))]
    public class MoleculeXRGamePanel : MonoBehaviour
    {
        [Header("Theme")]
        [Tooltip("Shared look (fonts/colours/edges). Create via Assets -> Create -> Molecule Shapes -> " +
                 "Panel Style, then assign here. Empty = built-in defaults.")]
        [SerializeField] private PanelStyle style;

        [Header("Placement / axis (world space)")]
        [Tooltip("Offset of the whole panel from the molecule's initial position (default: left of it).")]
        [SerializeField] private Vector3 panelWorldOffset = new Vector3(-0.45f, 0f, 0f);
        [Tooltip("Panel orientation in degrees. (0,0,0) faces the user on -Z; tilt/rotate freely.")]
        [SerializeField] private Vector3 panelEulerAngles = Vector3.zero;
        [Tooltip("Panel size in metres (x = width, y = height). Raise height if content ever overflows.")]
        [SerializeField] private Vector2 panelSizeMeters = new Vector2(0.34f, 0.84f);
        [Tooltip("Metres per UI unit. Smaller = sharper text but smaller visual font.")]
        [SerializeField] private float canvasScale = 0.001f;

        [Header("Movable")]
        [Tooltip("Add a grab bar on top so the panel can be picked up and moved (needs one " +
                 "PanelGrabController in the scene, wired to the controllers + grip actions).")]
        [SerializeField] private bool movable = true;
        [Tooltip("Thickness of the grab bar, metres.")]
        [SerializeField] private float handleThickness = 0.02f;
        [Tooltip("Grab-bar width as a fraction of the panel width. 1 = full bar; smaller = a centered tab.")]
        [Range(0.05f, 1f)] [SerializeField] private float handleWidthFraction = 1f;
        [SerializeField] private Color handleColor = new Color(0.30f, 0.40f, 0.55f, 1f);

        [Header("Score badge (floats beside the title, outside the panel)")]
        [Tooltip("Badge size in UI units (x = width, y = height).")]
        [SerializeField] private Vector2 scoreBadgeSize = new Vector2(240f, 64f);
        [Tooltip("Badge position from the panel's top edge, in UI units. Negative x pushes it left of the " +
                 "panel (away from the molecule); y offsets down from the top.")]
        [SerializeField] private Vector2 scoreBadgeOffset = new Vector2(-14f, -4f);

        [Header("Layout - element heights (UI units)")]
        [Tooltip("Height of the 'VSEPR Challenge' title row.")]
        [SerializeField] private float titleHeight = 44f;
        [Tooltip("Height of the small 'Mode' / 'Level' subtitle labels above each selector.")]
        [SerializeField] private float subtitleHeight = 22f;
        [Tooltip("Height of each ◀ value ▶ selector row (its buttons match this height).")]
        [SerializeField] private float selectorHeight = 44f;
        [Tooltip("Width of the ◀ / ▶ arrow buttons.")]
        [SerializeField] private float selectorArrowWidth = 44f;
        [Tooltip("Width of the value label between the arrows. The ◀ value ▶ cluster is centered, so a " +
                 "smaller value here tightens the spacing around Mode/Level.")]
        [SerializeField] private float selectorValueWidth = 190f;
        [Tooltip("Smallest font the selector value may shrink to so long mode names (e.g. 'Identify " +
                 "Molecular Geometry') fit inside their box instead of overlapping the ◀ ▶ arrows.")]
        [SerializeField] private int selectorMinFontSize = 11;
        [Tooltip("Height of the Start / Stop button row (those buttons match this height).")]
        [SerializeField] private float startRowHeight = 52f;
        [Tooltip("Height of the challenge prompt text area.")]
        [SerializeField] private float promptHeight = 64f;
        [Tooltip("Height of EACH multiple-choice answer button (Identify modes).")]
        [SerializeField] private float answerButtonHeight = 46f;
        [Tooltip("Height of the Next / Hint button row.")]
        [SerializeField] private float actionRowHeight = 50f;
        [Tooltip("Height of the feedback text area (raise it if the score breakdown wraps).")]
        [SerializeField] private float feedbackHeight = 76f;

        [Header("Content")]
        [Tooltip("Show the Level (difficulty) selector. Off = always use Default Difficulty (pools are " +
                 "cumulative, so Hard already includes every shape).")]
        [SerializeField] private bool showDifficultySelector = true;
        [SerializeField] private LearningObjective defaultObjective = LearningObjective.BuildMolecularGeometry;
        [SerializeField] private ChallengeDifficulty defaultDifficulty = ChallengeDifficulty.Hard;
        [Tooltip("Put each score-breakdown item on its own line under the result.")]
        [SerializeField] private bool multilineBreakdown = true;

        private GameSessionController _game;
        private GameSession Session => _game != null ? _game.Session : null;
        private PanelStyle _style;
        private GameObject _panelRoot;
        private GameObject _canvasGO;

        private Transform _col, _actionRow;
        private Text _objectiveText, _difficultyText, _promptText, _infoText, _feedbackText;
        private Button _stopButton, _nextButton, _hintButton;
        private readonly List<GameObject> _answerButtons = new();

        private LearningObjective _selObjective;
        private ChallengeDifficulty _selDifficulty;

        private const string DefaultPrompt = "Press Start to begin.";

        private void Awake()
        {
            _game = GetComponent<GameSessionController>();
            _selObjective = defaultObjective;
            _selDifficulty = defaultDifficulty;
            ClampObjective();                              // don't start on a hidden AXE mode
            AppSettings.Current.Changed += OnSettingsChanged;
            BuildPanel();
        }

        // When "Show AXE modes" is toggled in Settings, re-clamp the selection and refresh the label.
        private void OnSettingsChanged()
        {
            ClampObjective();
            RefreshSelectors();
        }

        private void OnEnable() { if (Session != null) Subscribe(); }

        private void Start()
        {
            Subscribe();
            RefreshSelectors();
            ResetDisplay();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            AppSettings.Current.Changed -= OnSettingsChanged;
            if (_panelRoot != null) Destroy(_panelRoot);
        }

        private void Update() { if (Session != null) RefreshInfoLine(); }

        private void OnValidate()
        {
            if (Application.isPlaying && _panelRoot != null) RebuildNow();
        }

        [ContextMenu("Rebuild Panel")]
        public void RebuildNow()
        {
            if (!Application.isPlaying) return;

            // Preserve where the user moved the panel to across a live rebuild.
            Vector3? pos = null; Quaternion rot = Quaternion.identity;
            if (_panelRoot != null)
            {
                pos = _panelRoot.transform.position; rot = _panelRoot.transform.rotation;
                Destroy(_panelRoot);
            }
            _answerButtons.Clear();
            BuildPanel();
            if (pos.HasValue) _panelRoot.transform.SetPositionAndRotation(pos.Value, rot);

            RefreshSelectors();
            if (Session != null && Session.Mode == GameMode.Challenge && Session.Current != null)
                OnChallengeStarted(Session.Current);
            else
                ResetDisplay();
        }

        // Prefer an assigned theme asset (picked up even if assigned at runtime); else reuse/create defaults.
        private void EnsureStyle() => _style = style != null ? style : (_style != null ? _style : PanelStyle.CreateDefault());

        // --- Event wiring ---------------------------------------------------------------------------

        private bool _subscribed;

        private void Subscribe()
        {
            if (_subscribed || Session == null) return;
            Session.ChallengeStarted += OnChallengeStarted;
            Session.ChallengeSolved += OnChallengeSolved;
            Session.AnswerJudged += OnAnswerJudged;
            Session.ChallengeTimedOut += OnChallengeTimedOut;
            Session.RoundCompleted += OnRoundCompleted;
            Session.StateChanged += OnStateChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Session == null) return;
            Session.ChallengeStarted -= OnChallengeStarted;
            Session.ChallengeSolved -= OnChallengeSolved;
            Session.AnswerJudged -= OnAnswerJudged;
            Session.ChallengeTimedOut -= OnChallengeTimedOut;
            Session.RoundCompleted -= OnRoundCompleted;
            Session.StateChanged -= OnStateChanged;
            _subscribed = false;
        }

        private void OnChallengeStarted(Challenge c)
        {
            _promptText.text = c.Prompt;
            SetFeedback("", _style.textColor);
            RebuildAnswers(c);
        }

        private void OnChallengeSolved(Challenge c, ScoreBreakdown b) =>
            SetFeedback(BuildSolveText(b), _style.textColor);   // rich text colours the parts itself

        private void OnAnswerJudged(Challenge c, bool correct)
        {
            if (correct) return;   // a correct answer routes through OnChallengeSolved
            int penalty = Session != null ? Session.Rules.wrongAnswerPenalty : 0;
            string red = Hex(_style.badColor);
            string lost = penalty > 0 ? $"  <color={red}>−{penalty}</color>" : "";
            // "Try Again" (or the chosen phrase) instead of "Wrong", to reduce fear of failure.
            SetFeedback($"{AppSettings.Current.LosingPhrase}{lost}", _style.textColor);
        }

        private void OnChallengeTimedOut(Challenge c) =>
            SetFeedback($"Time's up - it was {c.Goal.GeometryName}.", _style.badColor);

        // End of a finite round: show the score summary. The tally is coloured by how it actually went,
        // so skipping through with Next doesn't read as a win (GameAudio/CelebrationEffect gate their
        // celebration on the same result).
        private void OnRoundCompleted(int correct, int total)
        {
            ClearAnswers();

            float fraction = total > 0 ? correct / (float)total : 0f;
            bool didWell = fraction >= 0.6f;

            if (_promptText != null)
                _promptText.text = correct == 0 ? "Round over" : "Round complete!";

            int score = Session != null ? Session.Score.Total : 0;
            string tallyColor = Hex(didWell ? _style.goodColor : _style.badColor);
            SetFeedback($"<color={tallyColor}>You got {correct}/{total}</color>\nScore {score}",
                        _style.textColor);
        }

        private void OnStateChanged()
        {
            if (Session != null && Session.Mode == GameMode.Sandbox) ResetDisplay();
        }

        // --- Display state --------------------------------------------------------------------------

        private void ResetDisplay()
        {
            if (_promptText != null) _promptText.text = DefaultPrompt;
            SetFeedback("", _style.textColor);
            ClearAnswers();
            RefreshInfoLine();
        }

        // Stop / Next / Hint only mean something during an active round, so outside one they go dark and
        // stop responding to hover rather than looking pressable and doing nothing.
        private void RefreshButtonStates()
        {
            bool inGame = Session != null && Session.Mode == GameMode.Challenge;
            bool inRound = inGame && Session.RoundActive && Session.Current != null;

            if (_stopButton != null) _stopButton.interactable = inGame;
            if (_nextButton != null) _nextButton.interactable = inRound;
            // A hint is pointless once the question is already solved.
            if (_hintButton != null) _hintButton.interactable = inRound && !Session.CurrentSolved;
        }

        private void RefreshInfoLine()
        {
            RefreshButtonStates();
            if (_infoText == null) return;
            if (Session == null || Session.Mode != GameMode.Challenge)
            {
                _infoText.text = "Sandbox";
                return;
            }
            string timer = Session.Timer.Mode == TimerMode.CountDown
                ? $"{Session.Timer.Remaining:0.0}s left"
                : $"{Session.Timer.Elapsed:0.0}s";
            string progress = Session.RoundActive && Session.RoundLength > 0
                ? $"   Q {Session.RoundPosed}/{Session.RoundLength}"
                : "";
            _infoText.text = $"Score {Session.Score.Total}\nStreak {Session.Score.Streak}   {timer}{progress}";
        }

        private void RefreshSelectors()
        {
            if (_objectiveText != null) _objectiveText.text = ObjectiveLabel(_selObjective);
            if (_difficultyText != null) _difficultyText.text = _selDifficulty.ToString();
        }

        private void SetFeedback(string text, Color color)
        {
            if (_feedbackText == null) return;
            _feedbackText.text = text;
            _feedbackText.color = color;   // rich <color> tags override this per span
        }

        // Rich-text solve result: gains green, deductions red, and a correctly-signed total (no "+-90").
        private string BuildSolveText(ScoreBreakdown b)
        {
            string g = Hex(_style.goodColor);
            string r = Hex(_style.badColor);

            int net = b.Total;
            string headColor = net >= 0 ? g : r;
            string headAmt = (net >= 0 ? "+" : "−") + Mathf.Abs(net);
            string header = $"Solved!  <color={headColor}>{headAmt}</color>";

            var items = new List<string>
            {
                Colored(g, $"Base {b.basePoints}")   // base is a gain
            };
            AddItem(items, g, r, "Time", b.timeBonus);
            AddItem(items, g, r, "Accuracy", b.accuracyBonus);
            AddItem(items, g, r, "Streak", b.streakBonus);
            AddItem(items, g, r, "Attempts", b.attemptPenalty);
            AddItem(items, g, r, "Hints", b.hintPenalty);

            string sep = multilineBreakdown ? "\n" : "   ";
            return header + "\n" + string.Join(sep, items);
        }

        private static void AddItem(List<string> items, string green, string red, string name, int value)
        {
            if (value == 0) return;
            string col = value >= 0 ? green : red;
            string signed = value >= 0 ? $"+{value}" : $"−{-value}";
            items.Add(Colored(col, $"{name} {signed}"));
        }

        private static string Colored(string hex, string text) => $"<color={hex}>{text}</color>";
        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // --- Panel construction ---------------------------------------------------------------------

        private void BuildPanel()
        {
            EnsureStyle();

            _panelRoot = new GameObject("MoleculeXRGamePanel Root");
            _panelRoot.transform.SetPositionAndRotation(transform.position + panelWorldOffset, Quaternion.Euler(panelEulerAngles));

            _canvasGO = new GameObject("MoleculeXRGamePanel Canvas");
            _canvasGO.transform.SetParent(_panelRoot.transform, false);
            _canvasGO.transform.localScale = Vector3.one * canvasScale;

            Canvas canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;
            _canvasGO.AddComponent<CanvasScaler>();
            _canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();

            RectTransform rt = _canvasGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(panelSizeMeters.x / canvasScale, panelSizeMeters.y / canvasScale);

            AddBackground(_canvasGO.transform);
            BuildScoreBadge(_canvasGO.transform);

            _col = PanelUi.MakeColumn(_canvasGO.transform, _style, _style.rowSpacing, fillParent: true);

            PanelUi.MakeLabel(_col, _style, "VSEPR Challenge", _style.titleFontSize, FontStyle.Bold,
                              TextAnchor.MiddleCenter, _style.textColor, titleHeight);

            PanelUi.MakeLabel(_col, _style, "Mode", _style.subtitleFontSize, FontStyle.Normal,
                              TextAnchor.MiddleCenter, _style.subtitleColor, subtitleHeight);
            _objectiveText = CreateSelectorRow(
                () => { _selObjective = StepObjective(_selObjective, -1); RefreshSelectors(); },
                () => { _selObjective = StepObjective(_selObjective, +1); RefreshSelectors(); });

            if (showDifficultySelector)
            {
                PanelUi.MakeLabel(_col, _style, "Level", _style.subtitleFontSize, FontStyle.Normal,
                                  TextAnchor.MiddleCenter, _style.subtitleColor, subtitleHeight);
                _difficultyText = CreateSelectorRow(
                    () => { _selDifficulty = CycleEnum(_selDifficulty, -1); RefreshSelectors(); },
                    () => { _selDifficulty = CycleEnum(_selDifficulty, +1); RefreshSelectors(); });
            }
            else _difficultyText = null;

            Transform startRow = PanelUi.MakeRow(_col, startRowHeight, _style.buttonSpacing);
            PanelUi.MakeButton(startRow, _style, "Start", () => _game.StartChallenge(_selObjective, _selDifficulty));
            _stopButton = PanelUi.MakeButton(startRow, _style, "Stop", () => _game.EnterSandbox());

            _promptText = PanelUi.MakeLabel(_col, _style, DefaultPrompt, _style.promptFontSize, FontStyle.Bold,
                                            TextAnchor.MiddleCenter, _style.textColor, promptHeight);

            // Answer buttons are inserted here (direct children of the column) between the prompt and the
            // action row, so the vertical layout always spaces them and they can never overlap New/Hint.
            _actionRow = PanelUi.MakeRow(_col, actionRowHeight, _style.buttonSpacing);
            // "Next" (not "New") - it advances within the round; "New" read like starting a new game.
            _nextButton = PanelUi.MakeButton(_actionRow, _style, "Next", () => _game.NextChallenge());
            _hintButton = PanelUi.MakeButton(_actionRow, _style, "Hint", () => SetFeedback(_game.UseHint(), _style.textColor));

            _feedbackText = PanelUi.MakeLabel(_col, _style, "", _style.bodyFontSize, FontStyle.Bold,
                                              TextAnchor.UpperCenter, _style.textColor, feedbackHeight);

            PanelUi.AddFlexibleSpacer(_col);   // keeps every row at its set height (absorbs leftover space)

            if (movable) PanelUi.AddHandle(_panelRoot.transform, panelSizeMeters, handleThickness, handleColor, handleWidthFraction);

            RefreshSelectors();
            PanelUi.ForceRebuild(_col);
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

        // Score/streak/timer as its own small badge that floats beside the title, outside the panel body.
        private void BuildScoreBadge(Transform canvasT)
        {
            var badge = new GameObject("Score Badge", typeof(RectTransform));
            badge.transform.SetParent(canvasT, false);
            RectTransform r = badge.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);   // top-left corner of the panel
            r.pivot = new Vector2(1f, 1f);                     // badge's top-right pins there
            r.sizeDelta = scoreBadgeSize;
            r.anchoredPosition = scoreBadgeOffset;             // negative x -> outside, to the left

            Image img = badge.AddComponent<Image>();
            img.raycastTarget = false;
            PanelUi.StyleAsPanel(img, _style);

            _infoText = PanelUi.MakeLabel(badge.transform, _style, "", _style.infoFontSize, FontStyle.Bold,
                                          TextAnchor.MiddleCenter, _style.textColor, scoreBadgeSize.y);
            RectTransform lrt = _infoText.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(8, 4); lrt.offsetMax = new Vector2(-8, -4);
        }

        // "◀ value ▶" stepper. The row centers a fixed-width value between the arrows, so the cluster
        // stays tight instead of the value stretching the full panel width.
        //
        // Long names ("Identify Molecular Geometry") must stay INSIDE that fixed width - previously they
        // overflowed horizontally and drew on top of the arrows. So the value wraps and best-fits: it
        // uses up to two lines and shrinks the font (down to selectorMinFontSize) until it fits its box.
        private Text CreateSelectorRow(Action onPrev, Action onNext)
        {
            Transform row = PanelUi.MakeRow(_col, selectorHeight, _style.buttonSpacing, forceExpandWidth: false);
            PanelUi.MakeButton(row, _style, "◀", onPrev, fixedWidth: selectorArrowWidth);

            Text label = PanelUi.MakeLabel(row, _style, "", _style.valueFontSize, FontStyle.Bold,
                                           TextAnchor.MiddleCenter, _style.textColor, 0f, wrap: true);
            label.verticalOverflow = VerticalWrapMode.Truncate;   // best-fit shrinks instead of spilling
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(1, selectorMinFontSize);
            label.resizeTextMaxSize = _style.valueFontSize;

            LayoutElement le = label.GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = selectorValueWidth;
            le.flexibleWidth = 0f;                                // never stretch past the reserved width

            PanelUi.MakeButton(row, _style, "▶", onNext, fixedWidth: selectorArrowWidth);
            return label;
        }

        private void ClearAnswers()
        {
            for (int i = _answerButtons.Count - 1; i >= 0; i--)
                if (_answerButtons[i] != null) Destroy(_answerButtons[i]);
            _answerButtons.Clear();
        }

        private void RebuildAnswers(Challenge c)
        {
            ClearAnswers();
            if (c.Task != TaskMode.Identify || _actionRow == null) return;

            for (int i = 0; i < c.Options.Count; i++)
            {
                int index = i;
                Button btn = PanelUi.MakeButton(_col, _style, c.Options[i], () => _game.SubmitAnswer(index),
                                                height: answerButtonHeight);
                // Move it just above the New/Hint row so it lands between the prompt and the actions.
                btn.transform.SetSiblingIndex(_actionRow.GetSiblingIndex());
                _answerButtons.Add(btn.gameObject);
            }
        }

        // --- Helpers --------------------------------------------------------------------------------

        private static T CycleEnum<T>(T value, int dir) where T : Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            int i = Array.IndexOf(values, value);
            i = (i + dir + values.Length) % values.Length;
            return values[i];
        }

        // AXE-notation objectives, hidden unless "Show AXE modes" is enabled in Settings.
        private static bool IsAxe(LearningObjective o) =>
            o == LearningObjective.BuildFromAxe ||
            o == LearningObjective.IdentifyAxe ||
            o == LearningObjective.IdentifyBoth;

        // Objectives shown in the Mode selector. AXE modes require the advanced setting.
        private static bool IsVisible(LearningObjective o) => !IsAxe(o) || AppSettings.Current.ShowAxeModes;

        // Cycle to the next visible objective in the given direction.
        private static LearningObjective StepObjective(LearningObjective current, int dir)
        {
            var values = (LearningObjective[])Enum.GetValues(typeof(LearningObjective));
            int i = Array.IndexOf(values, current);
            for (int n = 0; n < values.Length; n++)
            {
                i = (i + dir + values.Length) % values.Length;
                if (IsVisible(values[i])) return values[i];
            }
            return current;
        }

        // If the current selection is hidden, move it to the nearest visible one.
        private void ClampObjective()
        {
            if (!IsVisible(_selObjective)) _selObjective = StepObjective(_selObjective, +1);
        }

        // "BuildFromAxe" -> "Build From AXE".
        private static string ObjectiveLabel(LearningObjective objective)
        {
            var sb = new StringBuilder();
            string s = objective.ToString();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i])) sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString().Replace("Axe", "AXE");
        }
    }
}

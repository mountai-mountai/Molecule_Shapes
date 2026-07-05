// World-space game HUD for the VR scene. Companion to MoleculeXRUiPanel (the molecule-editing panel):
// this one drives the gamified layer via GameSessionController and shows live score/timer/prompt.
// Built programmatically (no scene authoring) and placed at the scene root so it doesn't move when the
// molecule is grabbed. Controller-ray clicks come for free via the TrackedDeviceGraphicRaycaster.
//
// Layout (top to bottom):
//   title | "Mode" subtitle + selector | ("Level" subtitle + selector) | Start / Stop |
//   prompt | score+streak+timer | answer buttons (Identify only, auto-sized) | New + Hint | feedback
//
// Nearly every size / font / colour is exposed in the Inspector for on-the-fly tuning.

using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Molecule_Shapes.Game;

namespace Molecule_Shapes.View
{
    [RequireComponent(typeof(GameSessionController))]
    public class MoleculeXRGamePanel : MonoBehaviour
    {
        [Header("Placement (world space)")]
        [Tooltip("Where the panel sits relative to the molecule's initial position (default: left side).")]
        [SerializeField] private Vector3 panelWorldOffset = new Vector3(-0.45f, 0f, 0f);
        [Tooltip("Panel physical size in metres. Height must fit the tallest layout (Identify shows the most).")]
        [SerializeField] private Vector2 panelSizeMeters = new Vector2(0.34f, 0.82f);
        [Tooltip("1 UI unit = canvasScale metres (smaller = sharper text, smaller visual font).")]
        [SerializeField] private float canvasScale = 0.001f;

        [Header("Layout (UI units)")]
        [SerializeField] private int columnPadding = 20;
        [SerializeField] private float rowSpacing = 8f;
        [SerializeField] private float titleHeight = 44f;
        [SerializeField] private float subtitleHeight = 22f;
        [SerializeField] private float selectorHeight = 44f;
        [SerializeField] private float selectorArrowWidth = 44f;
        [SerializeField] private float startRowHeight = 52f;
        [SerializeField] private float promptHeight = 64f;
        [SerializeField] private float infoHeight = 30f;
        [SerializeField] private float answerButtonHeight = 46f;
        [SerializeField] private float answerSpacing = 6f;
        [SerializeField] private float actionRowHeight = 50f;
        [SerializeField] private float feedbackHeight = 72f;

        [Header("Fonts")]
        [SerializeField] private int titleFontSize = 28;
        [SerializeField] private int subtitleFontSize = 15;
        [SerializeField] private int valueFontSize = 20;
        [SerializeField] private int promptFontSize = 22;
        [SerializeField] private int infoFontSize = 18;
        [SerializeField] private int buttonFontSize = 18;
        [SerializeField] private int answerFontSize = 16;
        [SerializeField] private int feedbackFontSize = 17;

        [Header("Colours")]
        [SerializeField] private Color panelBackground = new Color(0.05f, 0.07f, 0.10f, 0.92f);
        [SerializeField] private Color buttonNormal = new Color(0.20f, 0.25f, 0.32f, 1f);
        [SerializeField] private Color buttonHover = new Color(0.30f, 0.40f, 0.55f, 1f);
        [SerializeField] private Color buttonPressed = new Color(0.45f, 0.60f, 0.85f, 1f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color subtitleColor = new Color(0.62f, 0.70f, 0.80f, 1f);
        [SerializeField] private Color goodColor = new Color(0.45f, 0.85f, 0.50f, 1f);
        [SerializeField] private Color badColor = new Color(0.90f, 0.45f, 0.40f, 1f);

        [Header("Selectors")]
        [Tooltip("Show the Level (difficulty) selector. Off = always use Default Difficulty (pools are " +
                 "cumulative, so Mixed/Hard already include every shape).")]
        [SerializeField] private bool showDifficultySelector = true;
        [SerializeField] private LearningObjective defaultObjective = LearningObjective.BuildFromAxe;
        [SerializeField] private ChallengeDifficulty defaultDifficulty = ChallengeDifficulty.Mixed;

        [Header("Feedback")]
        [Tooltip("Show the score breakdown (base, time, hints, ...) on its own lines under the result.")]
        [SerializeField] private bool multilineBreakdown = true;

        private GameSessionController _game;
        private GameSession Session => _game != null ? _game.Session : null;
        private Font _font;
        private GameObject _canvasGO;

        // Live-updated UI references.
        private Text _objectiveText;
        private Text _difficultyText;
        private Text _promptText;
        private Text _infoText;
        private Text _feedbackText;
        private Transform _answersContainer;
        private LayoutElement _answersLayout;

        // Selector state (what Start will launch).
        private LearningObjective _selObjective;
        private ChallengeDifficulty _selDifficulty;

        private const string DefaultPrompt = "Press Start to begin.";

        private void Awake()
        {
            _game = GetComponent<GameSessionController>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _selObjective = defaultObjective;
            _selDifficulty = defaultDifficulty;
            BuildPanel();
        }

        private void OnEnable()
        {
            if (Session != null) Subscribe();
        }

        private void Start()
        {
            Subscribe();                 // in case OnEnable ran before the session existed
            RefreshSelectors();
            ResetDisplay();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (_canvasGO != null) Destroy(_canvasGO);
        }

        private void Update()
        {
            if (Session != null) RefreshInfoLine();
        }

        // --- Event wiring ---------------------------------------------------------------------------

        private bool _subscribed;

        private void Subscribe()
        {
            if (_subscribed || Session == null) return;
            Session.ChallengeStarted += OnChallengeStarted;
            Session.ChallengeSolved += OnChallengeSolved;
            Session.AnswerJudged += OnAnswerJudged;
            Session.ChallengeTimedOut += OnChallengeTimedOut;
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
            Session.StateChanged -= OnStateChanged;
            _subscribed = false;
        }

        private void OnChallengeStarted(Challenge c)
        {
            _promptText.text = c.Prompt;
            SetFeedback("", textColor);
            RebuildAnswers(c);
        }

        private void OnChallengeSolved(Challenge c, ScoreBreakdown b)
        {
            string header = $"Solved!  +{b.Total}";
            SetFeedback(header + (multilineBreakdown ? "\n" : "  ") + b.Describe(multilineBreakdown), goodColor);
        }

        private void OnAnswerJudged(Challenge c, bool correct)
        {
            if (!correct) SetFeedback("Not quite - try the next one.", badColor);
            // A correct answer routes through OnChallengeSolved for the score breakdown.
        }

        private void OnChallengeTimedOut(Challenge c) =>
            SetFeedback($"Time's up - it was {c.Goal.GeometryName}.", badColor);

        // Fired on any mode/objective/difficulty change; when we drop to Sandbox (i.e. Stop), wipe the HUD.
        private void OnStateChanged()
        {
            if (Session != null && Session.Mode == GameMode.Sandbox) ResetDisplay();
        }

        // --- Display state --------------------------------------------------------------------------

        // Clears all generated text and answer buttons back to the idle/default look.
        private void ResetDisplay()
        {
            if (_promptText != null) _promptText.text = DefaultPrompt;
            SetFeedback("", textColor);
            ClearAnswers();
            RefreshInfoLine();
        }

        private void RefreshInfoLine()
        {
            if (_infoText == null) return;
            if (Session == null || Session.Mode != GameMode.Challenge)
            {
                _infoText.text = "Sandbox - free play";
                return;
            }
            string timer = Session.Timer.Mode == TimerMode.CountDown
                ? $"{Session.Timer.Remaining:0.0}s left"
                : $"{Session.Timer.Elapsed:0.0}s";
            _infoText.text = $"Score {Session.Score.Total}   Streak {Session.Score.Streak}   {timer}";
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
            _feedbackText.color = color;
        }

        // --- Panel construction ---------------------------------------------------------------------

        private void BuildPanel()
        {
            _canvasGO = new GameObject("MoleculeXRGamePanel Canvas");
            _canvasGO.transform.position = transform.position + panelWorldOffset;
            _canvasGO.transform.rotation = Quaternion.identity;     // -Z face points toward the user
            _canvasGO.transform.localScale = Vector3.one * canvasScale;

            Canvas canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;
            _canvasGO.AddComponent<CanvasScaler>();
            _canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();

            RectTransform rt = _canvasGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(panelSizeMeters.x / canvasScale, panelSizeMeters.y / canvasScale);

            AddBackground(_canvasGO.transform);

            Transform col = MakeColumn(_canvasGO.transform, rowSpacing, fillParent: true);

            CreateLabel(col, "VSEPR Challenge", titleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                        titleHeight, textColor);

            // Mode selector (subtitle above the value, per feedback).
            CreateLabel(col, "Mode", subtitleFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                        subtitleHeight, subtitleColor);
            _objectiveText = CreateSelectorRow(col,
                () => { _selObjective = CycleEnum(_selObjective, -1); RefreshSelectors(); },
                () => { _selObjective = CycleEnum(_selObjective, +1); RefreshSelectors(); });

            // Level selector (optional).
            if (showDifficultySelector)
            {
                CreateLabel(col, "Level", subtitleFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                            subtitleHeight, subtitleColor);
                _difficultyText = CreateSelectorRow(col,
                    () => { _selDifficulty = CycleEnum(_selDifficulty, -1); RefreshSelectors(); },
                    () => { _selDifficulty = CycleEnum(_selDifficulty, +1); RefreshSelectors(); });
            }

            // Start / Stop row. Stop ends the game and (via StateChanged) resets the HUD to default.
            Transform startRow = MakeRow(col, startRowHeight);
            CreateButton(startRow, "Start", () => _game.StartChallenge(_selObjective, _selDifficulty));
            CreateButton(startRow, "Stop", () => _game.EnterSandbox());

            _promptText = CreateLabel(col, DefaultPrompt, promptFontSize, FontStyle.Bold,
                                      TextAnchor.MiddleCenter, promptHeight, textColor);
            _infoText = CreateLabel(col, "", infoFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                                    infoHeight, textColor);

            // Answer buttons (Identify only) - own container, height set dynamically per challenge.
            _answersContainer = MakeColumn(col, answerSpacing, fillParent: false);
            _answersLayout = _answersContainer.gameObject.AddComponent<LayoutElement>();
            _answersLayout.minHeight = 0f;
            _answersLayout.preferredHeight = 0f;

            // New / Hint row.
            Transform actionRow = MakeRow(col, actionRowHeight);
            CreateButton(actionRow, "New", () => _game.NextChallenge());
            CreateButton(actionRow, "Hint", () => SetFeedback(_game.UseHint(), textColor));

            _feedbackText = CreateLabel(col, "", feedbackFontSize, FontStyle.Bold, TextAnchor.UpperCenter,
                                        feedbackHeight, textColor);
            _feedbackText.verticalOverflow = VerticalWrapMode.Overflow;

            RefreshSelectors();
        }

        private void AddBackground(Transform parent)
        {
            var bg = new GameObject("Background", typeof(RectTransform));
            bg.transform.SetParent(parent, false);
            RectTransform r = bg.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            Image img = bg.AddComponent<Image>();
            img.color = panelBackground;
            img.raycastTarget = false;
        }

        // A vertical layout column. If fillParent, it stretches to the canvas (with padding); otherwise
        // it's a content-sized sub-column whose height a LayoutElement controls.
        private Transform MakeColumn(Transform parent, float spacing, bool fillParent)
        {
            var go = new GameObject(fillParent ? "Column" : "SubColumn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform r = go.GetComponent<RectTransform>();
            if (fillParent)
            {
                r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(columnPadding, columnPadding);
                r.offsetMax = new Vector2(-columnPadding, -columnPadding);
            }
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            return go.transform;
        }

        private Transform MakeRow(Transform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = height; le.preferredHeight = height;
            return go.transform;
        }

        // "◀  <value>  ▶" selector row; returns the centre Text so callers can update the value.
        private Text CreateSelectorRow(Transform parent, Action onPrev, Action onNext)
        {
            Transform row = MakeRow(parent, selectorHeight);
            CreateButton(row, "◀", onPrev, selectorArrowWidth);
            Text label = CreateLabel(row, "", valueFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                                     selectorHeight, textColor);
            label.GetComponent<LayoutElement>().flexibleWidth = 1;
            CreateButton(row, "▶", onNext, selectorArrowWidth);
            return label;
        }

        private void ClearAnswers()
        {
            if (_answersContainer == null) return;
            for (int i = _answersContainer.childCount - 1; i >= 0; i--)
                Destroy(_answersContainer.GetChild(i).gameObject);
            if (_answersLayout != null) { _answersLayout.minHeight = 0f; _answersLayout.preferredHeight = 0f; }
        }

        private void RebuildAnswers(Challenge c)
        {
            ClearAnswers();
            if (c.Task != TaskMode.Identify) return;

            int n = c.Options.Count;
            for (int i = 0; i < n; i++)
            {
                int index = i;
                CreateButton(_answersContainer, c.Options[i], () => _game.SubmitAnswer(index),
                             fixedWidth: 0f, height: answerButtonHeight, fontSize: answerFontSize);
            }
            // Size the container to exactly hold its buttons so it can't overlap the New/Hint row or
            // feedback below it (the previous fixed height was the overflow culprit).
            float h = n > 0 ? n * answerButtonHeight + (n - 1) * answerSpacing + 4f : 0f;
            _answersLayout.minHeight = h;
            _answersLayout.preferredHeight = h;
        }

        // --- Primitive UI builders ------------------------------------------------------------------

        private Text CreateLabel(Transform parent, string text, int fontSize, FontStyle style,
                                 TextAnchor alignment, float height, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Text t = go.AddComponent<Text>();
            t.font = _font; t.text = text; t.fontSize = fontSize; t.fontStyle = style;
            t.alignment = alignment; t.color = color; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = height; le.preferredHeight = height;
            return t;
        }

        private Button CreateButton(Transform parent, string label, Action onClick,
                                    float fixedWidth = 0f, float height = 50f, int fontSize = -1)
        {
            var go = new GameObject(label + " Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            Image img = go.AddComponent<Image>();
            img.color = buttonNormal;

            Button btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = buttonNormal;
            colors.highlightedColor = buttonHover;
            colors.pressedColor = buttonPressed;
            colors.selectedColor = buttonHover;
            colors.colorMultiplier = 1f;
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick());

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = height; le.preferredHeight = height;
            if (fixedWidth > 0f) { le.minWidth = fixedWidth; le.preferredWidth = fixedWidth; }

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            RectTransform tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(4, 2); tr.offsetMax = new Vector2(-4, -2);
            Text t = textGO.AddComponent<Text>();
            t.font = _font; t.text = label; t.fontSize = fontSize > 0 ? fontSize : buttonFontSize;
            t.alignment = TextAnchor.MiddleCenter; t.color = textColor; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return btn;
        }

        // --- Helpers --------------------------------------------------------------------------------

        private static T CycleEnum<T>(T value, int dir) where T : Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            int i = Array.IndexOf(values, value);
            i = (i + dir + values.Length) % values.Length;
            return values[i];
        }

        // "BuildFromAxe" -> "Build From AXE" (AXE stays fully capitalised like the real notation).
        private static string ObjectiveLabel(LearningObjective objective)
        {
            var sb = new System.Text.StringBuilder();
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

// A shared, swappable "look" for the world-space UI panels (sandbox controls + game HUD).
//
// This is a ScriptableObject, so you author it as an .asset in the Project window
// (Create -> Molecule Shapes -> Panel Style), tweak every colour / font / edge in the Inspector, and
// drop the same asset onto both panels' "Style" slot - one place changes everything. Make several
// assets for different looks and swap them freely. If a panel has no style assigned, it falls back to
// a runtime instance of these defaults, so nothing ever breaks.

using UnityEngine;

namespace Molecule_Shapes.View
{
    [CreateAssetMenu(fileName = "PanelStyle", menuName = "Molecule Shapes/Panel Style")]
    public class PanelStyle : ScriptableObject
    {
        [Header("Font")]
        [Tooltip("Custom font (any imported .ttf/.otf). Leave empty to use Unity's built-in font.")]
        public Font font;

        [Header("Font sizes")]
        public int titleFontSize = 28;
        public int subtitleFontSize = 15;
        public int sectionFontSize = 20;
        public int valueFontSize = 20;
        public int promptFontSize = 22;
        public int infoFontSize = 18;
        public int bodyFontSize = 18;      // feedback, misc
        public int buttonFontSize = 18;

        [Header("Text colours")]
        public Color textColor = Color.white;
        public Color subtitleColor = new Color(0.62f, 0.70f, 0.80f, 1f);
        public Color goodColor = new Color(0.45f, 0.85f, 0.50f, 1f);
        public Color badColor = new Color(0.90f, 0.45f, 0.40f, 1f);

        [Header("Panel background & edges")]
        public Color panelBackground = new Color(0.05f, 0.07f, 0.10f, 0.92f);
        [Tooltip("Optional custom background sprite (9-sliced). Leave empty to auto-generate a rounded rect.")]
        public Sprite panelSprite;
        [Range(0, 80)] public int panelCornerRadius = 18;
        public Color panelBorderColor = new Color(1f, 1f, 1f, 0f);   // transparent = no border
        [Range(0, 20)] public int panelBorderWidth = 0;

        [Header("Buttons (shape & colours)")]
        public Color buttonNormal = new Color(0.20f, 0.25f, 0.32f, 1f);
        public Color buttonHover = new Color(0.30f, 0.40f, 0.55f, 1f);
        public Color buttonPressed = new Color(0.45f, 0.60f, 0.85f, 1f);
        public Color buttonText = Color.white;
        [Tooltip("Optional custom button sprite (9-sliced). Leave empty to auto-generate a rounded rect.")]
        public Sprite buttonSprite;
        [Range(0, 60)] public int buttonCornerRadius = 10;

        [Header("Hover feedback (readability at VR distance)")]
        [Tooltip("Grow and outline a button while it's pointed at, so the selected control is obvious " +
                 "from a distance where the colour tint alone is hard to read.")]
        public bool hoverFeedback = true;
        [Tooltip("How much a hovered button grows (1 = no growth).")]
        [Range(1f, 1.4f)] public float hoverScale = 1.08f;
        [Tooltip("Outline drawn around a hovered button. Set alpha to 0 to disable the outline.")]
        public Color hoverOutline = new Color(1f, 1f, 1f, 0.85f);
        [Range(0f, 12f)] public float hoverOutlineWidth = 3f;

        [Header("Spacing (UI units)")]
        public int padding = 20;
        public float rowSpacing = 8f;
        public float buttonSpacing = 6f;

        // Built-in font when none is assigned.
        public Font ResolvedFont => font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Runtime fallback carrying the field-initializer defaults above.
        public static PanelStyle CreateDefault() => CreateInstance<PanelStyle>();
    }
}

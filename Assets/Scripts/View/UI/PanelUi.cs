// Shared world-space uGUI builder used by both VR panels. Centralizes the "look" so a PanelStyle
// theme drives every widget, and provides a runtime rounded-rectangle sprite generator so panels and
// buttons get real rounded edges / borders without hand-authored sprite assets.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Molecule_Shapes.View
{
    public static class PanelUi
    {
        // Cache generated sprites so identical (radius, border, colours) reuse one texture.
        private static readonly Dictionary<int, Sprite> RoundedCache = new();

        // --- Widget builders ------------------------------------------------------------------------

        // Configures an Image as the panel background: custom sprite if provided, else a generated
        // rounded rect carrying the background + border colours.
        public static void StyleAsPanel(Image img, PanelStyle s)
        {
            if (s.panelSprite != null)
            {
                img.sprite = s.panelSprite;
                img.type = Image.Type.Sliced;
                img.color = s.panelBackground;
            }
            else if (s.panelCornerRadius > 0 || s.panelBorderWidth > 0)
            {
                img.sprite = RoundedSprite(s.panelCornerRadius, s.panelBorderWidth, s.panelBackground, s.panelBorderColor);
                img.type = Image.Type.Sliced;
                img.color = Color.white;   // colours are baked into the sprite
            }
            else
            {
                img.sprite = null;
                img.color = s.panelBackground;
            }
        }

        // A rounded (or custom) button whose fill is white so the Button ColorBlock can tint it through
        // normal/hover/pressed states.
        // height <= 0 means "don't impose a height" - the parent row/column then fully controls it.
        // Pass an explicit height only for column items (where the item's own height is the control).
        public static Button MakeButton(Transform parent, PanelStyle s, string label, Action onClick,
                                        int fontSize = 0, float height = 0f, float fixedWidth = 0f)
        {
            var go = new GameObject((string.IsNullOrEmpty(label) ? "Button" : label) + " Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            Image img = go.AddComponent<Image>();
            if (s.buttonSprite != null) { img.sprite = s.buttonSprite; img.type = Image.Type.Sliced; }
            else if (s.buttonCornerRadius > 0) { img.sprite = RoundedSprite(s.buttonCornerRadius, 0, Color.white, Color.clear); img.type = Image.Type.Sliced; }
            img.color = Color.white;

            Button btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = s.buttonNormal;
            colors.highlightedColor = s.buttonHover;
            colors.pressedColor = s.buttonPressed;
            colors.selectedColor = s.buttonHover;
            colors.disabledColor = s.buttonNormal;
            colors.colorMultiplier = 1f;
            btn.colors = colors;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());

            LayoutElement le = go.AddComponent<LayoutElement>();
            if (height > 0f) { le.minHeight = height; le.preferredHeight = height; }
            if (fixedWidth > 0f) { le.minWidth = fixedWidth; le.preferredWidth = fixedWidth; }

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            RectTransform tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(4, 2); tr.offsetMax = new Vector2(-4, -2);
            Text t = textGO.AddComponent<Text>();
            t.font = s.ResolvedFont; t.text = label; t.fontSize = fontSize > 0 ? fontSize : s.buttonFontSize;
            t.alignment = TextAnchor.MiddleCenter; t.color = s.buttonText; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return btn;
        }

        public static Text MakeLabel(Transform parent, PanelStyle s, string text, int fontSize,
                                     FontStyle style, TextAnchor alignment, Color color, float height,
                                     bool wrap = true)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Text t = go.AddComponent<Text>();
            t.font = s.ResolvedFont; t.text = text; t.fontSize = fontSize; t.fontStyle = style;
            t.alignment = alignment; t.color = color; t.raycastTarget = false;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            LayoutElement le = go.AddComponent<LayoutElement>();
            if (height > 0f) { le.minHeight = height; le.preferredHeight = height; }  // <=0 -> parent controls
            return t;
        }

        // A labelled checkbox row: [box] Caption. Left-aligned so boxes line up regardless of caption
        // length. onChanged fires on user toggles (not on the initial value). Returns the Toggle so the
        // caller can read/drive it later.
        public static Toggle MakeToggleRow(Transform parent, PanelStyle s, string caption, bool initial,
                                           Action<bool> onChanged, float rowHeight, float boxSize, Color captionColor)
        {
            Transform row = MakeRow(parent, rowHeight, s.buttonSpacing,
                                    forceExpandWidth: false, childAlignment: TextAnchor.MiddleLeft,
                                    forceExpandHeight: false);

            // Checkbox
            var boxGO = new GameObject("Checkbox", typeof(RectTransform));
            boxGO.transform.SetParent(row, false);
            Image box = boxGO.AddComponent<Image>();
            if (s.buttonCornerRadius > 0)
            {
                box.sprite = RoundedSprite(Mathf.Min(s.buttonCornerRadius, 8), 0, Color.white, Color.clear);
                box.type = Image.Type.Sliced;
            }
            box.color = s.buttonNormal;

            Toggle toggle = boxGO.AddComponent<Toggle>();
            toggle.targetGraphic = box;

            var checkGO = new GameObject("Check", typeof(RectTransform));
            checkGO.transform.SetParent(boxGO.transform, false);
            RectTransform crt = checkGO.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.22f, 0.22f); crt.anchorMax = new Vector2(0.78f, 0.78f);
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            Image checkImg = checkGO.AddComponent<Image>();
            checkImg.color = s.goodColor;
            toggle.graphic = checkImg;

            LayoutElement le = boxGO.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = boxSize;
            le.minHeight = le.preferredHeight = boxSize;

            MakeLabel(row, s, caption, s.bodyFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                      captionColor, rowHeight, wrap: false);

            toggle.SetIsOnWithoutNotify(initial);                 // don't fire during construction
            if (onChanged != null) toggle.onValueChanged.AddListener(v => onChanged(v));
            return toggle;
        }

        // A vertical layout column. If fillParent it stretches to the parent (inset by padding); else it
        // is content-sized (a LayoutElement, added by the caller, controls its height).
        public static Transform MakeColumn(Transform parent, PanelStyle s, float spacing, bool fillParent)
        {
            var go = new GameObject(fillParent ? "Column" : "SubColumn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (fillParent)
            {
                RectTransform r = go.GetComponent<RectTransform>();
                r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(s.padding, s.padding);
                r.offsetMax = new Vector2(-s.padding, -s.padding);
            }
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            // Control height from each child's LayoutElement so the height fields actually drive the
            // rendered heights (don't force-expand, so each keeps its own preferred height).
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            return go.transform;
        }

        // forceExpandWidth: true = children share the row width equally (Start/Stop, New/Hint). false =
        // children keep their own preferred/fixed widths (so a LayoutElement width or a flexibleWidth on
        // one child actually takes effect - needed for the ◀ value ▶ selector and the 1-6 preset row).
        public static Transform MakeRow(Transform parent, float height, float spacing,
                                        bool forceExpandWidth = true,
                                        TextAnchor childAlignment = TextAnchor.MiddleCenter,
                                        bool forceExpandHeight = true)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = childAlignment;   // matters when a force-expand is off
            layout.childControlWidth = true;
            layout.childForceExpandWidth = forceExpandWidth;
            layout.childControlHeight = true;
            // forceExpandHeight false lets a child keep its own (smaller) height, e.g. a checkbox in a
            // taller text row.
            layout.childForceExpandHeight = forceExpandHeight;
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = height; le.preferredHeight = height;
            return go.transform;
        }

        // A zero-content element that soaks up all leftover vertical space in a column, so every real
        // element stays at exactly its set height instead of the column stretching them to fill. Add it
        // as the LAST child of a column.
        public static void AddFlexibleSpacer(Transform column)
        {
            var go = new GameObject("Spacer", typeof(RectTransform));
            go.transform.SetParent(column, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = 0f;
            le.flexibleHeight = 1f;   // takes all the slack
        }

        // Forces the layout under `root` to recompute immediately (runtime-built world-space canvases
        // sometimes don't rebuild on their own after a programmatic rebuild).
        public static void ForceRebuild(Transform root)
        {
            if (root is RectTransform rt) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }

        // --- 3D helpers (grab handle) ---------------------------------------------------------------

        // A flat opaque material from a URP shader (Unlit), for the 3D grab-handle bar. Requires
        // URP/Unlit in the build (a material asset in a Resources folder guarantees this).
        public static Material SolidMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        // Adds a grab bar across the top of a panel. It's a thin box (with the primitive's BoxCollider,
        // which PanelGrabController raycasts against) parented to the panel root, plus a PanelHandle
        // marker pointing back at that root. Returns the handle GameObject.
        public static GameObject AddHandle(Transform panelRoot, Vector2 panelSizeMeters, float thicknessMeters,
                                           Color color, float widthFraction = 1f)
        {
            var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handle.name = "Panel Handle";
            handle.transform.SetParent(panelRoot, false);
            float width = panelSizeMeters.x * Mathf.Clamp(widthFraction, 0.05f, 1f);
            float depth = Mathf.Max(0.006f, thicknessMeters * 0.6f);
            handle.transform.localScale = new Vector3(width, thicknessMeters, depth);
            handle.transform.localPosition = new Vector3(0f, panelSizeMeters.y * 0.5f + thicknessMeters * 0.5f + 0.004f, 0f);
            handle.transform.localRotation = Quaternion.identity;

            handle.GetComponent<MeshRenderer>().material = SolidMaterial(color);   // keep the BoxCollider
            handle.AddComponent<PanelHandle>().PanelRoot = panelRoot;
            return handle;
        }

        // --- Rounded-rect sprite generator ----------------------------------------------------------

        // Generates a 9-sliced rounded-rectangle sprite so it scales to any size with crisp corners.
        // `fill` is the interior colour; the outer `border` ring (if width > 0 and colour visible) is
        // drawn in `borderColor`. Cached by its parameters.
        public static Sprite RoundedSprite(int radius, int border, Color fill, Color borderColor)
        {
            radius = Mathf.Max(1, radius);
            border = Mathf.Max(0, border);
            int key = HashKey(radius, border, fill, borderColor);
            if (RoundedCache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

            int pad = 1;                       // 2px stretchable centre for the 9-slice
            int size = radius * 2 + pad * 2;
            float half = size / 2f;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var px = new Color[size * size];
            bool hasBorder = border > 0 && borderColor.a > 0f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    // Signed distance to a rounded rect (half extent = half, corner radius = radius).
                    float qx = Mathf.Abs(dx) - (half - radius);
                    float qy = Mathf.Abs(dy) - (half - radius);
                    float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                                               Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                                    + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;

                    float alpha = Mathf.Clamp01(0.5f - outside);   // 1px anti-aliased edge
                    Color c = hasBorder && (-outside) < border ? borderColor : fill;
                    c.a *= alpha;
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            sprite.name = $"Rounded_{radius}_{border}";
            RoundedCache[key] = sprite;
            return sprite;
        }

        private static int HashKey(int radius, int border, Color a, Color b)
        {
            unchecked
            {
                int h = radius * 397 ^ border;
                h = h * 397 ^ ((Color32)a).GetHashCode();
                h = h * 397 ^ ((Color32)b).GetHashCode();
                return h;
            }
        }
    }
}

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
            colors.disabledColor = s.buttonDisabled;   // visibly dark when the button would do nothing
            colors.colorMultiplier = 1f;
            btn.colors = colors;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());

            LayoutElement le = go.AddComponent<LayoutElement>();
            if (height > 0f) { le.minHeight = height; le.preferredHeight = height; }
            if (fixedWidth > 0f) { le.minWidth = fixedWidth; le.preferredWidth = fixedWidth; }

            // Grow + outline on hover so the pointed-at control is legible from across the room.
            if (s.hoverFeedback)
            {
                HoverScale hover = go.AddComponent<HoverScale>();
                hover.HoverScaleFactor = s.hoverScale;
                hover.OutlineColor = s.hoverOutline;
                hover.OutlineWidth = s.hoverOutlineWidth;
            }

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

        // A "◀ value ▶" stepper row. The value keeps a fixed width and best-fits (wrapping + shrinking to
        // minFontSize) so long labels stay inside their box instead of drawing over the arrows.
        // Returns the value Text so the caller can update it.
        public static Text MakeSelectorRow(Transform parent, PanelStyle s, float height, float arrowWidth,
                                           float valueWidth, int minFontSize, Action onPrev, Action onNext)
        {
            Transform row = MakeRow(parent, height, s.buttonSpacing, forceExpandWidth: false);
            MakeButton(row, s, "◀", onPrev, fixedWidth: arrowWidth);

            Text label = MakeLabel(row, s, "", s.valueFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                                   s.textColor, 0f, wrap: true);
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(1, minFontSize);
            label.resizeTextMaxSize = s.valueFontSize;

            LayoutElement le = label.GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = valueWidth;
            le.flexibleWidth = 0f;

            MakeButton(row, s, "▶", onNext, fixedWidth: arrowWidth);
            return label;
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

        // An alpha-blended material from a URP shader, for particles / translucent 3D bits. Alpha blend
        // is set on the colour channel only (One/One on alpha) so it never punches a hole in the
        // framebuffer alpha - otherwise passthrough would bleed through it against a solid VR background.
        public static Material TransparentMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_QueueControl")) mat.SetFloat("_QueueControl", 1f);
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3f);

            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        // Material for particle systems. MUST be a *Particles* shader: the plain URP/Unlit shader ignores
        // vertex colours, and a ParticleSystem passes each particle's start colour as a vertex colour -
        // so with Unlit every particle renders white regardless of the gradient you set.
        // Keep "Universal Render Pipeline/Particles/Unlit" in device builds by putting a material that
        // uses it in a Resources folder (same trick as KeepUrpLit / KeepUrpUnlit).
        public static Material ParticleMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
            if (shader == null) shader = Shader.Find("Sprites/Default");        // built-in, also vertex-coloured
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");   // last resort (white)

            var mat = new Material(shader);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_QueueControl")) mat.SetFloat("_QueueControl", 1f);

            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // White base so the per-particle colour comes through unmodulated.
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
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

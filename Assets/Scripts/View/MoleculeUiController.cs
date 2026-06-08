// Bottom-of-screen IMGUI button row. Wires to the public methods already exposed by
// MoleculeController and BondAngleOverlay - no behavior lives here, just dispatch.
//
// IMGUI was chosen over uGUI for consistency with BondAngleOverlay's labels/HUD, zero scene
// authoring, and no TextMeshPro / EventSystem dependencies. The eventual VR port will replace
// this with a world-space uGUI canvas.

using UnityEngine;

namespace Molecule_Shapes.View
{
    [RequireComponent(typeof(MoleculeController))]
    public class MoleculeUiController : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private float panelPadding = 12f;
        [SerializeField] private float buttonHeight = 36f;
        [SerializeField] private float rowSpacing = 6f;

        [Header("Style")]
        [SerializeField] private int buttonFontSize = 15;
        [SerializeField] private int labelFontSize = 14;

        private MoleculeController _controller;
        private BondAngleOverlay _overlay;
        private GUIStyle _buttonStyle;
        private GUIStyle _labelStyle;

        private void Awake()
        {
            _controller = GetComponent<MoleculeController>();
            _overlay = GetComponent<BondAngleOverlay>(); // optional
        }

        private void OnGUI()
        {
            if (_controller == null) return;
            EnsureStyles();

            // Two-row panel anchored to the bottom of the screen.
            float panelHeight = buttonHeight * 2 + rowSpacing + panelPadding * 2;
            var panelRect = new Rect(
                panelPadding,
                Screen.height - panelHeight - panelPadding,
                Screen.width - panelPadding * 2,
                panelHeight);

            GUI.Box(panelRect, GUIContent.none);

            var innerRect = new Rect(
                panelRect.x + panelPadding,
                panelRect.y + panelPadding,
                panelRect.width - panelPadding * 2,
                panelRect.height - panelPadding * 2);

            GUILayout.BeginArea(innerRect);

            DrawPresetRow();
            GUILayout.Space(rowSpacing);
            DrawActionRow();

            GUILayout.EndArea();
        }

        private void DrawPresetRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            GUILayout.Label("Set bonds:", _labelStyle, GUILayout.Height(buttonHeight));

            for (int n = 1; n <= 6; n++)
            {
                if (GUILayout.Button(n.ToString(), _buttonStyle, GUILayout.Width(40), GUILayout.Height(buttonHeight)))
                {
                    _controller.SetBondedAtomCount(n);
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawActionRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("+ Atom", _buttonStyle, GUILayout.Width(80), GUILayout.Height(buttonHeight)))
                _controller.AddBondedAtom();
            if (GUILayout.Button("− Atom", _buttonStyle, GUILayout.Width(80), GUILayout.Height(buttonHeight)))
                _controller.RemoveLastAtom();

            GUILayout.Space(12);

            if (GUILayout.Button("+ Lone Pair", _buttonStyle, GUILayout.Width(110), GUILayout.Height(buttonHeight)))
                _controller.AddLonePair();
            if (GUILayout.Button("− Lone Pair", _buttonStyle, GUILayout.Width(110), GUILayout.Height(buttonHeight)))
                _controller.RemoveLastLonePair();

            GUILayout.Space(12);

            if (GUILayout.Button("Reset", _buttonStyle, GUILayout.Width(80), GUILayout.Height(buttonHeight)))
                _controller.ResetMolecule();

            if (_overlay != null)
            {
                if (GUILayout.Button("Toggle Angles", _buttonStyle, GUILayout.Width(120), GUILayout.Height(buttonHeight)))
                    _overlay.ToggleVisible();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void EnsureStyles()
        {
            if (_buttonStyle == null)
            {
                _buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = buttonFontSize,
                    alignment = TextAnchor.MiddleCenter
                };
            }
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = labelFontSize,
                    alignment = TextAnchor.MiddleLeft
                };
            }
        }
    }
}

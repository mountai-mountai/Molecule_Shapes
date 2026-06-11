// Port of PhET's BondAngleView (js/common/view/3d/BondAngleView.js).
//
// Three behaviors taken from the reference:
//   1. Filled translucent "pie slice" sector in the arc plane (not just a line).
//   2. Camera-orientation fade: brightness = |cross(a,b) . cameraDir|, with bond-count thresholds.
//      Arcs whose plane is edge-on to the camera fade out; broadside arcs are fully visible.
//   3. 180-degree stabilization: when two bonds are antiparallel, the bisector is undefined and
//      the sector would flip wildly. We use the previous frame's midpoint to pick a stable plane.

using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Molecule_Shapes.Model;

namespace Molecule_Shapes.View
{
    [RequireComponent(typeof(MoleculeController))]
    public class BondAngleOverlay : MonoBehaviour
    {
        // Thresholds copied from PhET BondAngleView (indexed by # of radial atoms).
        //   brightness < low  -> alpha 0
        //   brightness > high -> alpha 1
        //   linearly interpolated between.
        private static readonly float[] LowThresholds  = { 0f, 0f, 0f, 0.10f, 0.35f, 0.45f, 0.50f };
        private static readonly float[] HighThresholds = { 0f, 0f, 0f, 0.50f, 0.55f, 0.65f, 0.75f };

        // Match PhET's "approximate semicircle" threshold (~178.96 deg).
        private const float SemicircleAngleDeg = 178.96f;

        [Header("Sector appearance")]
        [Tooltip("Outer radius of the angle sector (model units).")]
        [SerializeField] private float displayRadius = 4f;
        [Tooltip("Inner radius - set just outside the central atom's visible radius so the sector doesn't intersect it.")]
        [SerializeField] private float innerRadius = 1.5f;
        [SerializeField, Range(8, 64)] private int sectorSegments = 24;
        [SerializeField] private Color sectorColor = new Color(1f, 0.9f, 0.3f, 0.55f);

        [Header("Arc border")]
        [SerializeField] private bool showArcBorder = true;
        [SerializeField] private Color arcBorderColor = new Color(1f, 0.85f, 0.2f, 1f);
        [SerializeField] private float arcBorderWidth = 0.12f;

        [Header("Labels (OnGUI - desktop)")]
        [SerializeField] private bool showLabels = true;
        [SerializeField] private int labelFontSize = 14;
        [SerializeField, Range(0f, 1f)] private float minOpacityForLabel = 0.15f;
        [SerializeField] private Color labelColor = Color.white;

        [Header("Labels (world-space - VR)")]
        [Tooltip("Render 3D TextMesh labels at each arc midpoint. Use this in VR (OnGUI labels don't show in headsets).")]
        [SerializeField] private bool showWorldSpaceLabels = false;
        [Tooltip("Character size of world-space labels (meters in world units, before any parent scale).")]
        [SerializeField] private float worldLabelCharacterSize = 0.012f;
        [SerializeField] private int worldLabelFontSize = 80;

        [Header("Geometry name (top-left OnGUI)")]
        [SerializeField] private bool showGeometryName = true;
        [SerializeField] private int geometryFontSize = 22;

        private MoleculeController _controller;
        private Camera _camera;
        private readonly List<SectorView> _pool = new();
        private readonly List<(Vector3 worldMid, float angleDeg, float opacity)> _labels = new();

        // Stabilization data for 180-degree pairs, keyed by sorted (atomIdA, atomIdB).
        private readonly Dictionary<long, Vector3> _lastMidpoints = new();

        private bool _visible = true;
        private GUIStyle _labelStyle;
        private GUIStyle _geometryStyle;

        // World-space label pool; siblings of the molecule (not parented) so they don't inherit its scale.
        private readonly List<TextMesh> _worldLabelPool = new();
        private Font _worldLabelFont;

        private class SectorView
        {
            public GameObject Go;
            public Mesh Mesh;
            public Material Material;
            public LineRenderer Line;
            public Material LineMaterial;
        }

        // Reused buffer for arc directions (unit vectors); mesh/line scale them per pass.
        private Vector3[] _arcDirs;

        private void Awake()
        {
            _controller = GetComponent<MoleculeController>();
            _camera = Camera.main;
        }

        // No keyboard handler here. The 'A' key was removed because the XR Device Simulator uses A
        // for strafing left; the angle toggle lives on the UI panel's "Toggle Angles" button via
        // ToggleVisible(), which both the desktop UI and (later) a VR UI button can drive.

        public void ToggleVisible()
        {
            _visible = !_visible;
            if (!_visible)
            {
                for (int i = 0; i < _pool.Count; i++)
                    if (_pool[i] != null) _pool[i].Go.SetActive(false);
                for (int i = 0; i < _worldLabelPool.Count; i++)
                    if (_worldLabelPool[i] != null) _worldLabelPool[i].gameObject.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            _labels.Clear();
            if (!_visible || _controller == null || _controller.Molecule == null) return;

            // Re-resolve every frame. With XR (Device Simulator or a real headset), the active camera
            // can change after Awake when an XR tracked camera takes over Camera.main, leaving a
            // cached reference pointing at a disabled/zeroed camera.
            if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
            if (_camera == null) return;

            VsepRMolecule molecule = _controller.Molecule;
            var atoms = molecule.RadialAtoms;
            int bondCount = atoms.Count;

            // Camera direction in the molecule's LOCAL frame (so it tracks correctly when the molecule is rotated).
            Vector3 cameraLocal = transform.InverseTransformPoint(_camera.transform.position);
            Vector3 localCameraDir = cameraLocal.sqrMagnitude > 1e-6f ? cameraLocal.normalized : Vector3.back;

            int pairIndex = 0;

            for (int i = 0; i < atoms.Count; i++)
            {
                for (int j = i + 1; j < atoms.Count; j++)
                {
                    PairGroup A = atoms[i];
                    PairGroup B = atoms[j];

                    Vector3 a = SafeNormalize(ToVector3(A.Position));
                    Vector3 b = SafeNormalize(ToVector3(B.Position));

                    float opacity = CalculateBrightness(a, b, localCameraDir, bondCount);
                    long key = PairKey(A.Id, B.Id);

                    if (opacity <= 0.001f)
                    {
                        DisableSector(pairIndex);
                        pairIndex++;
                        continue;
                    }

                    // Build the orthonormal basis (midpointUnit, planarUnit) spanning the arc plane.
                    Vector3 midpointUnit, planarUnit;
                    bool semicircle = Vector3.Angle(a, b) >= SemicircleAngleDeg;

                    if (semicircle)
                    {
                        // 180-degree stabilization (PhET): use the last frame's midpoint to pick a stable plane.
                        Vector3 lastMid = _lastMidpoints.TryGetValue(key, out var lm) ? lm : Vector3.up * displayRadius;
                        Vector3 lastMidDir = SafeNormalize(lastMid);

                        Vector3 badCross = Vector3.Cross(a, lastMidDir) + Vector3.Cross(lastMidDir, b);
                        Vector3 averageCross = badCross.sqrMagnitude > 0f ? badCross.normalized : Vector3.forward;

                        Vector3 averagePoint = SafeNormalize(a - b);
                        planarUnit = SafeNormalize(averagePoint - averageCross * Vector3.Dot(averageCross, averagePoint));
                        midpointUnit = SafeNormalize(Vector3.Cross(averageCross, planarUnit));
                    }
                    else
                    {
                        midpointUnit = SafeNormalize(a + b);
                        Vector3 planar = a - midpointUnit * Vector3.Dot(a, midpointUnit);
                        planarUnit = planar.sqrMagnitude > 0f ? planar.normalized : Vector3.right;
                    }

                    float angleRad = Mathf.Acos(Mathf.Clamp(Vector3.Dot(a, b), -1f, 1f));
                    float halfAngle = angleRad * 0.5f;
                    float angleDeg = angleRad * Mathf.Rad2Deg;

                    SectorView sv = GetOrCreateSector(pairIndex);
                    sv.Go.SetActive(true);
                    BuildArc(midpointUnit, planarUnit, halfAngle);
                    PopulateSectorMesh(sv.Mesh);
                    PopulateLineRenderer(sv.Line);
                    SetSectorColor(sv.Material, sectorColor, opacity);
                    SetArcBorderColor(sv.LineMaterial, sv.Line, arcBorderColor, opacity);

                    Vector3 midLocal = midpointUnit * displayRadius;
                    _lastMidpoints[key] = midLocal;
                    _labels.Add((transform.TransformPoint(midLocal), angleDeg, opacity));

                    pairIndex++;
                }
            }

            // Disable any leftover sectors from a previous higher pair count.
            for (int k = pairIndex; k < _pool.Count; k++)
                if (_pool[k] != null) _pool[k].Go.SetActive(false);

            UpdateWorldSpaceLabels();
        }

        private void UpdateWorldSpaceLabels()
        {
            if (!showWorldSpaceLabels)
            {
                for (int i = 0; i < _worldLabelPool.Count; i++)
                    if (_worldLabelPool[i] != null) _worldLabelPool[i].gameObject.SetActive(false);
                return;
            }

            for (int i = 0; i < _labels.Count; i++)
            {
                var lbl = _labels[i];
                TextMesh tm = GetOrCreateWorldLabel(i);
                tm.gameObject.SetActive(lbl.opacity >= minOpacityForLabel);
                if (!tm.gameObject.activeSelf) continue;

                tm.transform.position = lbl.worldMid;

                // Billboard toward camera so the text always reads facing the user.
                if (_camera != null)
                {
                    Vector3 toCam = _camera.transform.position - tm.transform.position;
                    if (toCam.sqrMagnitude > 1e-6f)
                    {
                        // LookRotation sets local +Z to point at the target; TextMesh renders on +Z side.
                        // We want the camera to be on +Z, so look FROM the label TOWARD the camera.
                        tm.transform.rotation = Quaternion.LookRotation(-toCam, Vector3.up);
                    }
                }

                tm.text = $"{lbl.angleDeg:F1}°";

                Color c = labelColor;
                c.a = labelColor.a * Mathf.Clamp01(lbl.opacity);
                tm.color = c;
            }

            // Hide any pooled labels not used this frame.
            for (int i = _labels.Count; i < _worldLabelPool.Count; i++)
                if (_worldLabelPool[i] != null) _worldLabelPool[i].gameObject.SetActive(false);
        }

        private TextMesh GetOrCreateWorldLabel(int index)
        {
            while (_worldLabelPool.Count <= index)
            {
                if (_worldLabelFont == null) _worldLabelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                var go = new GameObject($"AngleLabel_{_worldLabelPool.Count}");
                // No parent - keeps the label at world scale regardless of the molecule's transform scale.

                TextMesh tm = go.AddComponent<TextMesh>();
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.fontSize = worldLabelFontSize;
                tm.characterSize = worldLabelCharacterSize;
                tm.color = labelColor;
                tm.font = _worldLabelFont;
                if (_worldLabelFont != null) go.GetComponent<MeshRenderer>().material = _worldLabelFont.material;

                _worldLabelPool.Add(tm);
            }
            return _worldLabelPool[index];
        }

        private void DisableSector(int index)
        {
            if (index < _pool.Count && _pool[index] != null) _pool[index].Go.SetActive(false);
        }

        // PhET's brightness formula, with thresholds by bondCount.
        private static float CalculateBrightness(Vector3 a, Vector3 b, Vector3 cameraDir, int bondCount)
        {
            if (bondCount <= 2) return 1f;
            float brightness = Mathf.Abs(Vector3.Dot(Vector3.Cross(a, b), cameraDir));
            int idx = Mathf.Clamp(bondCount, 0, LowThresholds.Length - 1);
            float low = LowThresholds[idx];
            float high = HighThresholds[idx];
            if (high <= low) return brightness > low ? 1f : 0f;
            return Mathf.Clamp01((brightness - low) / (high - low));
        }

        private static long PairKey(int idA, int idB)
        {
            int lo = idA < idB ? idA : idB;
            int hi = idA < idB ? idB : idA;
            return ((long)lo << 32) | (uint)hi;
        }

        // Compute unit direction vectors along the arc once; mesh and line scale them independently
        // (annular sector mesh uses inner + outer; the LineRenderer uses outer only).
        private void BuildArc(Vector3 midUnit, Vector3 planarUnit, float halfAngle)
        {
            int n = sectorSegments;
            if (_arcDirs == null || _arcDirs.Length != n + 1) _arcDirs = new Vector3[n + 1];
            for (int s = 0; s <= n; s++)
            {
                float t = (s / (float)n) * 2f - 1f; // -1..+1
                float angle = t * halfAngle;
                _arcDirs[s] = Mathf.Cos(angle) * midUnit + Mathf.Sin(angle) * planarUnit;
            }
        }

        // Annular sector: a strip of quads between an inner arc and an outer arc, so the sector
        // never intersects the central atom and has no straight radial edges visible inside bonds.
        private void PopulateSectorMesh(Mesh mesh)
        {
            int n = sectorSegments;
            float rInner = Mathf.Max(0f, innerRadius);
            float rOuter = Mathf.Max(rInner + 0.001f, displayRadius);

            var verts = new Vector3[2 * (n + 1)];
            for (int s = 0; s <= n; s++)
            {
                verts[s]             = _arcDirs[s] * rInner;        // inner ring
                verts[(n + 1) + s]   = _arcDirs[s] * rOuter;        // outer ring
            }

            // Two triangles per segment: (inner_s, outer_s, outer_{s+1}) and (inner_s, outer_{s+1}, inner_{s+1}).
            var tris = new int[6 * n];
            for (int s = 0; s < n; s++)
            {
                int innerS  = s;
                int outerS  = (n + 1) + s;
                int innerS1 = s + 1;
                int outerS1 = (n + 1) + s + 1;

                int i = s * 6;
                tris[i + 0] = innerS;
                tris[i + 1] = outerS;
                tris[i + 2] = outerS1;
                tris[i + 3] = innerS;
                tris[i + 4] = outerS1;
                tris[i + 5] = innerS1;
            }

            mesh.Clear();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        private void PopulateLineRenderer(LineRenderer lr)
        {
            lr.enabled = showArcBorder;
            if (!showArcBorder) return;

            int n = sectorSegments;
            lr.positionCount = n + 1;
            for (int s = 0; s <= n; s++) lr.SetPosition(s, _arcDirs[s] * displayRadius);

            // LineRenderer widths are in WORLD units and are NOT scaled by the transform's local scale,
            // unlike the sector mesh (which is in local space and scales with the parent). Compensate
            // here so arcBorderWidth means "thickness relative to the molecule" regardless of whether
            // the parent transform is at desktop scale 1 or VR scale 0.03.
            float worldScale = Mathf.Max(1e-4f, transform.lossyScale.x);
            float effectiveWidth = arcBorderWidth * worldScale;
            lr.startWidth = effectiveWidth;
            lr.endWidth = effectiveWidth;
        }

        private SectorView GetOrCreateSector(int index)
        {
            while (_pool.Count <= index)
            {
                var go = new GameObject($"BondAngleSector_{_pool.Count}");
                go.transform.SetParent(transform, worldPositionStays: false);

                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                var mesh = new Mesh { name = "BondAngleSectorMesh" };
                mesh.MarkDynamic();
                mf.mesh = mesh;

                Material mat = CreateTranslucentDoubleSided();
                mr.material = mat;

                // LineRenderer on the same GameObject so it inherits the local transform of the sector.
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                // numCapVertices > 0 creates a closed polygonal "lid" at each end of the line which,
                // viewed end-on, looks like a hexagon at each arc terminus. The arc is short and
                // radial; no decorative cap needed.
                lr.numCapVertices = 0;
                lr.alignment = LineAlignment.View;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                Material lineMat = CreateLineMaterial();
                lr.material = lineMat;

                _pool.Add(new SectorView { Go = go, Mesh = mesh, Material = mat, Line = lr, LineMaterial = lineMat });
            }
            return _pool[index];
        }

        // URP/Unlit instead of Sprites/Default. Sprites/Default is a built-in-pipeline shader and
        // doesn't ship a Vulkan/Android variant; on URP+Android targets it falls back to the error
        // shader (renders as red/magenta), which is what produced the "red circle" rim.
        private static Material CreateLineMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader);

            // Same alpha-blend transparent setup the sector uses, so the rim can fade with it.
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_QueueControl")) mat.SetFloat("_QueueControl", 1f);

            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 1; // sit just above the sector
            return mat;
        }

        private static Material CreateTranslucentDoubleSided()
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
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);   // both sides
            if (mat.HasProperty("_QueueControl")) mat.SetFloat("_QueueControl", 1f);
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3f); // built-in Standard fallback

            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }

        private static void SetSectorColor(Material mat, Color baseColor, float opacity)
        {
            Color c = baseColor;
            c.a = baseColor.a * Mathf.Clamp01(opacity);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }

        private static void SetArcBorderColor(Material mat, LineRenderer lr, Color baseColor, float opacity)
        {
            Color c = baseColor;
            c.a = baseColor.a * Mathf.Clamp01(opacity);

            // LineRenderer multiplies start/end colors with the material color, so set both.
            if (mat != null)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            }
            if (lr != null)
            {
                lr.startColor = c;
                lr.endColor = c;
            }
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (showGeometryName) DrawGeometryName();

            if (!_visible || !showLabels || _camera == null) return;

            for (int i = 0; i < _labels.Count; i++)
            {
                var lbl = _labels[i];
                if (lbl.opacity < minOpacityForLabel) continue;

                Vector3 screen = _camera.WorldToScreenPoint(lbl.worldMid);
                if (screen.z <= 0f) continue;

                Color c = labelColor;
                c.a = labelColor.a * Mathf.Clamp01(lbl.opacity);
                Color old = _labelStyle.normal.textColor;
                _labelStyle.normal.textColor = c;

                var rect = new Rect(screen.x - 30f, Screen.height - screen.y - 10f, 60f, 20f);
                GUI.Label(rect, $"{lbl.angleDeg:F1}°", _labelStyle);

                _labelStyle.normal.textColor = old;
            }
        }

        private void DrawGeometryName()
        {
            if (_controller == null || _controller.Molecule == null) return;

            VsepRMolecule molecule = _controller.Molecule;
            int x = molecule.RadialAtoms.Count;
            int e = molecule.RadialLonePairs.Count;

            string geometryName;
            try { geometryName = MoleculeGeometry.GetConfiguration(x, e).DisplayName; }
            catch { geometryName = "-"; }
            if (string.IsNullOrEmpty(geometryName)) geometryName = "-";

            string axe = $"AX{x}" + (e > 0 ? $"E{e}" : "");
            var rect = new Rect(12f, 10f, 500f, 60f);
            GUI.Label(rect, $"{axe}: {geometryName}", _geometryStyle);
        }

        private void EnsureStyles()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = labelFontSize,
                    alignment = TextAnchor.MiddleCenter
                };
                _labelStyle.normal.textColor = labelColor;
            }
            if (_geometryStyle == null)
            {
                _geometryStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = geometryFontSize,
                    fontStyle = FontStyle.Bold
                };
                _geometryStyle.normal.textColor = labelColor;
            }
        }

        private static Vector3 ToVector3(float3 v) => new Vector3(v.x, v.y, v.z);
        private static Vector3 SafeNormalize(Vector3 v) => v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.right;
    }
}

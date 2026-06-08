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
using UnityEngine.InputSystem;
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
        [SerializeField] private float displayRadius = 4f;
        [SerializeField, Range(8, 64)] private int sectorSegments = 24;
        [SerializeField] private Color sectorColor = new Color(1f, 0.9f, 0.3f, 0.55f);

        [Header("Arc border")]
        [SerializeField] private bool showArcBorder = true;
        [SerializeField] private Color arcBorderColor = new Color(1f, 0.85f, 0.2f, 1f);
        [SerializeField] private float arcBorderWidth = 0.12f;

        [Header("Labels (OnGUI)")]
        [SerializeField] private bool showLabels = true;
        [SerializeField] private int labelFontSize = 14;
        [SerializeField, Range(0f, 1f)] private float minOpacityForLabel = 0.15f;
        [SerializeField] private Color labelColor = Color.white;

        [Header("Geometry name (top-left)")]
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

        private class SectorView
        {
            public GameObject Go;
            public Mesh Mesh;
            public Material Material;
            public LineRenderer Line;
            public Material LineMaterial;
        }

        // Reused buffer for arc points; resized on demand.
        private Vector3[] _arcPoints;

        private void Awake()
        {
            _controller = GetComponent<MoleculeController>();
            _camera = Camera.main;
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.aKey.wasPressedThisFrame) ToggleVisible();
        }

        public void ToggleVisible()
        {
            _visible = !_visible;
            if (!_visible)
            {
                for (int i = 0; i < _pool.Count; i++)
                    if (_pool[i] != null) _pool[i].Go.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            _labels.Clear();
            if (!_visible || _controller == null || _controller.Molecule == null || _camera == null) return;

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
                    BuildArc(midpointUnit, planarUnit, halfAngle, displayRadius);
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

        // Compute the arc points (in local space) once; mesh and line both consume them.
        private void BuildArc(Vector3 midUnit, Vector3 planarUnit, float halfAngle, float radius)
        {
            int n = sectorSegments;
            if (_arcPoints == null || _arcPoints.Length != n + 1) _arcPoints = new Vector3[n + 1];
            for (int s = 0; s <= n; s++)
            {
                float t = (s / (float)n) * 2f - 1f; // -1..+1
                float angle = t * halfAngle;
                Vector3 dir = Mathf.Cos(angle) * midUnit + Mathf.Sin(angle) * planarUnit;
                _arcPoints[s] = dir * radius;
            }
        }

        private void PopulateSectorMesh(Mesh mesh)
        {
            int n = sectorSegments;
            var verts = new Vector3[n + 2];
            verts[0] = Vector3.zero;
            for (int s = 0; s <= n; s++) verts[s + 1] = _arcPoints[s];

            var tris = new int[n * 3];
            for (int s = 0; s < n; s++)
            {
                tris[s * 3 + 0] = 0;
                tris[s * 3 + 1] = s + 1;
                tris[s * 3 + 2] = s + 2;
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
            for (int s = 0; s <= n; s++) lr.SetPosition(s, _arcPoints[s]);
            lr.startWidth = arcBorderWidth;
            lr.endWidth = arcBorderWidth;
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
                lr.numCapVertices = 2;
                lr.alignment = LineAlignment.View;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                Material lineMat = CreateLineMaterial();
                lr.material = lineMat;

                _pool.Add(new SectorView { Go = go, Mesh = mesh, Material = mat, Line = lr, LineMaterial = lineMat });
            }
            return _pool[index];
        }

        private static Material CreateLineMaterial()
        {
            // Sprites/Default supports vertex color alpha and renders LineRenderer reliably in URP.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 1; // draw on top of sector
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

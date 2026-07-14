using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Mathematics;
using Molecule_Shapes.Model;
// Aliased so calls like ModelModelMolecule.MaxConnections aren't shadowed by this class's `Molecule` property.
using ModelMolecule = Molecule_Shapes.Model.Molecule;

namespace Molecule_Shapes.View
{
    // The single MonoBehaviour that drives the model and syncs GameObjects to it.
    // Generates sphere/cylinder primitives in code (no prefabs needed for this first test).
    public class MoleculeController : MonoBehaviour
    {
        [Header("Initial molecule")]
        [Range(0, 6)]
        [SerializeField] private int initialAtomCount = 4;

        [Header("Appearance (model units)")]
        [SerializeField] private float atomDiameter = 2.5f;
        [SerializeField] private float bondThickness = 1.0f;
        [SerializeField] private Color centralColor = new Color(0.85f, 0.30f, 0.20f);
        [SerializeField] private Color radialColor = new Color(0.30f, 0.50f, 0.85f);
        [SerializeField] private Color bondColor = new Color(0.7f, 0.7f, 0.7f);

        [Header("Lone pairs")]
        [SerializeField] private Color lonePairColor = new Color(0.6f, 0.5f, 0.9f, 0.45f);
        [SerializeField] private float lonePairDiameter = 2.0f;
        [SerializeField] private float lonePairElongation = 1.6f;
        [Tooltip("Optional custom lone-pair mesh (e.g. PhET's balloon2, tip at the origin). Empty = the " +
                 "elongated sphere fallback.")]
        [SerializeField] private Mesh lonePairMesh;
        [Tooltip("Uniform scale for the custom mesh (PhET uses ~2.5). Ignored for the sphere fallback.")]
        [SerializeField] private float lonePairMeshScale = 2.5f;
        [Tooltip("How far the balloon is pulled back toward the central atom so its tip hides inside it. " +
                 "~7 (the lone-pair distance) puts the tip at the atom centre; lower pushes it outward.")]
        [SerializeField] private float lonePairPullback = 7f;

        [Header("Lone pair electrons (optional dots)")]
        [Tooltip("Show two small electron spheres on each lone pair, like PhET.")]
        [SerializeField] private bool showLonePairElectrons = false;
        [Tooltip("Electron colour (its alpha is overridden by Electron Opacity below).")]
        [SerializeField] private Color electronColor = new Color(0.12f, 0.12f, 0.18f, 0.85f);
        [Tooltip("Electron dot transparency: 0 = fully see-through/invisible, 1 = solid. A simple slider " +
                 "so you don't have to open the colour picker to tune it.")]
        [Range(0f, 1f)]
        [SerializeField] private float electronOpacity = 0.85f;
        [SerializeField] private float electronRadius = 0.25f;
        [Tooltip("Sideways spread of the two electrons (perpendicular to the radial axis).")]
        [SerializeField] private float electronPerp = 0.75f;
        [Tooltip("Distance out along the axis where the electrons sit.")]
        [SerializeField] private float electronAlong = 5f;

        [Header("Add / remove animation")]
        [Tooltip("Seconds for an atom/lone pair to grow in when added (0 = instant).")]
        [SerializeField] private float popInDuration = 0.18f;
        [Tooltip("Seconds for an atom/lone pair to shrink out when removed (0 = instant).")]
        [SerializeField] private float popOutDuration = 0.14f;

        [Header("Recenter")]
        [Tooltip("Camera the molecule snaps in front of when recentred. Empty = Camera.main (the headset).")]
        [SerializeField] private Camera viewCamera;
        [Tooltip("Distance in front of the camera the molecule is placed when recentred (metres).")]
        [SerializeField] private float recenterDistance = 0.6f;
        [Tooltip("Also level the placement to the camera's height instead of following where you're looking " +
                 "up/down (keeps the molecule at a comfortable, consistent height).")]
        [SerializeField] private bool recenterAtEyeLevel = true;
        [Tooltip("Also reset the molecule's orientation when recentring.")]
        [SerializeField] private bool recenterResetsRotation = true;

        [Header("Simulation")]
        [SerializeField] private float maxTimestep = 0.025f;

        private VsepRMolecule _molecule;
        private readonly Dictionary<int, GameObject> _atomViews = new();
        private readonly List<(Bond bond, BondView view)> _bondViews = new();

        // The atom whose bond Cycle Bond Order affects. Defaults to the last-added atom; the drag
        // controller repoints it when the player triggers a different atom.
        private PairGroup _activeBondAtom;

        // Read-only access for other view components (e.g. BondAngleOverlay).
        public VsepRMolecule Molecule => _molecule;

        // Asymmetric starting directions so atoms visibly rearrange into the ideal geometry.
        private static readonly float3[] StartDirections =
        {
            new float3(1f, 0f, 0f),
            new float3(0f, 1f, 0.2f),
            new float3(-0.5f, -0.5f, 0.1f),
            new float3(0.3f, 0.3f, 1f),
            new float3(-1f, 0.2f, -0.3f),
            new float3(0.2f, -1f, 0.4f),
        };

        private void Awake()
        {
            _molecule = new VsepRMolecule();

            _molecule.GroupAdded += OnGroupAdded;
            _molecule.GroupRemoved += OnGroupRemoved;
            _molecule.BondAdded += OnBondAdded;
            _molecule.BondRemoved += OnBondRemoved;

            var central = new PairGroup(float3.zero, isLonePair: false);
            _molecule.AddCentralAtom(central);

            int count = Mathf.Clamp(initialAtomCount, 0, StartDirections.Length);
            for (int i = 0; i < count; i++)
            {
                float3 pos = math.normalize(StartDirections[i]) * PairGroup.BondedPairDistance;
                var atom = new PairGroup(pos, isLonePair: false);
                _molecule.AddGroupAndBond(atom, central, bondOrder: 1, bondLength: PairGroup.BondedPairDistance);
            }
        }

        private void Update()
        {
            HandleInput();

            float dt = Mathf.Min(Time.deltaTime, maxTimestep);
            _molecule.Update(dt);
        }

        // --- Runtime editing (keyboard) ---
        //   +/=  : add a bonded atom        -/_ : remove the last bonded atom
        //   1..6 : set the bonded-atom count (AX2 .. AX6)
        private void HandleInput()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) AddBondedAtom();
            if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) RemoveLastAtom();

            if (kb.digit1Key.wasPressedThisFrame) SetBondedAtomCount(1);
            if (kb.digit2Key.wasPressedThisFrame) SetBondedAtomCount(2);
            if (kb.digit3Key.wasPressedThisFrame) SetBondedAtomCount(3);
            if (kb.digit4Key.wasPressedThisFrame) SetBondedAtomCount(4);
            if (kb.digit5Key.wasPressedThisFrame) SetBondedAtomCount(5);
            if (kb.digit6Key.wasPressedThisFrame) SetBondedAtomCount(6);

            if (kb.lKey.wasPressedThisFrame) AddLonePair();
            if (kb.kKey.wasPressedThisFrame) RemoveLastLonePair();

            if (kb.bKey.wasPressedThisFrame) CycleBondOrder();   // single -> double -> triple -> single
        }

        public bool AddBondedAtom() => AddBondedAtom(1);

        // Adds a bonded atom with the given bond order (1 single, 2 double, 3 triple). Bond order is
        // purely visual in VSEPR - a multiple bond is still one electron domain, so the geometry is
        // unchanged; only the rendered stick count differs.
        public bool AddBondedAtom(int bondOrder)
        {
            if (_molecule.RadialGroups.Count >= ModelMolecule.MaxConnections) return false;

            var atom = new PairGroup(RandomDirection() * PairGroup.BondedPairDistance, isLonePair: false);
            _molecule.AddGroupAndBond(atom, _molecule.CentralAtom,
                bondOrder: Mathf.Clamp(bondOrder, 1, 3), bondLength: PairGroup.BondedPairDistance);
            _activeBondAtom = atom;   // newly added atom becomes the bond-order target
            return true;
        }

        // Marks which atom's bond Cycle Bond Order targets. Called when the player triggers a bonded atom
        // (ignored for lone pairs / the central atom). Persists until a new atom is added or another is
        // triggered.
        public void SetActiveBondAtom(PairGroup atom)
        {
            if (atom == null || atom.IsLonePair || atom.IsCentralAtom) return;
            if (_molecule.RadialAtoms.Contains(atom)) _activeBondAtom = atom;
        }

        // Cycles the active atom's bond order 1 -> 2 -> 3 -> 1 (falls back to the last atom) and updates its view.
        public bool CycleBondOrder()
        {
            var radial = _molecule.RadialAtoms;
            if (radial.Count == 0) return false;

            PairGroup target = (_activeBondAtom != null && radial.Contains(_activeBondAtom))
                ? _activeBondAtom
                : radial[radial.Count - 1];

            Bond bond = _molecule.GetParentBond(target);
            if (bond == null) return false;

            bond.Order = bond.Order >= 3 ? 1 : bond.Order + 1;
            for (int i = 0; i < _bondViews.Count; i++)
                if (_bondViews[i].bond == bond && _bondViews[i].view != null)
                    _bondViews[i].view.SetOrder(bond.Order);
            return true;
        }

        public bool RemoveLastAtom()
        {
            var radial = _molecule.RadialAtoms;
            if (radial.Count == 0) return false;
            _molecule.RemoveGroup(radial[radial.Count - 1]);
            return true;
        }

        // Adds bonded atoms until the count reaches `target` OR no more slots are available
        // (lone pairs share the same connection budget, so this gracefully stops short instead of looping).
        public void SetBondedAtomCount(int target)
        {
            target = Mathf.Clamp(target, 0, ModelMolecule.MaxConnections);
            while (_molecule.RadialAtoms.Count < target && AddBondedAtom()) { }
            while (_molecule.RadialAtoms.Count > target && RemoveLastAtom()) { }
        }

        public bool AddLonePair()
        {
            if (_molecule.RadialGroups.Count >= ModelMolecule.MaxConnections) return false;

            var lonePair = new PairGroup(RandomDirection() * PairGroup.LonePairDistance, isLonePair: true);
            _molecule.AddGroupAndBond(lonePair, _molecule.CentralAtom, bondOrder: 0, bondLength: PairGroup.LonePairDistance);
            return true;
        }

        public bool RemoveLastLonePair()
        {
            var lonePairs = _molecule.RadialLonePairs;
            if (lonePairs.Count == 0) return false;
            _molecule.RemoveGroup(lonePairs[lonePairs.Count - 1]);
            return true;
        }

        // Removes every radial group, leaving only the central atom.
        public void ResetMolecule()
        {
            while (RemoveLastAtom()) { }
            while (RemoveLastLonePair()) { }
        }

        // Resets, then builds exactly x bonded atoms and e lone pairs (clamped to the connection
        // budget). Used by the game layer to display a target molecule (e.g. for Identify challenges)
        // or to seed a real-molecule preset. Lone pairs are added first so they're never starved
        // of the shared radial budget.
        public void SetConfiguration(int x, int e)
        {
            ResetMolecule();
            for (int i = 0; i < e; i++) if (!AddLonePair()) break;
            for (int i = 0; i < x; i++) if (!AddBondedAtom()) break;
        }

        // Snaps the whole molecule to a fixed distance in front of the camera, so it can be recovered if
        // it's been grabbed/thrown far away or the player has moved off with the joystick. Works on the
        // molecule's own transform (all atom/bond views are children), so the model is untouched.
        public void RecenterInFront()
        {
            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null) return;

            Transform camT = cam.transform;
            Vector3 forward = camT.forward;
            if (recenterAtEyeLevel)
            {
                forward.y = 0f;                                    // flatten so it lands level, not tilted up/down
                forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : camT.forward;
            }

            transform.position = camT.position + forward * recenterDistance;
            if (recenterResetsRotation) transform.rotation = Quaternion.identity;

            // Clear any residual throw velocity so it doesn't drift after being placed.
            if (TryGetComponent(out Rigidbody rb) && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        // Random non-degenerate unit direction; the attractor sorts out the final geometry.
        private static float3 RandomDirection()
        {
            float3 dir = math.normalize(new float3(
                UnityEngine.Random.Range(-1f, 1f),
                UnityEngine.Random.Range(-1f, 1f),
                UnityEngine.Random.Range(-1f, 1f)));
            return math.all(math.isfinite(dir)) ? dir : new float3(1f, 0f, 0f);
        }

        // --- View sync ---

        private void OnGroupAdded(PairGroup group)
        {
            if (group.IsLonePair)
            {
                CreateLonePairView(group);
                return;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = group.IsCentralAtom ? "CentralAtom" : "Atom";
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localScale = Vector3.one * atomDiameter;
            Destroy(go.GetComponent<Collider>());

            // Assign an explicit URP material rather than tinting the primitive's default one, so device
            // builds don't fall back to the magenta error shader (see CreateOpaqueMaterial).
            go.GetComponent<MeshRenderer>().material = CreateOpaqueMaterial(group.IsCentralAtom ? centralColor : radialColor);

            go.AddComponent<AtomView>().Bind(group);
            go.AddComponent<PopScale>().PopIn(popInDuration);
            _atomViews[group.Id] = go;
        }

        private void CreateLonePairView(PairGroup group)
        {
            var go = new GameObject("LonePair");
            go.transform.SetParent(transform, worldPositionStays: false);

            bool hasMesh = lonePairMesh != null;
            var config = new LonePairView.Config
            {
                shellMesh = lonePairMesh,
                // URP/Unlit translucent (URP/Lit's transparent variant is often stripped; Unlit is reliable).
                shellMaterial = CreateTranslucentMaterial(lonePairColor),
                scale = hasMesh ? lonePairMeshScale : lonePairDiameter,
                elongation = hasMesh ? 1f : lonePairElongation,
                pullBack = hasMesh ? lonePairPullback : 0f,   // only the balloon tucks its tip in
                showElectrons = showLonePairElectrons,
                electronMaterial = showLonePairElectrons
                    ? CreateTranslucentMaterial(new Color(electronColor.r, electronColor.g, electronColor.b, electronOpacity))
                    : null,
                electronRadius = electronRadius,
                electronPerp = electronPerp,
                electronAlong = electronAlong
            };

            go.AddComponent<LonePairView>().Initialize(group, config);
            go.AddComponent<PopScale>().PopIn(popInDuration);
            _atomViews[group.Id] = go; // tracked here so OnGroupRemoved can destroy it
        }

        private void OnGroupRemoved(PairGroup group)
        {
            if (_atomViews.TryGetValue(group.Id, out GameObject go))
            {
                _atomViews.Remove(group.Id);
                if (go.TryGetComponent(out PopScale pop)) pop.Collapse(popOutDuration);
                else Destroy(go);
            }
        }

        private void OnBondAdded(Bond bond)
        {
            if (bond.Order == 0) return; // lone-pair "bonds" have no visible stick

            // Empty container; BondView builds the actual cylinder(s) itself (1-3 for single/double/triple).
            var go = new GameObject("Bond");
            go.transform.SetParent(transform, worldPositionStays: false);

            BondView view = go.AddComponent<BondView>().Initialize(bond, bondThickness, CreateOpaqueMaterial(bondColor));
            _bondViews.Add((bond, view));
        }

        private void OnBondRemoved(Bond bond)
        {
            for (int i = _bondViews.Count - 1; i >= 0; i--)
            {
                if (_bondViews[i].bond == bond)
                {
                    if (_bondViews[i].view != null) Destroy(_bondViews[i].view.gameObject);
                    _bondViews.RemoveAt(i);
                }
            }
        }

        // Creates a fresh opaque material from a URP shader (falling back to Simple Lit / Standard).
        // Assigning this explicitly - instead of tinting the primitive's auto-assigned default material -
        // keeps the shader referenced so device builds include it and don't render magenta.
        // Requires URP/Lit to be in Project Settings > Graphics > Always Included Shaders for the build.
        private static Material CreateOpaqueMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) shader = Shader.Find("Standard"); // built-in pipeline fallback

            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        // Creates a fresh alpha-blended translucent material using URP/Unlit (variant always
        // available) with fallbacks. Color's alpha controls translucency.
        private static Material CreateTranslucentMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard"); // built-in pipeline fallback

            var mat = new Material(shader);

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);    // 0 = Opaque, 1 = Transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);        // 0 = Alpha blend
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_QueueControl")) mat.SetFloat("_QueueControl", 1f); // URP 14+: User override

            // Built-in pipeline Standard shader rendering mode.
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3f); // 3 = Transparent

            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON"); // Standard shader blend keyword

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

            return mat;
        }
    }
}

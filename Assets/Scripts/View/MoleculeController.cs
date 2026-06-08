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

        [Header("Simulation")]
        [SerializeField] private float maxTimestep = 0.025f;

        private VsepRMolecule _molecule;
        private readonly Dictionary<int, GameObject> _atomViews = new();
        private readonly List<(Bond bond, BondView view)> _bondViews = new();

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
        }

        public bool AddBondedAtom()
        {
            if (_molecule.RadialGroups.Count >= ModelMolecule.MaxConnections) return false;

            var atom = new PairGroup(RandomDirection() * PairGroup.BondedPairDistance, isLonePair: false);
            _molecule.AddGroupAndBond(atom, _molecule.CentralAtom, bondOrder: 1, bondLength: PairGroup.BondedPairDistance);
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

            SetColor(go, group.IsCentralAtom ? centralColor : radialColor);

            go.AddComponent<AtomView>().Bind(group);
            _atomViews[group.Id] = go;
        }

        private void CreateLonePairView(PairGroup group)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "LonePair";
            go.transform.SetParent(transform, worldPositionStays: false);
            Destroy(go.GetComponent<Collider>());

            // Replace the auto-assigned URP/Lit material entirely with a fresh URP/Unlit one.
            // URP/Lit's transparent variant is often stripped at import time, so toggling its
            // keywords at runtime silently keeps it opaque. URP/Unlit reliably has transparency.
            go.GetComponent<MeshRenderer>().material = CreateTranslucentMaterial(lonePairColor);

            go.AddComponent<LonePairView>().Initialize(group, lonePairDiameter, lonePairElongation);
            _atomViews[group.Id] = go; // tracked here so OnGroupRemoved can destroy it
        }

        private void OnGroupRemoved(PairGroup group)
        {
            if (_atomViews.TryGetValue(group.Id, out GameObject go))
            {
                Destroy(go);
                _atomViews.Remove(group.Id);
            }
        }

        private void OnBondAdded(Bond bond)
        {
            if (bond.Order == 0) return; // lone-pair "bonds" have no visible stick

            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Bond";
            go.transform.SetParent(transform, worldPositionStays: false);
            Destroy(go.GetComponent<Collider>());

            SetColor(go, bondColor);

            BondView view = go.AddComponent<BondView>().Initialize(bond, bondThickness);
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

        // Works for both URP (_BaseColor) and the built-in pipeline (_Color).
        private static void SetColor(GameObject go, Color color)
        {
            Material mat = go.GetComponent<MeshRenderer>().material;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
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

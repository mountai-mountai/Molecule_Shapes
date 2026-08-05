// A confetti burst when a round is completed, so finishing a set feels like an event.
//
// The ParticleSystem is built in code (no prefab/asset needed) and uses a URP/Unlit transparent
// material - the KeepUrpUnlit material in Resources keeps that shader in device builds, so it won't
// render magenta on the Quest. Bursts in front of the player's camera, facing them.
//
// Setup: put this on the Molecule object (next to GameSessionController). Optionally assign your own
// ParticleSystem prefab to Custom Effect to replace the built-in burst entirely.

using UnityEngine;
using Molecule_Shapes.Game;

namespace Molecule_Shapes.View
{
    [RequireComponent(typeof(GameSessionController))]
    public class CelebrationEffect : MonoBehaviour
    {
        [Header("Optional override")]
        [Tooltip("Your own ParticleSystem prefab. Empty = the built-in confetti burst below.")]
        [SerializeField] private ParticleSystem customEffect;

        [Header("Placement")]
        [Tooltip("Camera the burst appears in front of. Empty = Camera.main (the headset).")]
        [SerializeField] private Camera viewCamera;
        [Tooltip("Metres in front of the camera where the burst is centred.")]
        [SerializeField] private float distance = 0.8f;
        [Tooltip("Metres above the camera's eye line (burst falls through view).")]
        [SerializeField] private float heightOffset = 0.35f;

        [Header("Built-in burst")]
        [SerializeField] private int particleCount = 90;
        [SerializeField] private float particleSize = 0.018f;
        [SerializeField] private float lifetime = 2.2f;
        [SerializeField] private float speed = 1.1f;
        [SerializeField] private Color colorA = new Color(1f, 0.85f, 0.25f);
        [SerializeField] private Color colorB = new Color(0.35f, 0.80f, 1f);

        private GameSessionController _controller;
        private ParticleSystem _system;
        private bool _subscribed;

        private GameSession Session => _controller != null ? _controller.Session : null;

        private void Awake() => _controller = GetComponent<GameSessionController>();

        private void OnEnable() => TrySubscribe();
        private void Start() => TrySubscribe();

        private void OnDisable()
        {
            if (!_subscribed || Session == null) return;
            Session.RoundCompleted -= Play;
            _subscribed = false;
        }

        private void TrySubscribe()
        {
            if (_subscribed || Session == null) return;
            Session.RoundCompleted += Play;
            _subscribed = true;
        }

        [ContextMenu("Play Celebration")]
        public void Play()
        {
            EnsureSystem();
            if (_system == null) return;

            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam != null)
            {
                Transform t = cam.transform;
                _system.transform.position = t.position + t.forward * distance + Vector3.up * heightOffset;
                _system.transform.rotation = Quaternion.LookRotation(t.forward, Vector3.up);
            }

            _system.Clear();
            _system.Play();
        }

        private void EnsureSystem()
        {
            if (_system != null) return;

            if (customEffect != null)
            {
                _system = Instantiate(customEffect);
                _system.Stop();
                return;
            }

            var go = new GameObject("Celebration Burst");
            _system = go.AddComponent<ParticleSystem>();
            _system.Stop();   // configure while stopped, then Play() on demand

            ParticleSystem.MainModule main = _system.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = particleSize;
            main.gravityModifier = 0.35f;                       // confetti falls
            main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(16, particleCount);

            ParticleSystem.EmissionModule emission = _system.emission;
            emission.rateOverTime = 0f;                          // burst only
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)particleCount) });

            ParticleSystem.ShapeModule shape = _system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.12f;

            ParticleSystem.RotationOverLifetimeModule rot = _system.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);     // tumble

            ParticleSystem.ColorOverLifetimeModule fade = _system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            var psRenderer = go.GetComponent<ParticleSystemRenderer>();
            psRenderer.material = PanelUi.TransparentMaterial(Color.white);
            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        }
    }
}

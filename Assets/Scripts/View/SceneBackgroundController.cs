// Simple, Inspector-driven control of the scene's background colour and lighting, so you can restyle
// the environment without hunting through Camera / Lighting windows. Drop it on any GameObject (e.g.
// the Molecule object or a dedicated "Environment" object).
//
// Applies on start and live while playing (OnValidate). A gradient background isn't a single colour, so
// it can't be set here - see the class notes for the skybox route.

using UnityEngine;
using UnityEngine.Rendering;

namespace Molecule_Shapes.View
{
    public class SceneBackgroundController : MonoBehaviour
    {
        [Header("Camera background")]
        [Tooltip("Camera to recolour. Empty = Camera.main (the XR head camera).")]
        [SerializeField] private Camera targetCamera;
        [Tooltip("Fill the background with Background Color (a solid colour). Turn off to leave the camera's " +
                 "existing background - e.g. a skybox or passthrough - untouched.")]
        [SerializeField] private bool solidColorBackground = true;
        [SerializeField] private Color backgroundColor = new Color(0.03f, 0.04f, 0.07f, 1f);

        /// <summary>The configured VR background colour, so PassthroughController can crossfade to it.</summary>
        public Color BackgroundColor => backgroundColor;

        [Header("Ambient light")]
        [Tooltip("Override the scene's flat ambient colour (the base light on everything).")]
        [SerializeField] private bool overrideAmbient = true;
        [SerializeField] private Color ambientColor = new Color(0.35f, 0.38f, 0.45f, 1f);

        [Header("Main directional light")]
        [Tooltip("The scene's main light (drag your Directional Light here). Leave empty to skip.")]
        [SerializeField] private Light mainLight;
        [SerializeField] private bool overrideMainLight = false;
        [SerializeField] private Color lightColor = Color.white;
        [SerializeField, Range(0f, 3f)] private float lightIntensity = 1f;
        [SerializeField] private Vector3 lightEulerAngles = new Vector3(50f, -30f, 0f);

        private void Start() => Apply();

        private void OnValidate()
        {
            // Live preview in the editor (play or edit mode).
            Apply();
        }

        [ContextMenu("Apply Now")]
        public void Apply()
        {
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam != null && solidColorBackground)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;   // URP honors this for a solid background
                cam.backgroundColor = backgroundColor;
            }

            if (overrideAmbient)
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = ambientColor;
            }

            if (mainLight != null && overrideMainLight)
            {
                mainLight.color = lightColor;
                mainLight.intensity = lightIntensity;
                mainLight.transform.rotation = Quaternion.Euler(lightEulerAngles);
            }
        }
    }
}

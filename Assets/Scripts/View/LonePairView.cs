using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Molecule_Shapes.Model;

namespace Molecule_Shapes.View
{
    // Renders a lone pair as a translucent "balloon" (PhET's balloon2 mesh, or an elongated sphere
    // fallback) whose long axis points radially outward, optionally with two small electron spheres.
    //
    // Positioning mirrors PhET: the view is pulled BACK toward the parent atom by `pullBack`, so the
    // mesh's tip tucks inside the central atom and only the bulb shows outward - instead of the whole
    // teardrop floating out with its tip pointing in.
    //
    // The shell (scaled) and the electrons (unscaled) are separate children so the electrons keep their
    // absolute size. A PopScale on the parent object animates the whole thing in/out.
    public class LonePairView : MonoBehaviour
    {
        public struct Config
        {
            public Mesh shellMesh;          // null = elongated sphere
            public Material shellMaterial;
            public float scale;             // uniform shell scale
            public float elongation;        // extra Y stretch (1 for the balloon, which is already teardrop)
            public float pullBack;          // pull the view toward the atom so the tip hides inside it
            public bool showElectrons;
            public Material electronMaterial;
            public float electronRadius;
            public float electronPerp;      // +/- perpendicular spread of the two electrons
            public float electronAlong;     // distance out along the axis
        }

        private PairGroup _group;
        private float _pullBack;

        public LonePairView Initialize(PairGroup group, Config config)
        {
            _group = group;
            _pullBack = config.pullBack;

            BuildShell(config);
            if (config.showElectrons && config.electronMaterial != null)
            {
                BuildElectron(config, +config.electronPerp);
                BuildElectron(config, -config.electronPerp);
            }

            _group.PositionChanged += OnPositionChanged;
            OnPositionChanged(group.Position);
            return this;
        }

        private void BuildShell(Config config)
        {
            GameObject shell;
            if (config.shellMesh != null)
            {
                shell = new GameObject("Shell", typeof(MeshFilter), typeof(MeshRenderer));
                shell.GetComponent<MeshFilter>().sharedMesh = config.shellMesh;
            }
            else
            {
                shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Collider col = shell.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
            shell.name = "Shell";
            shell.transform.SetParent(transform, false);
            shell.transform.localScale = new Vector3(config.scale, config.scale * config.elongation, config.scale);
            shell.GetComponent<MeshRenderer>().material = config.shellMaterial;
        }

        private void BuildElectron(Config config, float perp)
        {
            var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            e.name = "Electron";
            Collider col = e.GetComponent<Collider>();
            if (col != null) Destroy(col);
            e.transform.SetParent(transform, false);
            e.transform.localPosition = new Vector3(perp, config.electronAlong, 0f);   // along = local +Y (the axis)
            e.transform.localScale = Vector3.one * (config.electronRadius * 2f);
            e.GetComponent<MeshRenderer>().material = config.electronMaterial;
        }

        private void OnPositionChanged(float3 p)
        {
            Vector3 pos = new Vector3(p.x, p.y, p.z);
            float mag = pos.magnitude;
            Vector3 orientation = mag > 1e-4f ? pos / mag : Vector3.up;

            // Pull the view origin back toward the central atom so the mesh's tip hides inside it.
            transform.localPosition = mag > _pullBack ? pos - orientation * _pullBack : Vector3.zero;
            transform.localRotation = Quaternion.FromToRotation(Vector3.up, orientation);
        }

        private void OnDestroy()
        {
            if (_group != null) _group.PositionChanged -= OnPositionChanged;
        }
    }
}

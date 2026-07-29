// Named blend presets for the passthrough (MR) <-> VR crossfade, so the look can be set from one
// place instead of typing raw 0..1 numbers at every call site.
//
//   FullVr -> opaque background, the room is hidden
//   Ghost  -> the room shows faintly behind the VR background colour
//   FullMr -> background fully transparent, you're standing in your room

using UnityEngine;

namespace Molecule_Shapes.View
{
    public enum PassthroughPreset
    {
        FullVr,
        Ghost,
        FullMr
    }

    public static class PassthroughPresets
    {
        /// <summary>The blend value (0 = VR, 1 = MR) a preset corresponds to.</summary>
        public static float BlendFor(PassthroughPreset preset)
        {
            switch (preset)
            {
                case PassthroughPreset.FullVr: return 0f;
                case PassthroughPreset.Ghost:  return 0.5f;
                case PassthroughPreset.FullMr: return 1f;
                default:                       return 1f;
            }
        }

        /// <summary>The preset nearest to a raw blend value.</summary>
        public static PassthroughPreset Nearest(float blend)
        {
            if (blend <= 0.25f) return PassthroughPreset.FullVr;
            if (blend >= 0.75f) return PassthroughPreset.FullMr;
            return PassthroughPreset.Ghost;
        }

        /// <summary>Step to the next preset, wrapping FullMr -> FullVr.</summary>
        public static PassthroughPreset Next(PassthroughPreset preset)
        {
            return preset == PassthroughPreset.FullMr ? PassthroughPreset.FullVr : preset + 1;
        }

        /// <summary>Human-readable name, for panel buttons and labels.</summary>
        public static string Label(PassthroughPreset preset)
        {
            switch (preset)
            {
                case PassthroughPreset.FullVr: return "Full VR";
                case PassthroughPreset.Ghost:  return "Ghost";
                case PassthroughPreset.FullMr: return "Full MR";
                default:                       return preset.ToString();
            }
        }
    }
}

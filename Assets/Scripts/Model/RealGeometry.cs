// Builds the ideal-orientation vectors for a REAL molecule - the directions that reproduce its
// measured bond angles, rather than the textbook VSEPR ideals.
//
// Feeding these to the attractor (via VsepRMolecule.IdealOrientationsOverride) makes the simulation
// actually hold water at 104.5 degrees instead of 109.5, so the Model/Real toggle changes the molecule
// you can see and measure - not just a number on a panel.
//
// Ordering matters: the attractor pairs these with the molecule's neighbour list, which keeps LONE
// PAIRS FIRST (Molecule.AddToList inserts lone pairs at the front), matching VseprConfiguration. Every
// construction below therefore emits lone-pair slots first, then bond slots.
//
// Pure C# with no Unity dependency beyond Unity.Mathematics, so the geometry is EditMode-testable -
// the tests assert the produced vectors really do subtend the angles they claim.

using System.Collections.Generic;
using Unity.Mathematics;

namespace Molecule_Shapes.Model
{
    public static class RealGeometry
    {
        /// <summary>
        /// Orientation vectors realising the given measured angles for an (x, e) configuration, or null
        /// when this shape has no distinct real construction (its measured geometry matches the ideal).
        /// `bondAngle` is the characteristic bond-bond angle in degrees; `secondaryAngle` is the second
        /// distinct angle where a shape has two (e.g. seesaw's axial-axial), else <= 0.
        /// </summary>
        public static IReadOnlyList<float3> Build(int x, int e, float bondAngle, float secondaryAngle = -1f)
        {
            switch (x, e)
            {
                case (2, 1): return BentWithLonePairs(bondAngle, lonePairCount: 1);   // SO2
                case (2, 2): return BentWithLonePairs(bondAngle, lonePairCount: 2);   // H2O
                case (3, 1): return Pyramidal(bondAngle);                              // NH3
                case (3, 2): return TShaped(bondAngle);                                // ClF3
                case (4, 1): return Seesaw(bondAngle, secondaryAngle);                 // SF4
                case (5, 1): return SquarePyramidal(bondAngle);                        // BrF5
                default: return null;   // ideal == real (CH4, CO2, BH3, SF6, XeF4, AsF5, PCl5)
            }
        }

        // --- Constructions ---------------------------------------------------------------------------

        // Bent: bonds split symmetrically about +y by `angle`; lone pairs fill the opposite side, in the
        // perpendicular plane so they stay as far from the bonds (and each other) as possible.
        private static float3[] BentWithLonePairs(float angle, int lonePairCount)
        {
            float h = math.radians(angle) * 0.5f;
            float3 b1 = new float3(math.sin(h), math.cos(h), 0f);
            float3 b2 = new float3(-math.sin(h), math.cos(h), 0f);

            if (lonePairCount == 1)
                return new[] { new float3(0f, -1f, 0f), b1, b2 };

            // Two lone pairs: opened wider than the bonds (they repel more), in the yz plane.
            float lp = math.radians(115f) * 0.5f;
            return new[]
            {
                new float3(0f, -math.cos(lp),  math.sin(lp)),
                new float3(0f, -math.cos(lp), -math.sin(lp)),
                b1, b2
            };
        }

        // Trigonal pyramidal: three bonds evenly spaced on a cone about -y, with the cone angle solved
        // so the bond-bond angle equals `angle`; the lone pair sits on the axis (+y).
        private static float3[] Pyramidal(float angle)
        {
            float beta = ConeAngleForPairAngle(angle);   // polar angle from the -y axis
            float s = math.sin(beta), c = math.cos(beta);

            var v = new float3[4];
            v[0] = new float3(0f, 1f, 0f);               // lone pair first
            for (int i = 0; i < 3; i++)
            {
                float az = math.radians(120f * i);
                v[i + 1] = new float3(s * math.cos(az), -c, s * math.sin(az));
            }
            return v;
        }

        // T-shaped (trigonal-bipyramidal parent): two lone pairs take equatorial slots, one bond stays
        // equatorial and the two axial bonds bend away from the lone pairs by (90 - angle).
        private static float3[] TShaped(float angle)
        {
            float tilt = math.radians(angle);            // axial-to-equatorial-bond angle
            return new[]
            {
                // lone pairs: the other two equatorial slots
                new float3(math.cos(math.radians(120f)), 0f, math.sin(math.radians(120f))),
                new float3(math.cos(math.radians(240f)), 0f, math.sin(math.radians(240f))),
                // bonds: equatorial, then the two axials leaning toward it
                new float3(1f, 0f, 0f),
                new float3(math.cos(tilt),  math.sin(tilt), 0f),
                new float3(math.cos(tilt), -math.sin(tilt), 0f)
            };
        }

        // Seesaw: lone pair takes an equatorial slot; the two equatorial bonds close to `angle`, and the
        // axial pair bends away from the lone pair so the axial-axial angle is `axialAngle`.
        private static float3[] Seesaw(float angle, float axialAngle)
        {
            if (axialAngle <= 0f) axialAngle = 173f;
            float h = math.radians(angle) * 0.5f;
            float lean = math.radians(180f - axialAngle) * 0.5f;   // how far each axial tips off ±y

            return new[]
            {
                new float3(1f, 0f, 0f),                                        // lone pair (equatorial)
                new float3(-math.cos(h), 0f,  math.sin(h)),                    // equatorial bonds
                new float3(-math.cos(h), 0f, -math.sin(h)),
                new float3(-math.sin(lean),  math.cos(lean), 0f),              // axials, tipped away
                new float3(-math.sin(lean), -math.cos(lean), 0f)
            };
        }

        // Square pyramidal: apex on +y, four basal bonds at `angle` from the apex direction, lone pair
        // opposite the apex.
        private static float3[] SquarePyramidal(float angle)
        {
            float polar = math.radians(angle);
            float s = math.sin(polar), c = math.cos(polar);

            var v = new float3[6];
            v[0] = new float3(0f, -1f, 0f);              // lone pair first
            v[1] = new float3(0f, 1f, 0f);               // apical bond
            for (int i = 0; i < 4; i++)
            {
                float az = math.radians(90f * i);
                v[i + 2] = new float3(s * math.cos(az), c, s * math.sin(az));
            }
            return v;
        }

        // --- Helpers ---------------------------------------------------------------------------------

        // For n=3 vectors evenly spaced on a cone of polar angle b, the pairwise angle t satisfies
        //   cos t = cos^2 b - 0.5 sin^2 b  =  1.5 cos^2 b - 0.5
        // Inverting gives the cone angle that produces a requested bond-bond angle.
        private static float ConeAngleForPairAngle(float pairAngleDegrees)
        {
            float cosT = math.cos(math.radians(pairAngleDegrees));
            float cosSq = math.clamp((cosT + 0.5f) / 1.5f, 0f, 1f);
            return math.acos(math.sqrt(cosSq));
        }
    }
}

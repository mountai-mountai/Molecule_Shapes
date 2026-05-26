// Kabsch / orthogonal-Procrustes rotation solver.
//
// PhET's AttractorModel computes R = V * U^T from the SVD of the covariance matrix
// H = sum_i ideal_i * current_i^T. That R is exactly the orthogonal polar factor of
// A = sum_i current_i * ideal_i^T, which we extract directly via the Mueller/Bender
// iterative method ("A robust method to extract the rotational part of deformations",
// Mueller, Bender, Chentanez, Macklin 2016) - no SVD required, ~5 quaternion iterations.

using System.Collections.Generic;
using Unity.Mathematics;

namespace Molecule_Shapes.Core
{
    public static class RotationSolver
    {
        private const int MaxIterations = 25;
        private const float Epsilon = 1e-9f;

        /// <summary>
        /// Returns the rotation R that minimizes sum_i |R * from_i - to_i|^2 (rotation about the origin,
        /// translation ignored). from_i and to_i should be the same length.
        /// </summary>
        public static quaternion BestFitRotation(IReadOnlyList<float3> from, IReadOnlyList<float3> to,
                                                  quaternion initialGuess)
        {
            // A = sum_i to_i * from_i^T  (3x3). Its orthogonal polar factor is the optimal rotation.
            float3x3 a = float3x3.zero;
            int n = from.Count;
            for (int i = 0; i < n; i++)
            {
                a += OuterProduct(to[i], from[i]);
            }
            return ExtractRotation(a, initialGuess);
        }

        /// <summary>
        /// Extracts the rotational (orthogonal) part of a 3x3 matrix via the Mueller/Bender iteration.
        /// </summary>
        public static quaternion ExtractRotation(float3x3 a, quaternion q)
        {
            for (int iter = 0; iter < MaxIterations; iter++)
            {
                float3x3 r = new float3x3(q); // current rotation's columns are the rotated basis axes

                // omega = (r0 x a0 + r1 x a1 + r2 x a2) / |r0.a0 + r1.a1 + r2.a2|
                float3 numerator =
                    math.cross(r.c0, a.c0) +
                    math.cross(r.c1, a.c1) +
                    math.cross(r.c2, a.c2);

                float denominator =
                    math.abs(math.dot(r.c0, a.c0) + math.dot(r.c1, a.c1) + math.dot(r.c2, a.c2)) + Epsilon;

                float3 omega = numerator / denominator;

                float w = math.length(omega);
                if (w < Epsilon) break;

                // Rotate q by angle w about axis omega/w.
                q = math.normalize(math.mul(quaternion.AxisAngle(omega / w, w), q));
            }
            return q;
        }

        /// <summary>Outer product a * b^T as a float3x3 (column-major: result.c[k] = a * b[k]).</summary>
        private static float3x3 OuterProduct(float3 a, float3 b)
        {
            return new float3x3(
                a.x * b.x, a.x * b.y, a.x * b.z,
                a.y * b.x, a.y * b.y, a.y * b.z,
                a.z * b.x, a.z * b.y, a.z * b.z);
        }
    }
}

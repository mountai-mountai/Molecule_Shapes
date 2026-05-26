// Port of dot/Permutation.ts - an immutable permutation that can rearrange a list.
// Only the members used by LocalShape + AttractorModel are ported.

using System;
using System.Collections.Generic;

namespace Molecule_Shapes.Core
{
    public class Permutation
    {
        // newList[i] = oldList[Indices[i]]
        public readonly int[] Indices;

        public Permutation(int[] indices)
        {
            Indices = indices;
        }

        public int Size => Indices.Length;

        // Identity permutation of the given size: [0, 1, 2, ... size-1].
        public static Permutation Identity(int size)
        {
            UnityEngine.Debug.Assert(size >= 0);
            var indices = new int[size];
            for (int i = 0; i < size; i++) indices[i] = i;
            return new Permutation(indices);
        }

        // All permutations of a given size.
        public static List<Permutation> Permutations(int size)
        {
            var result = new List<Permutation>();
            var range = new int[size];
            for (int i = 0; i < size; i++) range[i] = i;

            ForEachPermutation(range, integers =>
            {
                var copy = new int[integers.Count];
                for (int i = 0; i < integers.Count; i++) copy[i] = integers[i];
                result.Add(new Permutation(copy));
            });
            return result;
        }

        // Returns every permutation obtainable by permuting only the specified positions of this permutation.
        public List<Permutation> WithIndicesPermuted(IReadOnlyList<int> indices)
        {
            var result = new List<Permutation>();
            ForEachPermutation(indices, integers =>
            {
                var newPermutation = (int[])Indices.Clone();
                for (int i = 0; i < indices.Count; i++)
                {
                    newPermutation[indices[i]] = Indices[integers[i]];
                }
                result.Add(new Permutation(newPermutation));
            });
            return result;
        }

        public bool Equals(Permutation other)
        {
            if (other == null || Indices.Length != other.Indices.Length) return false;
            for (int i = 0; i < Indices.Length; i++)
                if (Indices[i] != other.Indices[i]) return false;
            return true;
        }

        public override string ToString() => $"P[{string.Join(", ", Indices)}]";

        // Calls callback on every permutation of the given list, in lexicographic order.
        public static void ForEachPermutation<T>(IReadOnlyList<T> array, Action<IReadOnlyList<T>> callback)
        {
            RecursiveForEachPermutation(array, new List<T>(), callback);
        }

        private static void RecursiveForEachPermutation<T>(IReadOnlyList<T> array, List<T> prefix, Action<IReadOnlyList<T>> callback)
        {
            if (array.Count == 0)
            {
                callback(prefix);
                return;
            }

            for (int i = 0; i < array.Count; i++)
            {
                T element = array[i];

                var nextArray = new List<T>(array);
                nextArray.RemoveAt(i);

                var nextPrefix = new List<T>(prefix) { element };

                RecursiveForEachPermutation(nextArray, nextPrefix, callback);
            }
        }
    }
}

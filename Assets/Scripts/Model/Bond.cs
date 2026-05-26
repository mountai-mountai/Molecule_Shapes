// Port of Bond.js - a molecular bond between two PairGroups.
// For lone pairs, order is 0 (not an actual molecular bond).

namespace Molecule_Shapes.Model
{
    public class Bond
    {
        public readonly PairGroup A;
        public readonly PairGroup B;

        // Bond order: 0 = lone-pair "bond", 1/2/3 = single/double/triple.
        public int Order { get; set; }

        // Bond length in model units (or 0).
        public float Length { get; set; }

        public Bond(PairGroup a, PairGroup b, int order, float length)
        {
            A = a;
            B = b;
            Order = order;
            Length = length;
        }

        // True if this bond has the given group as one of its endpoints.
        public bool Contains(PairGroup atom) => A == atom || B == atom;

        // Given one endpoint, return the other. Assumes Contains(atom) is true.
        public PairGroup GetOtherAtom(PairGroup atom)
        {
            UnityEngine.Debug.Assert(Contains(atom), "Bond.GetOtherAtom: atom not in bond");
            return A == atom ? B : A;
        }

        public override string ToString() => $"{{{A.Id} => {B.Id}}}";
    }
}

// Port of Molecule.js - abstract base for single-atom-centered molecule.
// Concrete subtypes (VsepRMolecule, RealMolecule) implement GetLocalShape and GetMaximumBondLength.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Molecule_Shapes.Model
{
    public abstract class Molecule
    {
        // Maximum radial connections (PhET clamps 0-6). Adjust via this static field.
        public static int MaxConnections = 6;

        // Whether this molecule uses real observed angles, or a pure VSEPR model.
        public bool IsReal { get; }

        // Core collections (lone pairs are kept FIRST in each list, matching PhET)

        // All pair groups, lone pairs first.
        public readonly List<PairGroup> Groups = new();

        // All bonds; lone-pair "bonds" (order 0) listed first.
        public readonly List<Bond> Bonds = new();

        public readonly List<PairGroup> Atoms = new(); // !IsLonePair

        public readonly List<PairGroup> LonePairs = new(); // IsLonePair

        public readonly List<PairGroup> RadialGroups = new(); // bonded directly to centralAtom

        public readonly List<PairGroup> RadialAtoms = new(); // !IsLonePair + radial

        public readonly List<PairGroup> RadialLonePairs = new(); // IsLonePair + radial

        public PairGroup CentralAtom { get; private set; }

        // Last bond-angle midpoint for a 2-atom system (used by the view).
        public float3? LastMidpoint { get; set; }

        // Events (replace axon Emitters)
        public event Action<Bond> BondAdded;
        public event Action<Bond> BondRemoved;
        public event Action<Bond> BondChanged;
        public event Action<PairGroup> GroupAdded;
        public event Action<PairGroup> GroupRemoved;
        public event Action<PairGroup> GroupChanged;

        protected Molecule(bool isReal)
        {
            IsReal = isReal;

            // Composite events, mirroring Molecule.js wiring.
            BondAdded += b => BondChanged?.Invoke(b);
            BondRemoved += b => BondChanged?.Invoke(b);
            GroupAdded += g => GroupChanged?.Invoke(g);
            GroupRemoved += g => GroupChanged?.Invoke(g);
        }

        // Abstract methods implemented by subtypes.

        // Ideal shape of bonds around the given atom.
        public abstract LocalShape GetLocalShape(PairGroup atom);

        // Maximum bond length (model units).
        public abstract float GetMaximumBondLength();

        // Physics step

        // Steps each non-central group forward and pulls it to its ideal distance.
        public virtual void Update(float deltaTime)
        {
            int numGroups = Groups.Count;
            for (int i = 0; i < numGroups; i++)
            {
                PairGroup group = Groups[i];
                if (group == CentralAtom) continue;

                Bond parentBond = GetParentBond(group);
                PairGroup parentGroup = parentBond.GetOtherAtom(group);

                float oldDistance = math.distance(group.Position, parentGroup.Position);

                group.StepForward(deltaTime);
                group.AttractToIdealDistance(deltaTime, oldDistance, parentBond);
            }
        }

        // Queries

        public List<Bond> GetBondsAround(PairGroup group)
        {
            var result = new List<Bond>();
            foreach (Bond b in Bonds)
                if (b.Contains(group)) result.Add(b);
            return result;
        }

        public List<PairGroup> GetNeighbors(PairGroup group)
        {
            var result = new List<PairGroup>();
            foreach (Bond b in Bonds)
                if (b.Contains(group)) result.Add(b.GetOtherAtom(group));
            return result;
        }

        public int GetNeighborCount(PairGroup group)
        {
            int count = 0;
            foreach (Bond b in Bonds)
                if (b.Contains(group)) count++;
            return count;
        }

        public VseprConfiguration GetCentralVseprConfiguration()
            => VseprConfiguration.GetConfiguration(RadialAtoms.Count, RadialLonePairs.Count);

        // Bond from a group toward the central atom, or null. Assumes a star-shaped molecule.
        public Bond GetParentBond(PairGroup group)
        {
            if (group.IsLonePair)
            {
                var around = GetBondsAround(group);
                return around.Count > 0 ? around[0] : null;
            }

            foreach (Bond b in GetBondsAround(group))
                if (b.GetOtherAtom(group) == CentralAtom) return b;
            return null;
        }

        public PairGroup GetParent(PairGroup group)
            => GetParentBond(group).GetOtherAtom(group);

        // Mutation

        public void AddCentralAtom(PairGroup group)
        {
            CentralAtom = group;
            group.IsCentralAtom = true; // set before notifying so GroupAdded listeners see it
            AddGroup(group, true);
        }

        // Adds a child group plus a bond to its parent. bondOrder is 0 for lone pairs.
        public void AddGroupAndBond(PairGroup group, PairGroup parent, int bondOrder, float bondLength = 0f)
        {
            // Add group first, but delay its notification (inconsistent state until the bond exists).
            AddGroup(group, false);

            if (bondLength <= 0f)
                bondLength = math.distance(group.Position, parent.Position);

            AddBond(new Bond(group, parent, bondOrder, bondLength));

            // Notify after the bond exists.
            GroupAdded?.Invoke(group);
        }

        public void AddGroup(PairGroup group, bool notify = true)
        {
            UnityEngine.Debug.Assert(CentralAtom != null, "Central atom must be added first");

            AddToList(Groups, group, group.IsLonePair);
            if (group.IsLonePair) AddToList(LonePairs, group, true);
            else                  AddToList(Atoms, group, false);

            if (notify) GroupAdded?.Invoke(group);
        }

        public void AddBond(Bond bond)
        {
            bool isLonePairBond = bond.Order == 0;
            AddToList(Bonds, bond, isLonePairBond);
            if (bond.Contains(CentralAtom))
            {
                PairGroup group = bond.GetOtherAtom(CentralAtom);
                AddToList(RadialGroups, group, isLonePairBond);
                if (group.IsLonePair) AddToList(RadialLonePairs, group, isLonePairBond);
                else                  AddToList(RadialAtoms, group, isLonePairBond);
            }

            BondAdded?.Invoke(bond);
        }

        public void RemoveBond(Bond bond)
        {
            Bonds.Remove(bond);

            if (bond.Contains(CentralAtom))
            {
                PairGroup group = bond.GetOtherAtom(CentralAtom);
                RadialGroups.Remove(group);
                if (group.IsLonePair) RadialLonePairs.Remove(group);
                else                  RadialAtoms.Remove(group);
            }

            BondRemoved?.Invoke(bond);
        }

        public void RemoveGroup(PairGroup group)
        {
            UnityEngine.Debug.Assert(group != CentralAtom, "Cannot remove central atom");

            // Remove all attached bonds first.
            var bondList = GetBondsAround(group);
            foreach (Bond b in bondList) RemoveBond(b);

            Groups.Remove(group);
            if (group.IsLonePair) LonePairs.Remove(group);
            else                  Atoms.Remove(group);

            GroupRemoved?.Invoke(group);
            // RemoveBond already fired BondRemoved; PhET re-emits here for delayed listeners
            // - omitted to avoid doubles.
        }

        public void RemoveAllGroups()
        {
            var copy = new List<PairGroup>(Groups);
            foreach (PairGroup group in copy)
                if (group != CentralAtom) RemoveGroup(group);
        }

        // VSEPR helpers

        public IReadOnlyList<float3> GetCorrespondingIdealGeometryVectors()
            => GetCentralVseprConfiguration().ElectronGeometry.UnitVectors;

        public bool WouldAllowBondOrder(int bondOrder)
            => RadialGroups.Count < MaxConnections;

        public List<PairGroup> GetDistantLonePairs()
        {
            var result = new List<PairGroup>();
            foreach (PairGroup lonePair in LonePairs)
                if (!RadialLonePairs.Contains(lonePair)) result.Add(lonePair);
            return result;
        }

        // Ideal local shape of bonds around an atom, for attraction/repulsion convergence.
        public LocalShape GetLocalVseprShape(PairGroup atom)
        {
            var groups = GetNeighbors(atom);

            int numLonePairs = 0;
            foreach (PairGroup group in groups)
                if (group.IsLonePair) numLonePairs++;
            int numAtoms = groups.Count - numLonePairs;

            return new LocalShape(
                LocalShape.VseprPermutations(groups),
                atom,
                groups,
                VseprConfiguration.GetConfiguration(numAtoms, numLonePairs).ElectronGeometry.UnitVectors
            );
        }

        public float GetIdealDistanceFromCenter(PairGroup group)
        {
            Bond bond = GetParentBond(group);
            UnityEngine.Debug.Assert(bond.Contains(CentralAtom));
            return group.IsLonePair ? PairGroup.LonePairDistance : bond.Length;
        }

        // Adds terminal lone pairs around an outer atom, in proper initial orientations.
        public void AddTerminalLonePairs(PairGroup atom, int quantity)
        {
            VseprConfiguration pairConfig = VseprConfiguration.GetConfiguration(1, quantity);
            var orientations = pairConfig.ElectronGeometry.UnitVectors;

            // Rotate the ideal lone-pair configuration to match the atom's orientation.
            float3 from = -orientations[orientations.Count - 1];
            quaternion rot = FromToRotation(from, atom.Orientation);

            for (int i = 0; i < quantity; i++)
            {
                float3 dir = math.mul(rot, orientations[i]);
                var lonePair = new PairGroup(
                    atom.Position + dir * PairGroup.LonePairDistance,
                    isLonePair: true
                );
                AddGroupAndBond(lonePair, atom, 0);
            }
        }

        // Helpers

        private static void AddToList<T>(List<T> list, T item, bool addToFront)
        {
            if (addToFront) list.Insert(0, item);
            else            list.Add(item);
        }

        // Quaternion rotating unit vector 'from' onto unit vector 'to' (port of Matrix3.rotateAToB).
        private static quaternion FromToRotation(float3 from, float3 to)
        {
            from = math.normalize(from);
            to = math.normalize(to);
            float d = math.clamp(math.dot(from, to), -1f, 1f);
            if (d > 0.9999f) return quaternion.identity;
            if (d < -0.9999f)
            {
                // Antiparallel: rotate 180 degrees about any perpendicular axis.
                float3 axis = math.cross(from, new float3(1, 0, 0));
                if (math.lengthsq(axis) < 1e-6f) axis = math.cross(from, new float3(0, 1, 0));
                return quaternion.AxisAngle(math.normalize(axis), math.PI);
            }
            float3 c = math.cross(from, to);
            return math.normalize(new quaternion(c.x, c.y, c.z, 1f + d));
        }
    }
}

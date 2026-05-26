using System;
using Unity.Mathematics;

namespace Molecule_Shapes.Model
{
    public class PairGroup
    {
        // Constants ported from PairGroup.js
        public const float BondedPairDistance = 10f;
        public const float LonePairDistance = 7f;
        public const float ElectronPairRepulsionScale = 30000f;
        public const float AngleRepulsionScale = 3f;
        public const float JitterScale = 0.001f;
        public const float DampingFactor = 0.1f;

        private static int _nextId;
        public readonly int Id = _nextId++;

        public bool IsLonePair { get; }
        public bool IsCentralAtom { get; set; }
        public Element Element { get; }                 // null for generic
        public bool UserControlled { get; set; }

        // Replaces axon Property<Vector3>
        public float3 Position { get; private set; }
        public float3 Velocity { get; private set; }
        public float3 Orientation { get; private set; } // normalized Position

        public event Action<float3> PositionChanged;
        public event Action<float3> VelocityChanged;

        public PairGroup(float3 position, bool isLonePair, Element element = null)
        {
            IsLonePair = isLonePair;
            Element = element;
            SetPosition(position);
            SetVelocity(float3.zero);
        }

        public void SetPosition(float3 position)
        {
            Position = position;
            Orientation = math.lengthsq(position) > 0 ? math.normalize(position) : float3.zero;
            PositionChanged?.Invoke(position);
        }

        public void SetVelocity(float3 velocity)
        {
            Velocity = velocity;
            VelocityChanged?.Invoke(velocity);
        }

        public void AddPosition(float3 deltaPosition)
        {
            if (UserControlled || IsCentralAtom) return;
            SetPosition(Position + deltaPosition);
        }

        public void AddVelocity(float3 deltaVelocity)
        {
            if (UserControlled || IsCentralAtom) return;
            SetVelocity(Velocity + deltaVelocity);
        }

        // Port of stepForward(dt)
        public void StepForward(float deltaTime)
        {
            if (UserControlled) return;

            // strip outward radial component of velocity (keep motion tangent to sphere)
            if (math.lengthsq(Position) > 0)
            {
                float radial = math.dot(Velocity, Orientation);
                SetVelocity(Velocity - Orientation * radial);
            }

            SetPosition(Position + Velocity * deltaTime);

            // exponential damping, normalized to 0.017s reference frame
            float damping = math.pow(1f - DampingFactor, deltaTime / 0.017f);
            SetVelocity(Velocity * damping);
        }

        // Port of attractToIdealDistance(timeElapsed, oldDistance, bond) - damped spring toward ideal bond length.
        public void AttractToIdealDistance(float timeElapsed, float oldDistance, Bond bond)
        {
            if (UserControlled) return;

            float3 origin = bond.GetOtherAtom(this).Position;
            bool isTerminalLonePair = !origin.Equals(float3.zero);
            float idealDistanceFromCenter = bond.Length;

            // prevent movement away from our ideal distance
            float currentError = math.abs(math.length(Position - origin) - idealDistanceFromCenter);
            float oldError = math.abs(oldDistance - idealDistanceFromCenter);
            if (currentError > oldError)
            {
                // don't let it slide AWAY from the ideal distance; snap back to the old distance
                SetPosition(Orientation * oldDistance + origin);
            }

            // damped movement towards our ideal distance
            float3 toCenter = Position - origin;
            float distance = math.length(toCenter);
            if (distance != 0f)
            {
                float3 directionToCenter = toCenter / distance;
                float offset = idealDistanceFromCenter - distance;
                float ratioOfMovement = math.min(0.1f * timeElapsed / 0.016f, 1f);
                if (isTerminalLonePair) ratioOfMovement = 1f;
                SetPosition(Position + directionToCenter * (ratioOfMovement * offset));
            }
        }

        // Coulomb-style repulsion impulse FROM 'other' ON 'this'
        public float3 GetRepulsionImpulse(PairGroup other, float deltaTime, float trueLengthRatio)
        {
            if (math.distancesq(Position, other.Position) < 1e-12f)
                return float3.zero;

            float thisMagnitude = Lerp(BondedPairDistance, math.length(Position), trueLengthRatio);
            float otherMagnitude = Lerp(BondedPairDistance, math.length(other.Position), trueLengthRatio);

            float3 adjustedThis = Orientation * thisMagnitude;
            float3 adjustedOther = math.lengthsq(other.Position) == 0
                ? float3.zero
                : other.Orientation * otherMagnitude;

            float3 delta = adjustedThis - adjustedOther;
            float distance = math.length(delta);
            float3 coulombImpulse = (delta / distance) * (deltaTime * ElectronPairRepulsionScale / (distance * distance));

            return coulombImpulse * TimescaleImpulseFactor(deltaTime);
        }

        public void RepulseFrom(PairGroup other, float deltaTime, float trueLengthRatio)
            => AddVelocity(GetRepulsionImpulse(other, deltaTime, trueLengthRatio));

        public static float TimescaleImpulseFactor(float deltaTime)
            => math.sqrt(deltaTime > 0.017f ? 0.017f / deltaTime : 1f);

        private static float Lerp(float a, float b, float t)
            => a * (1f - t) + b * t;
    }
}

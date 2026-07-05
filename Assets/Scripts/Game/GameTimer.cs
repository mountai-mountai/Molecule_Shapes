// A pure stopwatch / countdown ticked with a delta time. Independent of the physics loop and of
// Unity - the view layer feeds it Time.deltaTime. Supports count-up (speedrun) and count-down
// (time pressure) modes.

namespace Molecule_Shapes.Game
{
    public enum TimerMode { CountUp, CountDown }

    public sealed class GameTimer
    {
        public TimerMode Mode { get; private set; }
        public float Duration { get; private set; }   // countdown length (seconds); ignored for CountUp
        public float Elapsed { get; private set; }
        public bool Running { get; private set; }

        // Seconds left for CountDown (clamped >= 0); for CountUp returns Elapsed.
        public float Remaining => Mode == TimerMode.CountDown
            ? (Duration - Elapsed > 0f ? Duration - Elapsed : 0f)
            : Elapsed;

        public bool Expired => Mode == TimerMode.CountDown && Elapsed >= Duration;

        public void StartCountUp()
        {
            Mode = TimerMode.CountUp;
            Elapsed = 0f;
            Running = true;
        }

        public void StartCountDown(float seconds)
        {
            Mode = TimerMode.CountDown;
            Duration = seconds;
            Elapsed = 0f;
            Running = true;
        }

        public void Tick(float dt)
        {
            if (!Running || dt <= 0f) return;
            Elapsed += dt;
            if (Expired) Running = false;   // stop at zero for countdown
        }

        public void Pause() => Running = false;
        public void Resume() => Running = true;
        public void Reset() { Elapsed = 0f; Running = false; }
    }
}

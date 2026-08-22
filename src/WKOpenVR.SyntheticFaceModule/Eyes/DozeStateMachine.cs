using WKOpenVR.SyntheticFaceModule.Head;

namespace WKOpenVR.SyntheticFaceModule.Eyes;

public enum DozeState
{
    Awake,
    Dozing,
    Asleep,
}

// Closing someone's eyes in the middle of a conversation is far worse than noticing they fell
// asleep a minute late, so every gate must hold continuously for a long dwell before the lids move,
// and any one of speech, a head lift, or a motion spike opens them again within a fifth of a second.
// Without a head pose there is no state machine at all: audio alone never closes the eyes.
public sealed class DozeStateMachine
{
    private const float DozeClosure = 0.70f;
    private const float SleepClosure = 0.93f;

    // Opening is capped below the tracked eyelid p99 of 13.4/s; closing is deliberately slower.
    private const float OpenRatePerSecond = 13.0f;
    private const float CloseRatePerSecond = 1.0f;

    private const float WakeSpeedRadPerSecond = 1.0f;
    private const float StillnessMaxMeanSquare = 0.01f;
    private const float BreathHz = 0.25f;
    private const float BreathAmplitude = 0.02f;

    private float _held;
    private float _breathPhase;

    public DozeState State { get; private set; } = DozeState.Awake;

    public float LidClosure { get; private set; }

    public bool Asleep => State == DozeState.Asleep;

    // Sleeping faces are not frozen faces: a little breathing keeps it from reading as a mannequin.
    public float Breath { get; private set; }

    public float BlinkRateScale => State == DozeState.Dozing ? 0.5f : 1f;

    public void Update(
        HeadMotionTracker head,
        bool speaking,
        float dtSeconds,
        bool enabled,
        float dozePitchRadians,
        float dozeDwellSeconds,
        float sleepDwellSeconds)
    {
        if (!enabled || !head.Valid)
        {
            State = DozeState.Awake;
            _held = 0f;
            Breath = 0f;
            _breathPhase = 0f;
            LidClosure = MoveToward(LidClosure, 0f, dtSeconds);
            return;
        }

        bool headDown = head.PitchBelowNeutral > dozePitchRadians;
        bool still = head.MeanSquareSpeed < StillnessMaxMeanSquare;
        bool gatesHold = headDown && still && !speaking;

        bool wake = speaking
            || head.Speed > WakeSpeedRadPerSecond
            || head.PitchBelowNeutral < dozePitchRadians * 0.5f;

        if (wake)
        {
            State = DozeState.Awake;
            _held = 0f;
        }
        else if (gatesHold)
        {
            _held += dtSeconds;
            if (State == DozeState.Awake && _held >= dozeDwellSeconds)
            {
                State = DozeState.Dozing;
                _held = 0f;
            }
            else if (State == DozeState.Dozing && _held >= sleepDwellSeconds)
            {
                State = DozeState.Asleep;
                _held = 0f;
            }
        }
        else
        {
            _held = 0f;
        }

        float target = State switch
        {
            DozeState.Dozing => DozeClosure,
            DozeState.Asleep => SleepClosure,
            _ => 0f,
        };

        LidClosure = MoveToward(LidClosure, target, dtSeconds);

        if (State == DozeState.Asleep)
        {
            _breathPhase += dtSeconds * BreathHz * 2f * MathF.PI;
            Breath = BreathAmplitude * (0.5f + (0.5f * MathF.Sin(_breathPhase)));
        }
        else
        {
            Breath = 0f;
            _breathPhase = 0f;
        }
    }

    public void Reset()
    {
        State = DozeState.Awake;
        _held = 0f;
        LidClosure = 0f;
        Breath = 0f;
        _breathPhase = 0f;
    }

    private static float MoveToward(float current, float target, float dtSeconds)
    {
        float step = (target > current ? CloseRatePerSecond : OpenRatePerSecond) * MathF.Max(0f, dtSeconds);
        if (MathF.Abs(target - current) <= step)
        {
            return target;
        }

        return target > current ? current + step : current - step;
    }
}

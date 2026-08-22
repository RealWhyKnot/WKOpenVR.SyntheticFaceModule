using System.Numerics;

namespace WKOpenVR.SyntheticFaceModule.Head;

// Rates come from differencing successive rotations: several HMD drivers report a zero
// vecAngularVelocity, so the reported value cannot be relied on.
public sealed class HeadMotionTracker
{
    private const float EnergyTauSeconds = 2f;
    private const float MaxPlausibleRate = 30f;

    // A pose this stale is a stalled driver, not a still head, so it counts as no pose at all.
    private const float StalePoseSeconds = 1f;

    private bool _hasPrevious;
    private Quaternion _previousRotation = Quaternion.Identity;
    private float _timeSincePrevious;
    private long _previousSampleIndex;
    private bool _wasMoving;
    private bool _stale;

    public bool Valid { get; private set; }

    // rad/s about the head's own up axis; positive turns the face left.
    public float YawRate { get; private set; }

    // rad/s about the head's own right axis; positive tips the face up.
    public float PitchRate { get; private set; }

    // rad/s.
    public float Speed { get; private set; }

    // Root mean square angular speed over a couple of seconds, rad/s.
    public float RmsSpeed => MathF.Sqrt(MathF.Max(0f, _meanSquareSpeed));

    public bool Moving { get; private set; }

    public bool MotionOnset { get; private set; }

    private float _meanSquareSpeed;

    public void Update(in HeadInput head, float dtSeconds, float movingThreshold)
    {
        MotionOnset = false;

        if (!head.Valid)
        {
            Reset();
            return;
        }

        _timeSincePrevious += MathF.Max(0f, dtSeconds);

        bool newSample = !_hasPrevious || head.SampleIndex != _previousSampleIndex;
        if (newSample)
        {
            // The gap either side of a stall is not a measurement, so no rate comes out of it.
            if (_hasPrevious && !_stale && _timeSincePrevious > 1e-4f)
            {
                Vector3 rate = LocalRate(_previousRotation, head.Rotation, _timeSincePrevious);
                if (rate.Length() > MaxPlausibleRate)
                {
                    rate = Vector3.Zero;
                }

                PitchRate = rate.X;
                YawRate = rate.Y;
                Speed = rate.Length();
            }

            _previousRotation = head.Rotation;
            _previousSampleIndex = head.SampleIndex;
            _timeSincePrevious = 0f;
            _hasPrevious = true;
            _stale = false;
        }
        else if (_timeSincePrevious > StalePoseSeconds)
        {
            // Only a genuinely new sample index clears this, so a frozen pose never reads as still.
            _stale = true;
        }

        if (_stale)
        {
            Valid = false;
            YawRate = 0f;
            PitchRate = 0f;
            Speed = 0f;
            _meanSquareSpeed = 0f;
            Moving = false;
            _wasMoving = false;
            return;
        }

        Valid = true;

        float energyAlpha = Alpha(dtSeconds, EnergyTauSeconds);
        _meanSquareSpeed += ((Speed * Speed) - _meanSquareSpeed) * energyAlpha;

        Moving = Speed > movingThreshold;
        MotionOnset = Moving && !_wasMoving;
        _wasMoving = Moving;
    }

    private void Reset()
    {
        Valid = false;
        YawRate = 0f;
        PitchRate = 0f;
        Speed = 0f;
        _meanSquareSpeed = 0f;
        Moving = false;
        _hasPrevious = false;
        _wasMoving = false;
        _stale = false;
        _timeSincePrevious = 0f;
    }

    // Rotation from the previous sample to this one, expressed in the head's own axes.
    private static Vector3 LocalRate(Quaternion previous, Quaternion current, float elapsedSeconds)
    {
        Quaternion delta = Quaternion.Normalize(Quaternion.Concatenate(Quaternion.Inverse(previous), current));
        if (delta.W < 0f)
        {
            delta = new Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W);
        }

        var axis = new Vector3(delta.X, delta.Y, delta.Z);
        float sinHalf = axis.Length();
        if (sinHalf < 1e-7f)
        {
            return Vector3.Zero;
        }

        float angle = 2f * MathF.Atan2(sinHalf, Math.Clamp(delta.W, -1f, 1f));
        return axis * (angle / (sinHalf * elapsedSeconds));
    }

    private static float Alpha(float dtSeconds, float tauSeconds)
    {
        if (dtSeconds <= 0f)
        {
            return 0f;
        }

        return 1f - MathF.Exp(-dtSeconds / tauSeconds);
    }
}

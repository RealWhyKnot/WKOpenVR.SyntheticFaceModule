using WKOpenVR.FaceTracking.Sdk;

namespace WKOpenVR.SyntheticFaceModule.Coloring;

/// <summary>
/// Always-on micro-expression layer. Real faces are never still: idle segments of
/// hardware recordings show constant small brow and eye-squint activity (roughly 12, 8,
/// and 19 events per minute on inner brow, outer brow, and squint, at amplitudes under
/// ~0.1) plus the occasional faint smile. This layer schedules those events with an
/// exponential-gap clock and shapes each one with a smooth rise/hold/fall envelope. It
/// runs every frame regardless of audio, so the face keeps breathing when the mic is
/// quiet, and it never writes viseme-critical shapes.
/// </summary>
public sealed class IdleMotionLayer
{
    private sealed class Channel
    {
        public required FaceExpression Right;
        public required FaceExpression Left;
        public required float EventsPerMinute;
        public required float AmplitudeMin;
        public required float AmplitudeMax;
        public required float DurationMinSeconds;
        public required float DurationMaxSeconds;

        public float SecondsToNextEvent;
        public float SecondsIntoEvent = -1f;
        public float EventDuration;
        public float EventAmplitude;
        public float Current;
    }

    private const float RiseFraction = 0.3f;
    private const float FallFraction = 0.4f;
    private const float ArousalRateBoost = 0.5f;

    private readonly Random _rng;
    private readonly Channel[] _channels;

    public IdleMotionLayer(Random rng)
    {
        _rng = rng;
        _channels =
        [
            new Channel
            {
                Right = FaceExpression.BrowInnerUpRight,
                Left = FaceExpression.BrowInnerUpLeft,
                EventsPerMinute = 12.1f,
                AmplitudeMin = 0.02f,
                AmplitudeMax = 0.06f,
                DurationMinSeconds = 0.6f,
                DurationMaxSeconds = 2.4f,
            },
            new Channel
            {
                Right = FaceExpression.BrowOuterUpRight,
                Left = FaceExpression.BrowOuterUpLeft,
                EventsPerMinute = 7.8f,
                AmplitudeMin = 0.01f,
                AmplitudeMax = 0.035f,
                DurationMinSeconds = 0.6f,
                DurationMaxSeconds = 2.0f,
            },
            new Channel
            {
                Right = FaceExpression.EyeSquintRight,
                Left = FaceExpression.EyeSquintLeft,
                EventsPerMinute = 19.2f,
                AmplitudeMin = 0.02f,
                AmplitudeMax = 0.08f,
                DurationMinSeconds = 0.5f,
                DurationMaxSeconds = 2.5f,
            },
            new Channel
            {
                Right = FaceExpression.MouthCornerPullRight,
                Left = FaceExpression.MouthCornerPullLeft,
                EventsPerMinute = 2.8f,
                AmplitudeMin = 0.01f,
                AmplitudeMax = 0.05f,
                DurationMinSeconds = 1.0f,
                DurationMaxSeconds = 3.0f,
            },
        ];

        foreach (Channel channel in _channels)
        {
            channel.SecondsToNextEvent = NextGapSeconds(channel, arousal: 0f);
        }
    }

    /// <summary>
    /// Clears <paramref name="offsets"/> and writes this frame's micro-motion into it,
    /// scaled by <paramref name="intensity"/>. <paramref name="arousal"/> modestly raises
    /// event rates during and shortly after speech.
    /// </summary>
    public void Update(float dtSeconds, float arousal, float intensity, float[] offsets)
    {
        Array.Clear(offsets);
        float scale = Math.Clamp(intensity, 0f, 1f);
        if (dtSeconds <= 0f)
        {
            return;
        }

        foreach (Channel channel in _channels)
        {
            if (channel.SecondsIntoEvent < 0f)
            {
                channel.SecondsToNextEvent -= dtSeconds;
                if (channel.SecondsToNextEvent <= 0f)
                {
                    channel.SecondsIntoEvent = 0f;
                    channel.EventDuration = Lerp(
                        channel.DurationMinSeconds, channel.DurationMaxSeconds, (float)_rng.NextDouble());
                    channel.EventAmplitude = Lerp(
                        channel.AmplitudeMin, channel.AmplitudeMax, (float)_rng.NextDouble());
                }
            }
            else
            {
                channel.SecondsIntoEvent += dtSeconds;
                if (channel.SecondsIntoEvent >= channel.EventDuration)
                {
                    channel.SecondsIntoEvent = -1f;
                    channel.SecondsToNextEvent = NextGapSeconds(channel, arousal);
                }
            }

            channel.Current = channel.SecondsIntoEvent >= 0f
                ? channel.EventAmplitude * Envelope(channel.SecondsIntoEvent / channel.EventDuration)
                : 0f;

            float value = channel.Current * scale;
            offsets[(int)channel.Right] = value;
            offsets[(int)channel.Left] = value;
        }
    }

    public void Reset()
    {
        foreach (Channel channel in _channels)
        {
            channel.SecondsIntoEvent = -1f;
            channel.Current = 0f;
            channel.SecondsToNextEvent = NextGapSeconds(channel, arousal: 0f);
        }
    }

    private float NextGapSeconds(Channel channel, float arousal)
    {
        float rate = channel.EventsPerMinute * (1f + (ArousalRateBoost * Math.Clamp(arousal, 0f, 1f))) / 60f;
        // Exponential inter-event gap; clamp the draw away from 0 so log() stays finite.
        double u = Math.Max(1e-6, _rng.NextDouble());
        return (float)(-Math.Log(u) / rate);
    }

    /// <summary>Smooth rise/hold/fall envelope over normalized event time [0,1].</summary>
    private static float Envelope(float t)
    {
        if (t < RiseFraction)
        {
            return SmoothStep(t / RiseFraction);
        }

        if (t > 1f - FallFraction)
        {
            return SmoothStep((1f - t) / FallFraction);
        }

        return 1f;
    }

    private static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }
}

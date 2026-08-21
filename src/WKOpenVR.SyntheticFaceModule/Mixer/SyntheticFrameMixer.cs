using WKOpenVR.FaceTracking.Sdk;
using WKOpenVR.SyntheticFaceModule.Eyes;

namespace WKOpenVR.SyntheticFaceModule.Mixer;

/// <summary>
/// Composes the per-layer outputs into a single <see cref="FaceFrame"/> with a strict priority and
/// final clamps. The mouth solver owns the viseme-critical jaw/lip shapes; the emotion layer is
/// additive and only writes non-mouth-core shapes, so summing them can never let coloring override a
/// viseme. Eyes are written symmetrically (conjugate gaze) and only when enabled. With no active
/// layer the frame is left neutral (safety/reset).
/// </summary>
public sealed class SyntheticFrameMixer
{
    /// <summary>
    /// Fills <paramref name="frame"/> from the layer buffers. <paramref name="mouth"/>,
    /// <paramref name="emotion"/>, and <paramref name="idle"/> are 88-length expression buffers
    /// (mouth core, additive coloring, and idle micro-motion); any may be inactive.
    /// <paramref name="eyes"/> is written to both eyes when present.
    /// </summary>
    public void Compose(
        FaceFrame frame,
        float[]? mouth,
        bool mouthActive,
        float[]? emotion,
        bool emotionActive,
        float[]? idle,
        bool idleActive,
        in EyeOutput? eyes)
    {
        frame.Clear();

        bool useMouth = mouthActive && mouth is not null;
        bool useEmotion = emotionActive && emotion is not null;
        bool useIdle = idleActive && idle is not null;
        if (useMouth || useEmotion || useIdle)
        {
            float[] expr = frame.Expressions;
            for (int i = 0; i < expr.Length; i++)
            {
                // Emotion and idle overlap on brow, squint, and corner shapes. Summing them let a
                // real expression and its idle jitter saturate the same shape, losing both; the
                // stronger one wins instead. Mouth shares no shape with either, so it adds.
                float coloring = Math.Max(useEmotion ? emotion![i] : 0f, useIdle ? idle![i] : 0f);
                float value = (useMouth ? mouth![i] : 0f) + coloring;
                expr[i] = Math.Clamp(value, 0f, 1f);
            }

            frame.Flags |= FaceFrameFlags.ExpressionsValid;
        }

        if (eyes is { } eye)
        {
            frame.Eye.Left.GazeX = Math.Clamp(eye.GazeX, -1f, 1f);
            frame.Eye.Left.GazeY = Math.Clamp(eye.GazeY, -1f, 1f);
            frame.Eye.Left.Openness = Math.Clamp(eye.Openness, 0f, 1f);
            frame.Eye.Left.PupilDiameterMm = eye.PupilMm;

            frame.Eye.Right.GazeX = frame.Eye.Left.GazeX;
            frame.Eye.Right.GazeY = frame.Eye.Left.GazeY;
            frame.Eye.Right.Openness = frame.Eye.Left.Openness;
            frame.Eye.Right.PupilDiameterMm = eye.PupilMm;

            frame.Eye.MinDilation = eye.MinDilationMm;
            frame.Eye.MaxDilation = eye.MaxDilationMm;

            frame.Flags |= FaceFrameFlags.EyeValid;
        }
    }
}

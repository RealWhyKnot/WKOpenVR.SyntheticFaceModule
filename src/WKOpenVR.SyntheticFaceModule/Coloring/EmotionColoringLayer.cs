using WKOpenVR.FaceTracking.Sdk;
using WKOpenVR.SyntheticFaceModule.Config;
using WKOpenVR.SyntheticFaceModule.Prosody;

namespace WKOpenVR.SyntheticFaceModule.Coloring;

// Maps vocal-tone events to timed expression episodes. Onset, duration, offset and peak per channel
// are pooled medians from tracked-face recordings. Nothing here follows the audio continuously;
// that is what made the old timbre-following coloring read as noise. The corners move only on
// laughter, because audio valence sits near chance.
//
// The layer never writes viseme-critical shapes: jaw, MouthClosed, funnel, pucker, stretch, and the
// upper/lower lip openers stay owned by the audio mouth solver.
public sealed class EmotionColoringLayer
{
    private sealed record Channel(Episode Episode, float Peak, (FaceExpression Shape, float Ratio)[] Shapes);

    private readonly Channel _question = new(new Episode(0.561f, 2.143f, 0.933f), 0.54f,
    [
        (FaceExpression.BrowInnerUpRight, 1f),
        (FaceExpression.BrowInnerUpLeft, 1f),
        (FaceExpression.BrowOuterUpRight, 0.9f),
        (FaceExpression.BrowOuterUpLeft, 0.9f),
    ]);

    private readonly Channel _emphasis = new(new Episode(0.513f, 1.862f, 0.607f), 0.48f,
    [
        (FaceExpression.BrowOuterUpRight, 1f),
        (FaceExpression.BrowOuterUpLeft, 1f),
    ]);

    // Eyes only: the 186 ms onset would move a brow faster than any tracked brow episode.
    private readonly Channel _engagement = new(new Episode(0.186f, 0.701f, 0.233f), 0.93f,
    [
        (FaceExpression.EyeWideRight, 1f),
        (FaceExpression.EyeWideLeft, 1f),
    ]);

    // No tracked row for a furrow; modest so it reads as thought, not a scowl.
    private readonly Channel _hesitation = new(new Episode(0.3f, 1.5f, 0.5f), 0.30f,
    [
        (FaceExpression.BrowLowererRight, 1f),
        (FaceExpression.BrowLowererLeft, 1f),
        (FaceExpression.BrowPinchRight, 1f),
        (FaceExpression.BrowPinchLeft, 1f),
    ]);

    // Corner slant tracks corner pull one-to-one and dimples follow at a fixed ratio, as trackers
    // report smiles; cheek and eye squint are the Duchenne pairing.
    private readonly Channel _laughter = new(new Episode(0.651f, 4.337f, 1.723f), 1.00f,
    [
        (FaceExpression.MouthCornerPullRight, 1f),
        (FaceExpression.MouthCornerPullLeft, 1f),
        (FaceExpression.MouthCornerSlantRight, 1f),
        (FaceExpression.MouthCornerSlantLeft, 1f),
        (FaceExpression.MouthDimpleRight, 0.37f),
        (FaceExpression.MouthDimpleLeft, 0.37f),
        (FaceExpression.CheekSquintRight, 0.55f),
        (FaceExpression.CheekSquintLeft, 0.55f),
        (FaceExpression.EyeSquintRight, 0.35f),
        (FaceExpression.EyeSquintLeft, 0.35f),
    ]);

    // Clears offsets and writes every active episode into it.
    public void Apply(ProsodyEvents events, SyntheticConfig config, float dtSeconds, float[] offsets)
    {
        Array.Clear(offsets);
        float master = Math.Clamp(config.EmotionIntensity, 0f, 2f);
        Drive(_question, events.Question, config.QuestionEnabled ? config.QuestionGain * master : 0f, dtSeconds, offsets);
        Drive(_emphasis, events.Emphasis, config.EmphasisEnabled ? config.EmphasisGain * master : 0f, dtSeconds, offsets);
        Drive(_engagement, events.Engagement, config.EngagementEnabled ? config.EngagementGain * master : 0f, dtSeconds, offsets);
        Drive(_hesitation, events.Hesitation, config.HesitationEnabled ? config.HesitationGain * master : 0f, dtSeconds, offsets);
        Drive(_laughter, events.Laughter, config.LaughterEnabled ? config.LaughterGain * master : 0f, dtSeconds, offsets);
    }

    public void Reset()
    {
        _question.Episode.Reset();
        _emphasis.Episode.Reset();
        _engagement.Episode.Reset();
        _hesitation.Episode.Reset();
        _laughter.Episode.Reset();
    }

    // Channels share shapes (outer brow sits in two), so the strongest wins, as in the mixer.
    private static void Drive(Channel channel, bool trigger, float gain, float dtSeconds, float[] offsets)
    {
        if (trigger && gain > 0f)
        {
            channel.Episode.Trigger();
        }

        float value = channel.Episode.Advance(dtSeconds) * channel.Peak * gain;
        if (value <= 0f)
        {
            return;
        }

        foreach ((FaceExpression shape, float ratio) in channel.Shapes)
        {
            int i = (int)shape;
            offsets[i] = Math.Max(offsets[i], Math.Clamp(value * ratio, 0f, 1f));
        }
    }
}

using WKOpenVR.SyntheticFaceModule.Audio;

namespace WKOpenVR.SyntheticFaceModule.Prosody;

public readonly record struct ProsodyEvents(bool Question, bool Emphasis, bool Engagement, bool Hesitation, bool Laughter);

// Turns the audio feature stream into discrete vocal-tone events. Every cue is judged against the
// speaker's own running baseline (z-scores), never an absolute level, and each event fires once
// per occurrence so downstream episodes carry their own timing.
public sealed class ProsodyEventDetector
{
    private const float ZClamp = 4f;
    private const float SampleWindowSeconds = 1.3f;

    // ln(Hz)/s; four semitones over 300 ms is 0.77.
    private const float QuestionWindowSeconds = 0.4f;
    private const int QuestionMinVoiced = 6;
    private const float QuestionSlopeMin = 0.45f;

    private const float EmphasisSalienceMin = 2.5f;
    private const float EmphasisLoudMinZ = 1.0f;

    private const float EngagementTauSeconds = 0.3f;
    private const float EngagementThreshold = 0.75f;
    private const float EngagementMinGapSeconds = 14f;

    private const float HesitationWindowSeconds = 0.6f;
    private const int HesitationMinSamples = 10;
    private const float HesitationMinVoicedFraction = 0.9f;
    private const float HesitationPitchStdMax = 0.05f;
    private const float HesitationOnsetZMax = -0.5f;

    private const float LaughOnsetRatio = 2f;
    private const int LaughOnsets = 4;
    private const float LaughIntervalMin = 0.125f;
    private const float LaughIntervalMax = 0.25f;
    private const float LaughIntervalSpreadMax = 1.4f;

    private readonly record struct Sample(double T, bool Voiced, float LogPitch, float OnsetZ);

    private readonly RunningBaseline _loudness = new(20f);
    private readonly RunningBaseline _pitch = new(20f);
    private readonly RunningBaseline _onset = new(20f);
    private readonly Queue<Sample> _samples = new();
    private readonly Queue<double> _laughOnsets = new();

    private double _lastT = double.NaN;
    private double _speechStartT;
    private double _lastEngagementT = double.NegativeInfinity;
    private bool _wasSpeech;
    private bool _hesitationArmed = true;
    private float _engagement;
    private float _prevRms;
    private float _salience1;
    private float _salience2;
    private float _loud1;

    public ProsodyEvents Update(AudioAnalysisFrame frame, bool isSpeech, float arousal)
    {
        double t = frame.TimestampSeconds;
        if (t == _lastT)
        {
            // The 120 Hz loop re-reads each 20 ms frame two or three times.
            return default;
        }

        float hop = double.IsNaN(_lastT) ? 0f : (float)Math.Clamp(t - _lastT, 0.0, 0.1);
        _lastT = t;

        bool question = false;
        if (isSpeech && !_wasSpeech)
        {
            _speechStartT = t;
            _hesitationArmed = true;
        }
        else if (!isSpeech && _wasSpeech)
        {
            question = RisingTerminal();
        }

        _wasSpeech = isSpeech;

        float engagementTarget = isSpeech ? Math.Clamp(arousal, 0f, 1f) : 0f;
        _engagement += (engagementTarget - _engagement) * (hop <= 0f ? 1f : 1f - MathF.Exp(-hop / EngagementTauSeconds));

        if (!isSpeech)
        {
            _samples.Clear();
            _laughOnsets.Clear();
            _salience1 = 0f;
            _salience2 = 0f;
            _prevRms = frame.Rms;
            return new ProsodyEvents(question, false, false, false, false);
        }

        ProsodyFeatures f = GemapsLiteFeatures.Extract(frame);
        float zLoud = Clamp(_loudness.Update(f.Loudness, hop));
        float zPitch = f.Voiced ? Clamp(_pitch.Update(f.LogPitch, hop)) : 0f;
        float zOnset = Clamp(_onset.Update(f.Onset, hop));

        _samples.Enqueue(new Sample(t, f.Voiced, f.LogPitch, zOnset));
        while (_samples.Count > 0 && _samples.Peek().T < t - SampleWindowSeconds)
        {
            _samples.Dequeue();
        }

        // Emphasis: a local maximum of loudness-plus-pitch salience. The loudness gate keeps a
        // pitch-only rise (question, engagement) from reading as stress.
        float salience = zLoud + zPitch;
        bool emphasis = _salience1 > _salience2 && _salience1 >= salience
            && _salience1 >= EmphasisSalienceMin && _loud1 >= EmphasisLoudMinZ;
        _salience2 = _salience1;
        _salience1 = salience;
        _loud1 = zLoud;

        bool engagement = false;
        if (_engagement >= EngagementThreshold && t - _lastEngagementT >= EngagementMinGapSeconds)
        {
            engagement = true;
            _lastEngagementT = t;
        }

        bool hesitation = false;
        bool monotone = t - _speechStartT >= HesitationWindowSeconds && IsMonotone(t);
        if (monotone && _hesitationArmed)
        {
            hesitation = true;
            _hesitationArmed = false;
        }
        else if (!monotone)
        {
            _hesitationArmed = true;
        }

        bool laughter = false;
        if (f.Voiced && _prevRms > 0f && frame.Rms >= LaughOnsetRatio * _prevRms)
        {
            _laughOnsets.Enqueue(t);
            while (_laughOnsets.Count > LaughOnsets)
            {
                _laughOnsets.Dequeue();
            }

            if (_laughOnsets.Count == LaughOnsets && IsRhythmic())
            {
                laughter = true;
                _laughOnsets.Clear();
            }
        }

        _prevRms = frame.Rms;
        return new ProsodyEvents(question, emphasis, engagement, hesitation, laughter);
    }

    public void Reset()
    {
        _loudness.Reset();
        _pitch.Reset();
        _onset.Reset();
        _samples.Clear();
        _laughOnsets.Clear();
        _lastT = double.NaN;
        _speechStartT = 0;
        _lastEngagementT = double.NegativeInfinity;
        _wasSpeech = false;
        _hesitationArmed = true;
        _engagement = 0f;
        _prevRms = 0f;
        _salience1 = 0f;
        _salience2 = 0f;
        _loud1 = 0f;
    }

    // Least-squares slope of log pitch over the voiced samples in the window ending at the last
    // voiced sample, so the VAD hangover only delays the verdict instead of diluting it.
    private bool RisingTerminal()
    {
        double lastVoiced = double.NegativeInfinity;
        foreach (Sample s in _samples)
        {
            if (s.Voiced)
            {
                lastVoiced = s.T;
            }
        }

        int n = 0;
        double sumT = 0, sumP = 0, sumTT = 0, sumTP = 0;
        foreach (Sample s in _samples)
        {
            if (!s.Voiced || s.T < lastVoiced - QuestionWindowSeconds)
            {
                continue;
            }

            double x = s.T - lastVoiced;
            n++;
            sumT += x;
            sumP += s.LogPitch;
            sumTT += x * x;
            sumTP += x * s.LogPitch;
        }

        if (n < QuestionMinVoiced)
        {
            return false;
        }

        double denominator = (n * sumTT) - (sumT * sumT);
        if (denominator <= 1e-9)
        {
            return false;
        }

        double slope = ((n * sumTP) - (sumT * sumP)) / denominator;
        return slope >= QuestionSlopeMin;
    }

    private bool IsMonotone(double t)
    {
        int count = 0;
        int voiced = 0;
        double sumPitch = 0, sumPitchSq = 0, sumOnset = 0;
        foreach (Sample s in _samples)
        {
            if (s.T < t - HesitationWindowSeconds)
            {
                continue;
            }

            count++;
            sumOnset += s.OnsetZ;
            if (s.Voiced)
            {
                voiced++;
                sumPitch += s.LogPitch;
                sumPitchSq += s.LogPitch * s.LogPitch;
            }
        }

        if (count < HesitationMinSamples || voiced < HesitationMinVoicedFraction * count)
        {
            return false;
        }

        double mean = sumPitch / voiced;
        double variance = Math.Max(0.0, (sumPitchSq / voiced) - (mean * mean));
        return Math.Sqrt(variance) <= HesitationPitchStdMax && sumOnset / count <= HesitationOnsetZMax;
    }

    private bool IsRhythmic()
    {
        double previous = double.NaN;
        double shortest = double.PositiveInfinity;
        double longest = 0;
        foreach (double onset in _laughOnsets)
        {
            if (!double.IsNaN(previous))
            {
                double interval = onset - previous;
                if (interval < LaughIntervalMin || interval > LaughIntervalMax)
                {
                    return false;
                }

                shortest = Math.Min(shortest, interval);
                longest = Math.Max(longest, interval);
            }

            previous = onset;
        }

        return longest <= shortest * LaughIntervalSpreadMax;
    }

    private static float Clamp(float z) => Math.Clamp(z, -ZClamp, ZClamp);
}

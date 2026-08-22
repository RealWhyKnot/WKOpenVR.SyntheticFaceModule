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

    // Prosodic stress does not recur every syllable, and the loud high-pitched calls of a laugh are
    // salience maxima too -- without these an entire laugh bout also reads as repeated emphasis.
    private const float EmphasisMinGapSeconds = 0.8f;

    // Arousal is a z-score against the speaker's own rolling 20 s baseline, so it re-centres on
    // whatever they are currently doing and cannot report a sustained state. Over a 10 min tracked
    // session it peaked at 0.61 and averaged 0.14, so the old 0.75 gate was unreachable. A gate low
    // enough to be reachable needs the slower time constant to go with it, or single pitch
    // transients trip a channel that is supposed to mean sustained animation. The min gap, not the
    // threshold, is what bounds how often this fires.
    private const float EngagementTauSeconds = 1.5f;
    private const float EngagementThreshold = 0.55f;
    private const float EngagementMinGapSeconds = 6f;

    private const float HesitationWindowSeconds = 0.6f;
    private const int HesitationMinSamples = 10;
    private const float HesitationMinVoicedFraction = 0.9f;
    private const float HesitationPitchStdMax = 0.05f;
    private const float HesitationOnsetZMax = -0.5f;

    // Laughter and ordinary speech share a 4-6 Hz syllable rate, so rhythm alone cannot separate
    // them; the pitch and loudness lift over the speaker's own baseline is what does. Bouts are
    // 1-3 s, well inside the 20 s baseline window, so that lift reads as a transient.
    private const float LaughOnsetRatio = 1.5f;
    private const int LaughOnsets = 3;
    private const float LaughIntervalMin = 0.09f;
    private const float LaughIntervalMax = 0.4f;
    private const float LaughIntervalSpreadMax = 2.2f;
    private const float LaughWindowSeconds = 1.5f;

    // A bout runs about a second and should read as one laugh, not one per call. The same window
    // suppresses emphasis, whose salience test the loud high-pitched calls would otherwise pass.
    private const float LaughRefractorySeconds = 1f;
    private const float LaughPitchMinZ = 0.8f;
    private const float LaughLoudMinZ = 0.5f;

    // Around half of natural laughter is breathy or unvoiced, and zPitch is reported as 0 on
    // unvoiced frames, so a pitch-only lift test can never fire on those bouts. They still carry a
    // loudness lift, so a louder one stands in for the pitch evidence.
    private const float LaughUnvoicedLoudMinZ = 1.3f;

    private readonly record struct Sample(double T, bool Voiced, float LogPitch, float OnsetZ);

    private readonly record struct LaughOnset(double T, float ZPitch, float ZLoud);

    private readonly RunningBaseline _loudness = new(20f);
    private readonly RunningBaseline _pitch = new(20f);
    private readonly RunningBaseline _onset = new(20f);
    private readonly Queue<Sample> _samples = new();
    private readonly Queue<LaughOnset> _laughOnsets = new();

    private double _lastT = double.NaN;
    private double _speechStartT;
    private double _lastEngagementT = double.NegativeInfinity;
    private double _lastEmphasisT = double.NegativeInfinity;
    private double _lastLaughT = double.NegativeInfinity;
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
            // Laugh onsets deliberately survive a VAD dropout: breathy calls dip below the speech
            // gate mid-bout, and clearing here threw away the rhythm evidence every time. The 1.5 s
            // prune and the 0.4 s interval ceiling already reject anything genuinely stale.
            _samples.Clear();
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
            && _salience1 >= EmphasisSalienceMin && _loud1 >= EmphasisLoudMinZ
            && t - _lastEmphasisT >= EmphasisMinGapSeconds
            && t - _lastLaughT >= LaughRefractorySeconds
            && !InRhythmicBurst();
        _salience2 = _salience1;
        _salience1 = salience;
        _loud1 = zLoud;
        if (emphasis)
        {
            _lastEmphasisT = t;
        }

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

        // Stale onsets used to sit in the queue indefinitely and force IsRhythmic() false forever.
        bool laughter = false;
        while (_laughOnsets.Count > 0 && _laughOnsets.Peek().T < t - LaughWindowSeconds)
        {
            _laughOnsets.Dequeue();
        }

        // Voicing is not required: a good share of real laughter is breathy or unvoiced.
        if (_prevRms > 0f && frame.Rms >= LaughOnsetRatio * _prevRms)
        {
            _laughOnsets.Enqueue(new LaughOnset(t, zPitch, zLoud));
            while (_laughOnsets.Count > LaughOnsets)
            {
                _laughOnsets.Dequeue();
            }

            if (_laughOnsets.Count == LaughOnsets
                && t - _lastLaughT >= LaughRefractorySeconds
                && IsRhythmic()
                && HasLaughLift())
            {
                laughter = true;
                _lastLaughT = t;
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
        _lastEmphasisT = double.NegativeInfinity;
        _lastLaughT = double.NegativeInfinity;
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
        foreach (LaughOnset onset in _laughOnsets)
        {
            if (!double.IsNaN(previous))
            {
                double interval = onset.T - previous;
                if (interval < LaughIntervalMin || interval > LaughIntervalMax)
                {
                    return false;
                }

                shortest = Math.Min(shortest, interval);
                longest = Math.Max(longest, interval);
            }

            previous = onset.T;
        }

        return longest <= shortest * LaughIntervalSpreadMax;
    }

    // True once a run of onsets is pacing itself like a laugh bout, before the full laughter
    // condition has been met.
    private bool InRhythmicBurst()
    {
        if (_laughOnsets.Count < 2)
        {
            return false;
        }

        double previous = double.NaN;
        double last = 0;
        foreach (LaughOnset onset in _laughOnsets)
        {
            last = previous;
            previous = onset.T;
        }

        double interval = previous - last;
        return interval >= LaughIntervalMin && interval <= LaughIntervalMax;
    }

    // Peak pitch rather than mean: a bout mixes voiced calls (which carry the lift) with unvoiced
    // ones, and zPitch is reported as 0 for the unvoiced frames.
    private bool HasLaughLift()
    {
        float peakPitch = float.NegativeInfinity;
        float sumLoud = 0f;
        foreach (LaughOnset onset in _laughOnsets)
        {
            peakPitch = Math.Max(peakPitch, onset.ZPitch);
            sumLoud += onset.ZLoud;
        }

        float meanLoud = sumLoud / _laughOnsets.Count;
        return meanLoud >= LaughLoudMinZ
            && (peakPitch >= LaughPitchMinZ || meanLoud >= LaughUnvoicedLoudMinZ);
    }

    private static float Clamp(float z) => Math.Clamp(z, -ZClamp, ZClamp);
}

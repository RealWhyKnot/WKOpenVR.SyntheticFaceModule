using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using WKOpenVR.FaceTracking.Sdk;
using WKOpenVR.SyntheticFaceModule;
using WKOpenVR.SyntheticFaceModule.Audio;
using WKOpenVR.SyntheticFaceModule.Coloring;
using WKOpenVR.SyntheticFaceModule.Config;
using WKOpenVR.SyntheticFaceModule.Dsp;
using WKOpenVR.SyntheticFaceModule.Dsp.Vad;
using WKOpenVR.SyntheticFaceModule.Eyes;
using WKOpenVR.SyntheticFaceModule.Mixer;
using WKOpenVR.SyntheticFaceModule.Mouth;
using WKOpenVR.SyntheticFaceModule.Prosody;
using WKOpenVR.SyntheticFaceModule.Ser;

var tests = new (string Name, Action Body)[]
{
    ("FFT peaks at sine frequency", FftPeaksAtSineFrequency),
    ("Spectral centroid rises with frequency", CentroidRisesWithFrequency),
    ("MFCC is finite for a tone", MfccIsFiniteForTone),
    ("Analyzer detects voiced tone", AnalyzerDetectsVoicedTone),
    ("VAD ignores low-level noise", VadIgnoresLowLevelNoise),
    ("VAD opens on loud signal", VadOpensOnLoudSignal),
    ("Asymmetric smoother attack faster than release", SmootherAttackFasterThanRelease),
    ("Mouth is neutral on silence", MouthNeutralOnSilence),
    ("Mouth rests between utterances", MouthRestsBetweenUtterances),
    ("Mouth opens on loud vowel", MouthOpensOnLoudVowel),
    ("Mouth lip openers follow jaw", MouthLipOpenersFollowJaw),
    ("Fricative damps jaw without a lip posture", FricativeDampsJawWithoutLipPosture),
    ("Lip postures are mutually exclusive", LipPosturesAreMutuallyExclusive),
    ("Closure stays quiet through an utterance", ClosureStaysQuietThroughUtterance),
    ("Mouth close does not fight open speech", MouthCloseDoesNotFightOpenSpeech),
    ("Mouth rounded vs front mapping", MouthRoundedVsFrontMapping),
    ("Lip posture duty cycles match tracked speech", LipPostureDutyCyclesMatchTrackedSpeech),
    ("Episodes respect tracked peaks and avoid mouth shapes", EpisodesRespectPeaksAndAvoidMouth),
    ("Only laughter moves the mouth corners", OnlyLaughterMovesCorners),
    ("Laughter mirrors tracker pairings", LaughterMirrorsTrackerPairings),
    ("Laughter rises fast and falls slow", LaughterRisesFastFallsSlow),
    ("Disabled laughter keeps corners still", DisabledLaughterKeepsCornersStill),
    ("Detector flags only the scripted event", DetectorFlagsOnlyTheScriptedEvent),
    ("Question raises inner brow", QuestionRaisesInnerBrow),
    ("Statement fires nothing", StatementFiresNothing),
    ("Emphasis flashes outer brow", EmphasisFlashesOuterBrow),
    ("Engagement widens eyes once", EngagementWidensEyesOnce),
    ("Hesitation furrows brow", HesitationFurrowsBrow),
    ("Laughter smiles with Duchenne ratios", LaughterSmilesWithDuchenneRatios),
    ("Disabled channel stays still", DisabledChannelStaysStill),
    ("Mixed script respects invariants", MixedScriptRespectsInvariants),
    ("Golden frames match", GoldenFramesMatch),
    ("Mixer composes mouth and emotion", MixerComposesMouthAndEmotion),
    ("Mixer coloring does not saturate", MixerColoringDoesNotSaturate),
    ("Mixer idle alone sets expressions", MixerIdleAloneSetsExpressions),
    ("Idle layer produces calibrated activity", IdleLayerProducesCalibratedActivity),
    ("Idle layer deterministic for seed", IdleLayerDeterministicForSeed),
    ("Mixer omits eye flag when no eyes", MixerOmitsEyeFlag),
    ("Mixer sets symmetric eyes", MixerSetsSymmetricEyes),
    ("Blink closes faster than it opens", BlinkClosesFasterThanOpens),
    ("Blink rate stays in the tracked band", BlinkRateIsNatural),
    ("Blink rate follows the configured rate", BlinkRateFollowsConfig),
    ("Gaze stays within cone", GazeStaysWithinCone),
    ("Saccades are paced fast", SaccadesArePacedFast),
    ("Procedural eyes are bounded", ProceduralEyesBounded),
    ("Pupil span follows arousal", PupilSpanFollowsArousal),
    ("Speaker baseline produces z-scores", SpeakerBaselineZScores),
    ("Heuristic arousal rises with loudness", HeuristicArousalRisesWithLoudness),
    ("Crossfade falls back to heuristic without model", CrossfadeFallsBackWithoutModel),
    ("Step is deterministic for seed", StepIsDeterministicForSeed),
    ("Module writes mouth frame", ModuleWritesMouthFrame),
    ("Module sets eye flag when eyes enabled", ModuleSetsEyeFlagWhenEnabled),
    ("Module drives eyes by default", ModuleDrivesEyesByDefault),
    ("Module reports healthy status when active", ModuleReportsHealthyStatus),
    ("Module reports no-channels status when idle", ModuleReportsNoChannelsStatus),
    ("Package dependencies are allowed", PackageDependenciesAreAllowed),
    ("Settings descriptor matches config defaults", SettingsDescriptorMatchesConfigDefaults),
};

if (args.Contains("--print-golden"))
{
    foreach (string line in GoldenLines(RunScript(NewModule(), MixedScript())))
    {
        Console.WriteLine("    \"" + line + "\",");
    }

    return;
}

foreach (var test in tests)
{
    test.Body();
    Console.WriteLine("PASS " + test.Name);
}

// Idle micro-motion peaks at ~0.1; anything above it during a scripted run is an episode.
const float IdleCeiling = 0.11f;

// ---- DSP ----

static void FftPeaksAtSineFrequency()
{
    const int sampleRate = 16000;
    const int fftSize = 512;
    var fft = new RealFft(fftSize);
    var magnitude = new float[fft.SpectrumLength];
    fft.MagnitudeSpectrum(Sine(1000f, fftSize, sampleRate), magnitude);

    int peakBin = 1;
    for (int k = 2; k < magnitude.Length; k++)
    {
        if (magnitude[k] > magnitude[peakBin])
        {
            peakBin = k;
        }
    }

    float binHz = sampleRate / (float)fftSize;
    AssertTrue(MathF.Abs((peakBin * binHz) - 1000f) <= binHz * 1.5f);
}

static void CentroidRisesWithFrequency()
{
    const int sampleRate = 16000;
    const int fftSize = 512;
    var fft = new RealFft(fftSize);
    var low = new float[fft.SpectrumLength];
    var high = new float[fft.SpectrumLength];
    fft.MagnitudeSpectrum(Sine(500f, fftSize, sampleRate), low);
    fft.MagnitudeSpectrum(Sine(3000f, fftSize, sampleRate), high);

    AssertTrue(SpectralFeatures.Centroid(high, sampleRate, fftSize) > SpectralFeatures.Centroid(low, sampleRate, fftSize));
}

static void MfccIsFiniteForTone()
{
    const int sampleRate = 16000;
    const int fftSize = 512;
    var fft = new RealFft(fftSize);
    var magnitude = new float[fft.SpectrumLength];
    fft.MagnitudeSpectrum(Sine(800f, fftSize, sampleRate), magnitude);

    var mfcc = new MfccExtractor(sampleRate, fftSize, 26, 13);
    var coeffs = new float[13];
    mfcc.Compute(magnitude, coeffs);

    foreach (float c in coeffs)
    {
        AssertTrue(float.IsFinite(c));
    }
}

static void AnalyzerDetectsVoicedTone()
{
    const int sampleRate = 16000;
    var analyzer = new AudioAnalyzer(sampleRate);
    var frame = new AudioAnalysisFrame(analyzer.MfccCount);
    analyzer.Analyze(Sine(150f, 512, sampleRate), 0.0, 0.032f, frame);

    AssertTrue(frame.Voiced);
    AssertTrue(frame.PitchHz > 130f && frame.PitchHz < 170f);
    AssertTrue(frame.Rms > 0.2f);
}

static void VadIgnoresLowLevelNoise()
{
    var detector = new SpeechActivityDetector();
    bool speech = false;
    for (int i = 0; i < 20; i++)
    {
        speech = detector.Update(rms: 0.002f, noiseFloor: 0.001f, dtSeconds: 0.02f);
    }

    AssertTrue(!speech);
    AssertEqual(0.0f, detector.Activity);
}

static void VadOpensOnLoudSignal()
{
    var detector = new SpeechActivityDetector();
    bool speech = detector.Update(rms: 0.05f, noiseFloor: 0.001f, dtSeconds: 0.02f);
    AssertTrue(speech);
    AssertTrue(detector.Activity > 0.0f);
}

static void SmootherAttackFasterThanRelease()
{
    var rising = new AsymmetricSmoother(attackSeconds: 0.02f, releaseSeconds: 0.2f, initial: 0f);
    float attackDelta = rising.Update(1.0f, 0.02f);

    var falling = new AsymmetricSmoother(attackSeconds: 0.02f, releaseSeconds: 0.2f, initial: 1f);
    float releaseDelta = 1.0f - falling.Update(0.0f, 0.02f);

    AssertTrue(attackDelta > releaseDelta);
}

// ---- Mouth ----

static void MouthNeutralOnSilence()
{
    var solver = new MouthSolver();
    var expr = new float[FaceExpressionCount.Value];
    var silence = new AudioAnalysisFrame(13) { Rms = 0f };
    for (int i = 0; i < 10; i++)
    {
        solver.Solve(silence, activity: 0f, dtSeconds: 0.02f, intensity: 1f, expr);
    }

    AssertTrue(expr[(int)FaceExpression.JawOpen] < 0.05f);
    AssertTrue(expr[(int)FaceExpression.MouthClosed] < 0.05f);
}

static void MouthOpensOnLoudVowel()
{
    var solver = new MouthSolver();
    var expr = new float[FaceExpressionCount.Value];
    var vowel = MakeVoiceFrame(rms: 0.3f, centroid: 1416f);
    for (int i = 0; i < 40; i++)
    {
        solver.Solve(vowel, activity: 1f, dtSeconds: 0.02f, intensity: 1f, expr);
    }

    AssertTrue(expr[(int)FaceExpression.JawOpen] > 0.3f);
}

static void MouthLipOpenersFollowJaw()
{
    var solver = new MouthSolver();
    var expr = new float[FaceExpressionCount.Value];
    var vowel = MakeVoiceFrame(rms: 0.3f, centroid: 1416f);
    for (int i = 0; i < 40; i++)
    {
        solver.Solve(vowel, activity: 1f, dtSeconds: 0.02f, intensity: 1f, expr);
    }

    float jaw = expr[(int)FaceExpression.JawOpen];
    AssertTrue(jaw > 0.3f);
    AssertEqual(jaw * 0.82f, expr[(int)FaceExpression.MouthUpperUpRight]);
    AssertEqual(expr[(int)FaceExpression.MouthUpperUpRight], expr[(int)FaceExpression.MouthUpperDeepenRight]);
    AssertEqual(jaw * 0.83f, expr[(int)FaceExpression.MouthLowerDownLeft]);
}

static void FricativeDampsJawWithoutLipPosture()
{
    var vowelSolver = new MouthSolver();
    var fricSolver = new MouthSolver();
    var vowelExpr = new float[FaceExpressionCount.Value];
    var fricExpr = new float[FaceExpressionCount.Value];
    var vowel = MakeVoiceFrame(rms: 0.3f, centroid: 1416f);
    var fricative = MakeVoiceFrame(rms: 0.25f, centroid: 3400f, pitch: 0f, voiced: false, zcr: 0.30f);
    for (int i = 0; i < 40; i++)
    {
        vowelSolver.Solve(vowel, activity: 1f, dtSeconds: 0.02f, intensity: 1f, vowelExpr);
        fricSolver.Solve(fricative, activity: 1f, dtSeconds: 0.02f, intensity: 1f, fricExpr);
    }

    AssertTrue(fricExpr[(int)FaceExpression.JawOpen] < vowelExpr[(int)FaceExpression.JawOpen]);
    AssertEqual(0f, fricExpr[(int)FaceExpression.LipFunnelUpperRight]);
    AssertEqual(0f, fricExpr[(int)FaceExpression.LipPuckerUpperRight]);
    AssertEqual(0f, fricExpr[(int)FaceExpression.MouthStretchRight]);
}

// Rounding and spreading never co-occur in hardware recordings (0 of 77851 sampled frames), so the
// solver must pick one posture rather than blending both.
static void LipPosturesAreMutuallyExclusive()
{
    var rounded = MakeVoiceFrame(rms: 0.3f, centroid: 400f);
    var front = MakeVoiceFrame(rms: 0.3f, centroid: 3200f);
    var solver = new MouthSolver();
    var expr = new float[FaceExpressionCount.Value];

    for (int step = 0; step < 200; step++)
    {
        solver.Solve(step % 40 < 20 ? rounded : front, activity: 1f, dtSeconds: 0.02f, intensity: 1f, expr);

        float rounding = Math.Max(
            expr[(int)FaceExpression.LipFunnelUpperRight],
            expr[(int)FaceExpression.LipPuckerUpperRight]);
        AssertTrue(rounding <= 0.0001f || expr[(int)FaceExpression.MouthStretchRight] <= 0.0001f);
    }
}

static void ClosureStaysQuietThroughUtterance()
{
    var solver = new MouthSolver();
    var expr = new float[FaceExpressionCount.Value];
    var vowel = MakeVoiceFrame(rms: 0.3f, centroid: 1416f);
    var silence = new AudioAnalysisFrame(13) { Rms = 0f };
    float peak = 0f;

    for (int i = 0; i < 10; i++)
    {
        solver.Solve(silence, activity: 0f, dtSeconds: 0.02f, intensity: 1f, expr);
    }

    // Onset must not read as a closure: the old solver spiked MouthClosed here every utterance.
    for (int i = 0; i < 60; i++)
    {
        solver.Solve(vowel, activity: 1f, dtSeconds: 0.02f, intensity: 1f, expr);
        peak = Math.Max(peak, expr[(int)FaceExpression.MouthClosed]);
    }

    AssertTrue(peak < 0.02f);
    AssertTrue(expr[(int)FaceExpression.JawOpen] > 0.3f);
}

static void MouthCloseDoesNotFightOpenSpeech()
{
    var solver = new MouthSolver();
    var expr = new float[FaceExpressionCount.Value];
    var vowel = MakeVoiceFrame(rms: 0.3f, centroid: 1416f);
    for (int i = 0; i < 40; i++)
    {
        solver.Solve(vowel, activity: 1f, dtSeconds: 0.02f, intensity: 1f, expr);
    }

    AssertTrue(expr[(int)FaceExpression.JawOpen] > 0.3f);
    AssertTrue(expr[(int)FaceExpression.MouthClosed] < 0.05f);
}

// Posture is judged against the speaker's own centroid baseline: a dip below it rounds the lips,
// a rise above it spreads them, and the baseline centroid itself does neither.
static void MouthRoundedVsFrontMapping()
{
    var neutral = MakeVoiceFrame(rms: 0.3f, centroid: 1200f);

    float[] Run(AudioAnalysisFrame probe)
    {
        var solver = new MouthSolver();
        var expr = new float[FaceExpressionCount.Value];
        for (int i = 0; i < 150; i++)
        {
            solver.Solve(neutral, 1f, 0.02f, 1f, expr);
        }

        AssertEqual(0f, expr[(int)FaceExpression.LipFunnelUpperRight]);
        AssertEqual(0f, expr[(int)FaceExpression.MouthStretchRight]);
        for (int i = 0; i < 40; i++)
        {
            solver.Solve(probe, 1f, 0.02f, 1f, expr);
        }

        return expr;
    }

    float[] rounded = Run(MakeVoiceFrame(rms: 0.3f, centroid: 400f));
    float[] front = Run(MakeVoiceFrame(rms: 0.3f, centroid: 2200f));

    AssertTrue(rounded[(int)FaceExpression.LipFunnelUpperRight] > 0.1f);
    AssertEqual(0f, rounded[(int)FaceExpression.MouthStretchRight]);
    AssertTrue(front[(int)FaceExpression.MouthStretchRight] > 0.1f);
    AssertEqual(0f, front[(int)FaceExpression.LipFunnelUpperRight]);
}

// Tracked speech holds a rounding posture in ~1% of speaking frames and a spread one in ~13%. A
// Gaussian spread of centroids around the speaker's mean (AR(1), ~100 ms correlation) must land in
// those bands instead of rounding every low vowel.
static void LipPostureDutyCyclesMatchTrackedSpeech()
{
    var rng = new Random(1);
    var solver = new MouthSolver();
    var expr = new float[FaceExpressionCount.Value];
    const int frames = 3000;
    int rounding = 0;
    int spreading = 0;
    float deviation = 0f;

    for (int i = 0; i < frames; i++)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        float gaussian = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        deviation = (0.8f * deviation) + (0.6f * 300f * gaussian);
        float centroid = Math.Max(250f, 1200f + deviation);
        solver.Solve(MakeVoiceFrame(rms: 0.3f, centroid: centroid), 1f, 0.02f, 1f, expr);

        bool rounded = Math.Max(
            expr[(int)FaceExpression.LipFunnelUpperRight],
            expr[(int)FaceExpression.LipPuckerUpperRight]) > 0.10f;
        bool spread = expr[(int)FaceExpression.MouthStretchRight] > 0.10f;
        AssertTrue(!(rounded && spread));
        if (rounded)
        {
            rounding++;
        }

        if (spread)
        {
            spreading++;
        }
    }

    float roundingFraction = rounding / (float)frames;
    float spreadingFraction = spreading / (float)frames;
    Console.WriteLine($"  posture duty: rounding={roundingFraction:P1} spreading={spreadingFraction:P1}");
    AssertTrue(roundingFraction < 0.03f);
    AssertTrue(spreadingFraction > 0.05f && spreadingFraction < 0.25f);
}

// ---- Expression episodes ----

static void EpisodesRespectPeaksAndAvoidMouth()
{
    var layer = new EmotionColoringLayer();
    var offsets = new float[FaceExpressionCount.Value];
    var all = new ProsodyEvents(true, true, true, true, true);
    float browInner = 0f;
    float eyeWide = 0f;
    float corner = 0f;
    for (int i = 0; i < 300; i++)
    {
        layer.Apply(i == 0 ? all : default, new SyntheticConfig(), dtSeconds: 0.02f, offsets);
        browInner = Math.Max(browInner, offsets[(int)FaceExpression.BrowInnerUpRight]);
        eyeWide = Math.Max(eyeWide, offsets[(int)FaceExpression.EyeWideRight]);
        corner = Math.Max(corner, offsets[(int)FaceExpression.MouthCornerPullRight]);
        // Viseme-critical shapes stay owned by the audio mouth solver.
        AssertEqual(0f, offsets[(int)FaceExpression.JawOpen]);
        AssertEqual(0f, offsets[(int)FaceExpression.MouthClosed]);
        AssertEqual(0f, offsets[(int)FaceExpression.LipFunnelUpperRight]);
        AssertEqual(0f, offsets[(int)FaceExpression.MouthStretchRight]);
        AssertEqual(0f, offsets[(int)FaceExpression.MouthTightenerRight]);
        AssertEqual(0f, offsets[(int)FaceExpression.MouthUpperUpRight]);
        AssertEqual(0f, offsets[(int)FaceExpression.MouthPressRight]);
    }

    AssertTrue(browInner > 0.5f && browInner <= 0.54f);
    AssertTrue(eyeWide > 0.9f && eyeWide <= 0.93f);
    AssertTrue(corner > 0.99f && corner <= 1f);
}

// Audio valence is near chance, so nothing but laughter reaches the corners.
static void OnlyLaughterMovesCorners()
{
    var layer = new EmotionColoringLayer();
    var offsets = new float[FaceExpressionCount.Value];
    var everythingElse = new ProsodyEvents(Question: true, Emphasis: true, Engagement: true, Hesitation: true, Laughter: false);
    for (int i = 0; i < 600; i++)
    {
        layer.Apply(i % 100 == 0 ? everythingElse : default, new SyntheticConfig(), dtSeconds: 0.02f, offsets);
        AssertEqual(0f, offsets[(int)FaceExpression.MouthCornerPullRight]);
        AssertEqual(0f, offsets[(int)FaceExpression.MouthFrownRight]);
        AssertEqual(0f, offsets[(int)FaceExpression.MouthDimpleRight]);
    }
}

static void LaughterMirrorsTrackerPairings()
{
    var layer = new EmotionColoringLayer();
    var offsets = new float[FaceExpressionCount.Value];
    float pull = RunToSmileAbove(layer, offsets, 0.4f);

    AssertTrue(pull > 0.4f);
    AssertEqual(pull, offsets[(int)FaceExpression.MouthCornerSlantRight]);
    AssertEqual(pull * 0.37f, offsets[(int)FaceExpression.MouthDimpleRight]);
    AssertEqual(pull * 0.55f, offsets[(int)FaceExpression.CheekSquintRight]);
    AssertEqual(pull * 0.35f, offsets[(int)FaceExpression.EyeSquintRight]);
    AssertEqual(0f, offsets[(int)FaceExpression.MouthFrownRight]);
}

static void LaughterRisesFastFallsSlow()
{
    var layer = new EmotionColoringLayer();
    var offsets = new float[FaceExpressionCount.Value];
    var config = new SyntheticConfig();
    layer.Apply(new ProsodyEvents(false, false, false, false, Laughter: true), config, 0.02f, offsets);

    float peak = offsets[(int)FaceExpression.MouthCornerPullRight];
    int toPeak = 0;
    while (true)
    {
        layer.Apply(default, config, 0.02f, offsets);
        float v = offsets[(int)FaceExpression.MouthCornerPullRight];
        if (v <= peak)
        {
            break;
        }

        peak = v;
        toPeak++;
    }

    int toRest = 0;
    while (offsets[(int)FaceExpression.MouthCornerPullRight] > 0f && toRest < 100000)
    {
        layer.Apply(default, config, 0.02f, offsets);
        toRest++;
    }

    AssertTrue(peak >= 0.99f && peak <= 1.0f);
    // Onset 0.65 s against a 1.7 s offset after a ~4.3 s episode, the tracked smile shape.
    AssertTrue(toPeak * 0.02f < 0.8f);
    AssertTrue(toRest * 0.02f > 1.5f);
}

static void DisabledLaughterKeepsCornersStill()
{
    var layer = new EmotionColoringLayer();
    var offsets = new float[FaceExpressionCount.Value];
    var config = new SyntheticConfig { LaughterEnabled = false };
    for (int i = 0; i < 2000; i++)
    {
        layer.Apply(new ProsodyEvents(false, false, false, false, Laughter: true), config, 0.02f, offsets);
        AssertEqual(0f, offsets[(int)FaceExpression.MouthCornerPullRight]);
        AssertEqual(0f, offsets[(int)FaceExpression.MouthDimpleRight]);
    }
}

static float RunToSmileAbove(EmotionColoringLayer layer, float[] offsets, float threshold)
{
    var config = new SyntheticConfig();
    layer.Apply(new ProsodyEvents(false, false, false, false, Laughter: true), config, 0.02f, offsets);
    for (int i = 0; i < 1000; i++)
    {
        layer.Apply(default, config, 0.02f, offsets);
        float v = offsets[(int)FaceExpression.MouthCornerPullRight];
        if (v > threshold)
        {
            return v;
        }
    }

    throw new InvalidOperationException("No laughter episode fired");
}

// ---- Vocal-tone events ----

static ScriptedAudio QuestionScript() => new ScriptedAudio().Speech(2f).Rise(0.3f, 150f, 220f).Silence(1.5f);

static ScriptedAudio StatementScript() => new ScriptedAudio().Speech(2f).Silence(1.5f);

static ScriptedAudio EmphasisScript() => new ScriptedAudio().Speech(1f).Speech(0.1f, rms: 0.3f).Speech(0.9f).Silence(0.5f);

static ScriptedAudio EngagementScript() => new ScriptedAudio().Speech(1.5f).Speech(1.5f, pitch: 220f).Silence(0.5f);

static ScriptedAudio HesitationScript() => new ScriptedAudio().Speech(2f).Monotone(1.2f).Silence(1f);

// Real laughter runs well above the speaker's modal pitch and louder than the speech around it.
// Pulsing at the same pitch and level is just rhythmic speech, which shares the same 4-6 Hz
// syllable rate; RhythmicSpeechScript covers that as the negative case.
static ScriptedAudio LaughterScript() =>
    new ScriptedAudio().Speech(2f).Pulses(1.2f, 5f, rms: 0.22f, pitch: 300f).Silence(1.5f);

static ScriptedAudio RhythmicSpeechScript() =>
    new ScriptedAudio().Speech(2f).Pulses(1.2f, 5f).Silence(1.5f);

static ScriptedAudio UnvoicedLaughterScript() =>
    new ScriptedAudio().Speech(2f).UnvoicedPulses(1.2f, 5f, rms: 0.3f).Silence(1.5f);

static void DetectorFlagsOnlyTheScriptedEvent()
{
    AssertEvents("question", RunDetector(QuestionScript(), 0f), question: 1);
    AssertEvents("statement", RunDetector(StatementScript(), 0f));
    AssertEvents("emphasis", RunDetector(EmphasisScript(), 0f), emphasis: -1);
    AssertEvents("engagement", RunDetector(new ScriptedAudio().Speech(3f), 0.95f), engagement: 1);
    AssertEvents("hesitation", RunDetector(HesitationScript(), 0f), hesitation: 1);
    // The single emphasis is the burst's first call, before its rhythm has established -- a brow
    // flash entering a laugh, which is what a face does anyway.
    AssertEvents("laughter", RunDetector(LaughterScript(), 0f), emphasis: 1, laughter: 1);
    AssertEvents("rhythmic speech", RunDetector(RhythmicSpeechScript(), 0f));
    // Roughly half of real laughter is unvoiced; a pitch-only lift test cannot see any of it.
    AssertEvents("unvoiced laughter", RunDetector(UnvoicedLaughterScript(), 0f), emphasis: 1, laughter: -1);
}

static void QuestionRaisesInnerBrow()
{
    List<StepRecord> run = RunScript(NewModule(), QuestionScript());
    AssertTrue(MaxOver(run, FaceExpression.BrowInnerUpLeft, 2.3f, 3.3f) >= 0.5f);
    AssertTrue(MaxOver(run, FaceExpression.BrowInnerUpLeft, 0f, 2.3f) < IdleCeiling);
    AssertQuiet(run, FaceExpression.EyeWideLeft, FaceExpression.BrowLowererLeft, FaceExpression.MouthCornerPullLeft);

    StepRecord peak = run.MaxBy(r => r.Expressions[(int)FaceExpression.BrowInnerUpLeft])!;
    AssertEqual(peak.Expressions[(int)FaceExpression.BrowInnerUpLeft], peak.Expressions[(int)FaceExpression.BrowInnerUpRight]);
}

static void StatementFiresNothing()
{
    List<StepRecord> run = RunScript(NewModule(), StatementScript());
    AssertQuiet(
        run,
        FaceExpression.BrowInnerUpLeft,
        FaceExpression.BrowOuterUpLeft,
        FaceExpression.EyeWideLeft,
        FaceExpression.BrowLowererLeft,
        FaceExpression.MouthCornerPullLeft);
}

static void EmphasisFlashesOuterBrow()
{
    List<StepRecord> run = RunScript(NewModule(), EmphasisScript());
    AssertTrue(MaxOver(run, FaceExpression.BrowOuterUpLeft, 1.0f, 1.9f) >= 0.4f);
    AssertTrue(MaxOver(run, FaceExpression.BrowOuterUpLeft, 0f, 1.0f) < IdleCeiling);
    AssertQuiet(run, FaceExpression.BrowInnerUpLeft, FaceExpression.EyeWideLeft, FaceExpression.BrowLowererLeft, FaceExpression.MouthCornerPullLeft);
}

static void EngagementWidensEyesOnce()
{
    List<StepRecord> run = RunScript(NewModule(), EngagementScript());
    AssertTrue(MaxOver(run, FaceExpression.EyeWideLeft, 1.5f, 3.5f) >= 0.8f);
    AssertTrue(MaxOver(run, FaceExpression.EyeWideLeft, 0f, 1.5f) < IdleCeiling);
    AssertCount(1, RisingEdges(run, FaceExpression.EyeWideLeft), "eye-wide rising edges");
    AssertQuiet(run, FaceExpression.BrowInnerUpLeft, FaceExpression.BrowLowererLeft, FaceExpression.MouthCornerPullLeft);
}

static void HesitationFurrowsBrow()
{
    List<StepRecord> run = RunScript(NewModule(), HesitationScript());
    AssertTrue(MaxOver(run, FaceExpression.BrowLowererLeft, 2.2f, 3.4f) >= 0.25f);
    AssertTrue(MaxOver(run, FaceExpression.BrowLowererLeft, 0f, 2.2f) < IdleCeiling);
    AssertCount(1, RisingEdges(run, FaceExpression.BrowLowererLeft), "brow-lowerer rising edges");
    AssertQuiet(run, FaceExpression.BrowInnerUpLeft, FaceExpression.BrowOuterUpLeft, FaceExpression.EyeWideLeft, FaceExpression.MouthCornerPullLeft);

    StepRecord peak = run.MaxBy(r => r.Expressions[(int)FaceExpression.BrowLowererLeft])!;
    AssertEqual(peak.Expressions[(int)FaceExpression.BrowLowererLeft], peak.Expressions[(int)FaceExpression.BrowPinchLeft]);
}

// The envelope smoothers decay exponentially, so before the rest knee every mouth shape stayed
// faintly non-zero forever: JawOpen was non-zero in 90.9% of frames of a tracked session, mean 0.05,
// which reads as a permanent mumble rather than speech.
static void MouthRestsBetweenUtterances()
{
    List<StepRecord> run = RunScript(
        NewModule(),
        new ScriptedAudio().Speech(2f).Silence(2f).Speech(2f).Silence(2f));

    int nonZero = run.Count(r => r.Expressions[(int)FaceExpression.JawOpen] > 0f);
    float duty = (float)nonZero / run.Count;
    Console.WriteLine($"  jaw duty: {duty * 100f:F1}% non-zero");
    AssertTrue(duty < 0.55f);

    // Deep in each silence the mouth must be exactly closed, not merely small.
    AssertEqual(0f, MaxOver(run, FaceExpression.JawOpen, 3f, 4f));
    AssertEqual(0f, MaxOver(run, FaceExpression.MouthClosed, 3f, 4f));
    AssertEqual(0f, MaxOver(run, FaceExpression.MouthUpperUpLeft, 3f, 4f));
    AssertEqual(0f, MaxOver(run, FaceExpression.MouthLowerDownLeft, 3f, 4f));
}

static void LaughterSmilesWithDuchenneRatios()
{
    List<StepRecord> run = RunScript(NewModule(), LaughterScript());
    AssertTrue(MaxOver(run, FaceExpression.MouthCornerPullLeft, 2.4f, 3.8f) >= 0.9f);
    AssertTrue(MaxOver(run, FaceExpression.MouthCornerPullLeft, 0f, 2.4f) < IdleCeiling);
    AssertQuiet(run, FaceExpression.BrowInnerUpLeft, FaceExpression.EyeWideLeft, FaceExpression.BrowLowererLeft);

    // Entering a laugh reads as one emphasis: the first call is a large pitch accent before the
    // bout's rhythm has established. One flash at the emphasis peak, not a brow per call.
    AssertCount(1, RisingEdges(run, FaceExpression.BrowOuterUpLeft), "outer-brow rising edges");
    AssertTrue(MaxOver(run, FaceExpression.BrowOuterUpLeft, 0f, 8f) <= 0.49f);

    StepRecord peak = run.MaxBy(r => r.Expressions[(int)FaceExpression.MouthCornerPullLeft])!;
    float pull = peak.Expressions[(int)FaceExpression.MouthCornerPullLeft];
    AssertEqual(pull, peak.Expressions[(int)FaceExpression.MouthCornerSlantLeft]);
    AssertEqual(pull * 0.37f, peak.Expressions[(int)FaceExpression.MouthDimpleLeft]);
    AssertEqual(pull * 0.55f, peak.Expressions[(int)FaceExpression.CheekSquintLeft]);
    AssertEqual(pull * 0.35f, peak.Expressions[(int)FaceExpression.EyeSquintLeft]);
}

static void DisabledChannelStaysStill()
{
    List<StepRecord> muted = RunScript(NewModule(new SyntheticConfig { LaughterEnabled = false }), LaughterScript());
    AssertQuiet(muted, FaceExpression.MouthCornerPullLeft);

    List<StepRecord> half = RunScript(NewModule(new SyntheticConfig { QuestionGain = 0.5f }), QuestionScript());
    float peak = MaxOver(half, FaceExpression.BrowInnerUpLeft, 2.3f, 3.3f);
    AssertTrue(peak >= 0.25f && peak <= 0.30f);
}

static List<ProsodyEvents> RunDetector(ScriptedAudio script, float arousal)
{
    var detector = new ProsodyEventDetector();
    var events = new List<ProsodyEvents>(script.Frames.Count);
    foreach (AudioAnalysisFrame frame in script.Frames)
    {
        events.Add(detector.Update(frame, isSpeech: frame.Rms > 0.01f, arousal));
    }

    return events;
}

// Expected counts: exact when >= 0, "at least one" when -1.
static void AssertEvents(string script, List<ProsodyEvents> events, int question = 0, int emphasis = 0, int engagement = 0, int hesitation = 0, int laughter = 0)
{
    var actual = new[]
    {
        events.Count(e => e.Question),
        events.Count(e => e.Emphasis),
        events.Count(e => e.Engagement),
        events.Count(e => e.Hesitation),
        events.Count(e => e.Laughter),
    };
    var expected = new[] { question, emphasis, engagement, hesitation, laughter };
    for (int i = 0; i < 5; i++)
    {
        bool ok = expected[i] < 0 ? actual[i] >= 1 : actual[i] == expected[i];
        if (!ok)
        {
            string where = string.Join(" ", events
                .Select((e, k) => (e, k))
                .Where(x => x.e != default)
                .Select(x => $"{x.k * 0.02f:F2}s:{(x.e.Question ? "q" : "")}{(x.e.Emphasis ? "e" : "")}{(x.e.Engagement ? "g" : "")}{(x.e.Hesitation ? "h" : "")}{(x.e.Laughter ? "l" : "")}"));
            throw new InvalidOperationException(
                $"{script}: events q/e/g/h/l = {string.Join("/", actual)}, expected {string.Join("/", expected)}; at {where}");
        }
    }
}

static void AssertQuiet(List<StepRecord> run, params FaceExpression[] shapes)
{
    foreach (FaceExpression shape in shapes)
    {
        float max = MaxOver(run, shape, 0f, run.Count / 120f + 1f);
        if (max >= IdleCeiling)
        {
            throw new InvalidOperationException($"{shape} reached {max:F3}, above the idle ceiling");
        }
    }
}

static float MaxOver(List<StepRecord> records, FaceExpression shape, float fromSeconds, float toSeconds)
{
    float max = 0f;
    for (int k = (int)(fromSeconds * 120f); k < Math.Min(records.Count, (int)(toSeconds * 120f)); k++)
    {
        max = Math.Max(max, records[k].Expressions[(int)shape]);
    }

    return max;
}

static int RisingEdges(List<StepRecord> records, FaceExpression shape, float level = IdleCeiling)
{
    int edges = 0;
    bool above = false;
    foreach (StepRecord record in records)
    {
        bool now = record.Expressions[(int)shape] > level;
        if (now && !above)
        {
            edges++;
        }

        above = now;
    }

    return edges;
}

// ---- Output stream guards ----

static ScriptedAudio MixedScript()
{
    var script = new ScriptedAudio();
    for (int cycle = 0; cycle < 2; cycle++)
    {
        script
            .Speech(2f).Rise(0.3f, 150f, 220f).Silence(1.5f)
            .Speech(2f).Silence(1.5f)
            .Speech(1f).Speech(0.1f, rms: 0.3f).Speech(0.9f).Silence(0.5f)
            .Speech(1.5f).Speech(1.5f, pitch: 220f).Silence(0.5f)
            .Speech(2f).Monotone(1.2f).Silence(1f)
            .Speech(2f).Pulses(1.2f, 5f, rms: 0.22f, pitch: 300f).Silence(1.5f);
    }

    return script.Silence(16f);
}

// Largest change one 120 Hz step may make. Expression families use the tracked p99.9 slew; mouth
// shapes use the solver's own attack, and gaze a no-teleport bound (a 21 Hz tracker cannot time a
// 30 ms saccade).
static float StepCap(FaceExpression shape)
{
    string name = shape.ToString();
    if (name.StartsWith("Jaw") || name.StartsWith("Lip") || name.StartsWith("MouthUpper")
        || name.StartsWith("MouthLower") || name.StartsWith("MouthClosed") || name.StartsWith("MouthStretch"))
    {
        return 0.25f;
    }

    if (name.StartsWith("EyeSquint") || name.StartsWith("EyeWide"))
    {
        return 0.08f;
    }

    if (name.StartsWith("MouthCorner") || name.StartsWith("CheekSquint") || name.StartsWith("MouthDimple"))
    {
        return 0.06f;
    }

    return 0.02f;
}

static void MixedScriptRespectsInvariants()
{
    List<StepRecord> run = RunScript(NewModule(), MixedScript());
    AssertTrue(run.Count > 60 * 120);

    var railSteps = new int[FaceExpressionCount.Value];
    for (int k = 0; k < run.Count; k++)
    {
        StepRecord now = run[k];
        AssertTrue(float.IsFinite(now.Openness) && now.Openness >= 0f && now.Openness <= 1f);
        AssertTrue(float.IsFinite(now.GazeX) && MathF.Abs(now.GazeX) <= 1f);
        AssertTrue(float.IsFinite(now.GazeY) && MathF.Abs(now.GazeY) <= 1f);

        float rounding = Math.Max(now.Expressions[(int)FaceExpression.LipFunnelUpperRight], now.Expressions[(int)FaceExpression.LipPuckerUpperRight]);
        AssertTrue(rounding <= 0.10f || now.Expressions[(int)FaceExpression.MouthStretchRight] <= 0.10f);

        for (int i = 0; i < now.Expressions.Length; i++)
        {
            float v = now.Expressions[i];
            AssertTrue(float.IsFinite(v) && v >= 0f && v <= 1f);
            railSteps[i] = v > 0.95f ? railSteps[i] + 1 : 0;
            if (railSteps[i] > 6 * 120)
            {
                throw new InvalidOperationException($"{(FaceExpression)i} pinned above 0.95 for over 6 s at step {k}");
            }

            if (k == 0)
            {
                continue;
            }

            float delta = MathF.Abs(v - run[k - 1].Expressions[i]);
            if (delta > StepCap((FaceExpression)i))
            {
                throw new InvalidOperationException($"{(FaceExpression)i} jumped {delta:F3} in one step at {k / 120f:F2}s");
            }
        }

        if (k > 0)
        {
            AssertTrue(MathF.Abs(now.Openness - run[k - 1].Openness) <= 0.18f);
            float gazeStep = MathF.Sqrt(
                ((now.GazeX - run[k - 1].GazeX) * (now.GazeX - run[k - 1].GazeX)) +
                ((now.GazeY - run[k - 1].GazeY) * (now.GazeY - run[k - 1].GazeY)));
            AssertTrue(gazeStep <= 0.2f);
        }
    }

    // The closing 4 s of silence must carry nothing above idle amplitude.
    float tail = run.Count / 120f;
    for (int i = 0; i < FaceExpressionCount.Value; i++)
    {
        float max = MaxOver(run, (FaceExpression)i, tail - 4f, tail + 1f);
        if (max >= IdleCeiling)
        {
            throw new InvalidOperationException($"{(FaceExpression)i} reached {max:F3} during the closing silence");
        }
    }
}

// Top-8 shapes at ten sampled steps of the mixed script under the fixed test seed. Regenerate with
// `dotnet run --project tests/WKOpenVR.SyntheticFaceModule.Tests -- --print-golden` after an
// intended change to the parameter stream.
static string[] Golden() =>
[
    "22:0.095,50:0.079,51:0.079,44:0.078,45:0.078,46:0.078,47:0.078,0:0.000",
    "22:0.399,50:0.331,51:0.331,44:0.327,45:0.327,46:0.327,47:0.327,10:0.135",
    "0:0.059,1:0.059,10:0.031,11:0.031,2:0.000,3:0.000,4:0.000,5:0.000",
    "10:0.478,11:0.478,22:0.096,50:0.080,51:0.080,44:0.079,45:0.079,46:0.079",
    "8:0.390,9:0.390,10:0.351,11:0.351,0:0.014,1:0.014,2:0.000,3:0.000",
    "22:0.400,50:0.332,51:0.332,44:0.328,45:0.328,46:0.328,47:0.328,8:0.059",
    "2:0.422,3:0.422,10:0.037,11:0.037,0:0.000,1:0.000,4:0.000,5:0.000",
    "22:0.400,50:0.332,51:0.332,44:0.328,45:0.328,46:0.328,47:0.328,8:0.055",
    "56:0.930,57:0.930,58:0.930,59:0.930,16:0.512,17:0.512,64:0.344,65:0.344",
    "0:0.000,1:0.000,2:0.000,3:0.000,4:0.000,5:0.000,6:0.000,7:0.000",
];

static string[] GoldenLines(List<StepRecord> run)
{
    var lines = new List<string>();
    for (int step = 600; step <= 6000; step += 600)
    {
        float[] e = run[step].Expressions;
        IEnumerable<string> top = Enumerable.Range(0, e.Length)
            .OrderByDescending(i => e[i])
            .ThenBy(i => i)
            .Take(8)
            .Select(i => $"{i}:{e[i].ToString("F3", CultureInfo.InvariantCulture)}");
        lines.Add(string.Join(",", top));
    }

    return [.. lines];
}

static void GoldenFramesMatch()
{
    string[] golden = Golden();
    string[] actual = GoldenLines(RunScript(NewModule(), MixedScript()));
    AssertCount(golden.Length, actual.Length, "golden lines");
    for (int n = 0; n < actual.Length; n++)
    {
        string[] want = golden[n].Split(',');
        string[] got = actual[n].Split(',');
        for (int j = 0; j < want.Length; j++)
        {
            string[] w = want[j].Split(':');
            string[] g = got[j].Split(':');
            float wv = float.Parse(w[1], CultureInfo.InvariantCulture);
            float gv = float.Parse(g[1], CultureInfo.InvariantCulture);
            if (w[0] != g[0] || MathF.Abs(wv - gv) > 0.002f)
            {
                throw new InvalidOperationException($"golden line {n} entry {j}: expected {want[j]} got {got[j]}");
            }
        }
    }
}

// ---- Mixer ----

static void MixerComposesMouthAndEmotion()
{
    var mixer = new SyntheticFrameMixer();
    var frame = new FaceFrame();
    var mouth = new float[FaceExpressionCount.Value];
    var emotion = new float[FaceExpressionCount.Value];
    mouth[(int)FaceExpression.JawOpen] = 0.5f;
    emotion[(int)FaceExpression.MouthCornerPullRight] = 0.2f;

    var idle = new float[FaceExpressionCount.Value];
    idle[(int)FaceExpression.BrowInnerUpRight] = 0.04f;
    idle[(int)FaceExpression.MouthCornerPullRight] = 0.1f;

    mixer.Compose(frame, mouth, mouthActive: true, emotion, emotionActive: true, idle, idleActive: true, eyes: null);

    AssertTrue((frame.Flags & FaceFrameFlags.ExpressionsValid) != 0);
    AssertTrue((frame.Flags & FaceFrameFlags.EyeValid) == 0);
    AssertEqual(0.5f, frame.GetExpression(FaceExpression.JawOpen));
    // Emotion and idle contend for the same shape; the stronger one wins rather than summing.
    AssertEqual(0.2f, frame.GetExpression(FaceExpression.MouthCornerPullRight));
    AssertEqual(0.04f, frame.GetExpression(FaceExpression.BrowInnerUpRight));
}

// A full-strength expression plus its idle jitter used to sum past 1.0 and clamp, losing both.
static void MixerColoringDoesNotSaturate()
{
    var mixer = new SyntheticFrameMixer();
    var frame = new FaceFrame();
    var emotion = new float[FaceExpressionCount.Value];
    var idle = new float[FaceExpressionCount.Value];
    emotion[(int)FaceExpression.EyeSquintRight] = 0.96f;
    idle[(int)FaceExpression.EyeSquintRight] = 0.058f;

    mixer.Compose(frame, null, mouthActive: false, emotion, emotionActive: true, idle, idleActive: true, eyes: null);

    AssertEqual(0.96f, frame.GetExpression(FaceExpression.EyeSquintRight));
}

static void IdleLayerProducesCalibratedActivity()
{
    var layer = new IdleMotionLayer(new Random(7));
    var offsets = new float[FaceExpressionCount.Value];
    int browEvents = 0;
    bool browAbove = false;
    float browMax = 0f;
    float cornerMax = 0f;
    const float dt = 1f / 60f;
    for (int i = 0; i < 120 * 60; i++)
    {
        layer.Update(dt, arousal: 0f, intensity: 1f, offsets);

        float brow = offsets[(int)FaceExpression.BrowInnerUpLeft];
        browMax = Math.Max(browMax, brow);
        cornerMax = Math.Max(cornerMax, offsets[(int)FaceExpression.MouthCornerPullLeft]);
        if (brow > 0.01f && !browAbove)
        {
            browEvents++;
        }

        browAbove = brow > 0.01f;

        // Viseme-critical shapes must never move.
        AssertEqual(0f, offsets[(int)FaceExpression.JawOpen]);
        AssertEqual(0f, offsets[(int)FaceExpression.MouthClosed]);
        AssertEqual(0f, offsets[(int)FaceExpression.LipFunnelUpperRight]);
    }

    // Two simulated minutes at ~16 events/min; allow wide slack for the random draw.
    AssertTrue(browEvents is > 15 and < 60);
    AssertTrue(browMax <= 0.083f);
    AssertTrue(cornerMax <= 0.093f);

    // Symmetry: both sides always carry the same value.
    AssertEqual(
        offsets[(int)FaceExpression.BrowInnerUpLeft],
        offsets[(int)FaceExpression.BrowInnerUpRight]);
}

static void IdleLayerDeterministicForSeed()
{
    var a = new IdleMotionLayer(new Random(42));
    var b = new IdleMotionLayer(new Random(42));
    var bufA = new float[FaceExpressionCount.Value];
    var bufB = new float[FaceExpressionCount.Value];
    const float dt = 1f / 60f;
    for (int i = 0; i < 600; i++)
    {
        a.Update(dt, arousal: 0.3f, intensity: 1f, bufA);
        b.Update(dt, arousal: 0.3f, intensity: 1f, bufB);
        for (int s = 0; s < bufA.Length; s++)
        {
            AssertEqual(bufA[s], bufB[s]);
        }
    }
}

static void MixerIdleAloneSetsExpressions()
{
    var mixer = new SyntheticFrameMixer();
    var frame = new FaceFrame();
    var idle = new float[FaceExpressionCount.Value];
    idle[(int)FaceExpression.BrowInnerUpLeft] = 0.05f;

    mixer.Compose(frame, mouth: null, mouthActive: false, emotion: null, emotionActive: false, idle, idleActive: true, eyes: null);

    AssertTrue((frame.Flags & FaceFrameFlags.ExpressionsValid) != 0);
    AssertEqual(0.05f, frame.GetExpression(FaceExpression.BrowInnerUpLeft));
}

static void MixerOmitsEyeFlag()
{
    var mixer = new SyntheticFrameMixer();
    var frame = new FaceFrame();
    var mouth = new float[FaceExpressionCount.Value];
    mouth[(int)FaceExpression.JawOpen] = 0.3f;

    mixer.Compose(frame, mouth, mouthActive: true, emotion: null, emotionActive: false, idle: null, idleActive: false, eyes: null);

    AssertTrue((frame.Flags & FaceFrameFlags.EyeValid) == 0);
}

static void MixerSetsSymmetricEyes()
{
    var mixer = new SyntheticFrameMixer();
    var frame = new FaceFrame();
    var eye = new EyeOutput(Openness: 0.7f, GazeX: 0.2f, GazeY: -0.1f, PupilMm: 4f, MinDilationMm: 3f, MaxDilationMm: 5f);

    mixer.Compose(frame, mouth: null, mouthActive: false, emotion: null, emotionActive: false, idle: null, idleActive: false, eye);

    AssertTrue((frame.Flags & FaceFrameFlags.EyeValid) != 0);
    AssertEqual(frame.Eye.Left.Openness, frame.Eye.Right.Openness);
    AssertEqual(frame.Eye.Left.GazeX, frame.Eye.Right.GazeX);
    AssertEqual(0.7f, frame.Eye.Left.Openness);
}

// ---- Eyes ----

static void BlinkClosesFasterThanOpens()
{
    var blink = new BlinkScheduler(new Random(11));
    const float dt = 0.005f;
    blink.RequestBlinkSoon();

    int guard = 0;
    while (blink.Openness >= 0.999f && guard++ < 20000)
    {
        blink.Update(dt);
    }

    float closeTime = 0f;
    while (blink.Openness > 0.001f && guard++ < 20000)
    {
        blink.Update(dt);
        closeTime += dt;
    }

    float openTime = 0f;
    while (blink.Openness < 0.999f && guard++ < 20000)
    {
        blink.Update(dt);
        openTime += dt;
    }

    AssertTrue(closeTime > 0f && openTime > 0f);
    AssertTrue(closeTime < openTime);
}

static void GazeStaysWithinCone()
{
    var gaze = new MicroSaccadeGaze(new Random(3));
    float minX = 1f;
    float maxX = -1f;
    float sumY = 0f;
    for (int i = 0; i < 1500; i++)
    {
        gaze.Update(0.016f);
        AssertTrue(MathF.Abs(gaze.GazeX) <= 0.40f);
        // Down-biased asymmetric cone: more room below center than above.
        AssertTrue(gaze.GazeY <= 0.16f && gaze.GazeY >= -0.50f);
        minX = MathF.Min(minX, gaze.GazeX);
        maxX = MathF.Max(maxX, gaze.GazeX);
        sumY += gaze.GazeY;
    }

    AssertTrue(maxX - minX > 0.05f);
    AssertTrue(sumY / 1500f < -0.03f);
}

static void SaccadesArePacedFast()
{
    var gaze = new MicroSaccadeGaze(new Random(9));
    int saccades = 0;
    for (int i = 0; i < 3750; i++)
    {
        gaze.Update(0.016f);
        if (gaze.SaccadeStarted)
        {
            saccades++;
        }
    }

    // 60 simulated seconds; log-normal dwells (median 233 ms, long tail) put the rate near the
    // tracked 102 per minute.
    AssertTrue(saccades is > 80 and < 160);
}

// Counts blink onsets the way the replay analysis does, so test numbers and field numbers compare
// directly. A saccade-coupled regression here reads as a rate several times the configured one.
static int CountBlinks(ProceduralEyes eyes, float seconds, float arousal, float blinksPerMinute)
{
    const float dt = 1f / 120f;
    int blinks = 0;
    bool closed = false;
    for (int i = 0; i < (int)(seconds / dt); i++)
    {
        float openness = eyes.Update(dt, arousal, blinksPerMinute).Openness;
        if (!closed && openness < 0.25f)
        {
            closed = true;
            blinks++;
        }
        else if (closed && openness > 0.75f)
        {
            closed = false;
        }
    }

    return blinks;
}

static void BlinkRateIsNatural()
{
    int blinks = CountBlinks(new ProceduralEyes(new Random(17)), 600f, arousal: 0f, blinksPerMinute: 15.9f);
    float perMinute = blinks / 10f;
    Console.WriteLine($"  blink rate: {perMinute:F1}/min over 600 s");
    AssertTrue(perMinute is >= 8f and <= 18f);
}

static void BlinkRateFollowsConfig()
{
    float slow = CountBlinks(new ProceduralEyes(new Random(23)), 600f, arousal: 0f, blinksPerMinute: 8f) / 10f;
    float fast = CountBlinks(new ProceduralEyes(new Random(23)), 600f, arousal: 0f, blinksPerMinute: 30f) / 10f;
    Console.WriteLine($"  blink rate: {slow:F1}/min at 8, {fast:F1}/min at 30");
    AssertTrue(slow is >= 5f and <= 11f);
    AssertTrue(fast is >= 24f and <= 36f);
}

static void ProceduralEyesBounded()
{
    var eyes = new ProceduralEyes(new Random(7));
    for (int i = 0; i < 1500; i++)
    {
        EyeOutput o = eyes.Update(0.016f, arousal: (i % 100) / 100f);
        AssertTrue(o.Openness >= 0f && o.Openness <= 1f);
        AssertTrue(o.PupilMm >= 2.9f && o.PupilMm <= 5.1f);
        AssertTrue(MathF.Abs(o.GazeX) <= 0.40f);
    }
}

static void PupilSpanFollowsArousal()
{
    var calm = new ProceduralEyes(new Random(5));
    var excited = new ProceduralEyes(new Random(5));
    float calmPupil = 0f;
    float excitedPupil = 0f;
    for (int i = 0; i < 3000; i++)
    {
        calmPupil = calm.Update(0.016f, arousal: 0f).PupilMm;
        excitedPupil = excited.Update(0.016f, arousal: 1f).PupilMm;
    }

    // Converged pupils must actually use the advertised range, not a token sliver.
    AssertTrue(excitedPupil - calmPupil >= 1.0f);
    AssertTrue(calmPupil >= 3.0f && excitedPupil <= 5.0f);
}

// ---- Prosody ----

static void SpeakerBaselineZScores()
{
    var baseline = new RunningBaseline(20f);
    baseline.Update(1.0f, 0.02f);
    for (int i = 0; i < 100; i++)
    {
        baseline.Update(1.0f, 0.02f);
    }

    float zSame = baseline.Update(1.0f, 0.02f);
    float zHigh = baseline.Update(2.0f, 0.02f);
    AssertTrue(MathF.Abs(zSame) < 0.5f);
    AssertTrue(zHigh > 0f);
}

static void HeuristicArousalRisesWithLoudness()
{
    var estimator = new HeuristicProsodyEstimator();
    var quiet = MakeVoiceFrame(rms: 0.03f);
    var loud = MakeVoiceFrame(rms: 0.5f);

    ProsodyState state = default;
    for (int i = 0; i < 60; i++)
    {
        state = estimator.Estimate(quiet, activity: 0.4f, isSpeech: true, dtSeconds: 0.02f);
    }

    float quietArousal = state.Arousal;
    for (int i = 0; i < 30; i++)
    {
        state = estimator.Estimate(loud, activity: 1f, isSpeech: true, dtSeconds: 0.02f);
    }

    AssertTrue(state.Arousal > quietArousal);
}

static void CrossfadeFallsBackWithoutModel()
{
    using var estimator = new CrossfadeProsodyEstimator(new HeuristicProsodyEstimator(), new OnnxProsodyEstimator(null));
    var frame = MakeVoiceFrame(rms: 0.3f);
    ProsodyState state = estimator.Estimate(frame, activity: 1f, isSpeech: true, dtSeconds: 0.02f);

    AssertTrue(float.IsFinite(state.Arousal) && float.IsFinite(state.Valence));
    AssertTrue(state.SpeechActive);
}

// ---- Module integration ----

static void StepIsDeterministicForSeed()
{
    var script = new ScriptedAudio().Speech(2f).Silence(1f);
    List<StepRecord> a = RunScript(NewModule(), script);
    List<StepRecord> b = RunScript(NewModule(), script);

    AssertTrue(a.Count == b.Count);
    for (int k = 0; k < a.Count; k++)
    {
        AssertTrue(a[k].Openness == b[k].Openness && a[k].GazeX == b[k].GazeX && a[k].GazeY == b[k].GazeY);
        for (int i = 0; i < a[k].Expressions.Length; i++)
        {
            AssertTrue(a[k].Expressions[i] == b[k].Expressions[i]);
        }
    }
}

static void ModuleReportsHealthyStatus()
{
    var source = new FixedAudioAnalysisSource(MakeVoiceFrame(rms: 0.3f));
    var config = new SyntheticConfig { DriveMouth = true, DriveEmotion = false, DriveEyes = false };
    using var module = new SyntheticFaceModule(source, config);

    module.InitializeAsync(
        new FaceModuleContext(Path.GetTempPath()),
        new FaceModuleInitRequest(EyeAvailable: true, ExpressionAvailable: true, HeadAvailable: false),
        CancellationToken.None).AsTask().GetAwaiter().GetResult();

    FaceModuleStatus status = module.GetStatus();
    AssertTrue(status.Health == FaceModuleHealth.Healthy);
    AssertTrue(status.Detail is null);
}

static void ModuleReportsNoChannelsStatus()
{
    var source = new FixedAudioAnalysisSource(MakeVoiceFrame(rms: 0.3f));
    var config = new SyntheticConfig { DriveMouth = false, DriveEmotion = false, DriveEyes = false };
    using var module = new SyntheticFaceModule(source, config);

    module.InitializeAsync(
        new FaceModuleContext(Path.GetTempPath()),
        new FaceModuleInitRequest(EyeAvailable: true, ExpressionAvailable: true, HeadAvailable: false),
        CancellationToken.None).AsTask().GetAwaiter().GetResult();

    FaceModuleStatus status = module.GetStatus();
    AssertTrue(status.Health == FaceModuleHealth.Healthy);
    AssertTrue(status.Detail == "no channels enabled");
}

static void ModuleWritesMouthFrame()
{
    var source = new FixedAudioAnalysisSource(MakeVoiceFrame(rms: 0.3f, centroid: 1416f));
    var config = new SyntheticConfig { DriveMouth = true, DriveEmotion = false, DriveEyes = false };
    using var module = new SyntheticFaceModule(source, config);

    var init = module.InitializeAsync(
        new FaceModuleContext(Path.GetTempPath()),
        new FaceModuleInitRequest(EyeAvailable: true, ExpressionAvailable: true, HeadAvailable: false),
        CancellationToken.None).AsTask().GetAwaiter().GetResult();

    var frame = new FaceFrame();
    for (int i = 0; i < 15; i++)
    {
        module.UpdateAsync(frame, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        Thread.Sleep(20);
    }

    module.TeardownAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();

    AssertTrue(init.ExpressionActive);
    AssertTrue(!init.EyeActive);
    AssertTrue(source.Started);
    AssertTrue(source.Disposed);
    AssertTrue((frame.Flags & FaceFrameFlags.ExpressionsValid) != 0);
    AssertTrue(frame.GetExpression(FaceExpression.JawOpen) > 0.05f);
}

static void ModuleSetsEyeFlagWhenEnabled()
{
    var source = new FixedAudioAnalysisSource(MakeVoiceFrame(rms: 0.2f));
    var config = new SyntheticConfig { DriveMouth = false, DriveEmotion = false, DriveEyes = true };
    using var module = new SyntheticFaceModule(source, config);

    var init = module.InitializeAsync(
        new FaceModuleContext(Path.GetTempPath()),
        new FaceModuleInitRequest(EyeAvailable: true, ExpressionAvailable: true, HeadAvailable: false),
        CancellationToken.None).AsTask().GetAwaiter().GetResult();

    var frame = new FaceFrame();
    module.UpdateAsync(frame, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    module.TeardownAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();

    AssertTrue(init.EyeActive);
    AssertTrue((frame.Flags & FaceFrameFlags.EyeValid) != 0);
}

static void ModuleDrivesEyesByDefault()
{
    var source = new FixedAudioAnalysisSource(MakeVoiceFrame(rms: 0.3f));
    using var module = new SyntheticFaceModule(source, new SyntheticConfig());

    module.InitializeAsync(
        new FaceModuleContext(Path.GetTempPath()),
        new FaceModuleInitRequest(EyeAvailable: true, ExpressionAvailable: true, HeadAvailable: false),
        CancellationToken.None).AsTask().GetAwaiter().GetResult();

    var frame = new FaceFrame();
    module.UpdateAsync(frame, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    module.TeardownAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();

    AssertTrue((frame.Flags & FaceFrameFlags.EyeValid) != 0);
}

// The descriptor is what a host UI renders, so a default that drifts from SyntheticConfig shows the
// user a value the module will not actually use.
static void SettingsDescriptorMatchesConfigDefaults()
{
    var repo = FindRepoRoot();
    var path = Path.Combine(repo, "packaging", "settings_descriptor.json");
    using var doc = JsonDocument.Parse(File.ReadAllText(path));

    var defaults = new SyntheticConfig();
    var props = typeof(SyntheticConfig).GetProperties();

    foreach (JsonElement entry in doc.RootElement.GetProperty("settings").EnumerateArray())
    {
        string key = entry.GetProperty("key").GetString()!;
        var prop = props.FirstOrDefault(p => p.Name == key)
            ?? throw new InvalidOperationException("Descriptor key has no config property: " + key);

        if (!entry.TryGetProperty("default", out JsonElement declared))
        {
            continue;
        }

        object? actual = prop.GetValue(defaults);
        string actualText = actual switch
        {
            null => string.Empty,
            bool b => b ? "true" : "false",
            float f => f.ToString("0.####", CultureInfo.InvariantCulture),
            _ => Convert.ToString(actual, CultureInfo.InvariantCulture) ?? string.Empty,
        };
        string declaredText = declared.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => declared.GetDouble().ToString("0.####", CultureInfo.InvariantCulture),
            _ => declared.GetString() ?? string.Empty,
        };

        if (!string.Equals(actualText, declaredText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Descriptor default for {key} is {declaredText} but SyntheticConfig uses {actualText}.");
        }
    }
}

static void PackageDependenciesAreAllowed()
{
    var repo = FindRepoRoot();
    var project = Path.Combine(repo, "src", "WKOpenVR.SyntheticFaceModule", "WKOpenVR.SyntheticFaceModule.csproj");
    var xml = File.ReadAllText(project);
    var forbidden = new[]
    {
        "openSMILE",
        "OVRLipSync",
        "ProjectBabble",
        "Babble.Core",
        "BabbleCore",
    };

    foreach (var name in forbidden)
    {
        if (xml.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            throw new InvalidOperationException("Restricted dependency found: " + name);
        }
    }
}

// ---- helpers ----

static float[] Sine(float frequency, int count, int sampleRate, float amplitude = 0.5f)
{
    var samples = new float[count];
    for (int i = 0; i < count; i++)
    {
        samples[i] = amplitude * MathF.Sin(2f * MathF.PI * frequency * i / sampleRate);
    }

    return samples;
}

static AudioAnalysisFrame MakeVoiceFrame(float rms, float centroid = 1200f, float pitch = 150f, bool voiced = true, float zcr = 0f)
{
    return new AudioAnalysisFrame(13)
    {
        Rms = rms,
        Voiced = voiced,
        PitchHz = pitch,
        SpectralCentroidHz = centroid,
        SpectralRolloffHz = centroid * 1.5f,
        ZeroCrossingRate = zcr,
        SampleRate = 16000,
        DurationSeconds = 0.02f,
    };
}

// ---- scripted timeline ----

static SyntheticFaceModule NewModule(SyntheticConfig? config = null)
{
    var module = new SyntheticFaceModule(new FixedAudioAnalysisSource(MakeVoiceFrame(rms: 1e-4f, voiced: false)), config ?? new SyntheticConfig());
    module.InitializeAsync(
        new FaceModuleContext(Path.GetTempPath()),
        new FaceModuleInitRequest(EyeAvailable: true, ExpressionAvailable: true, HeadAvailable: false),
        CancellationToken.None).AsTask().GetAwaiter().GetResult();
    return module;
}

// Steps the module at 120 Hz over a 20 ms-per-frame script: 2,3,2,3,2 steps per frame (exactly
// 0.1 s per five frames), the same re-read cadence the live loop sees. Step k sits at k/120 s.
static List<StepRecord> RunScript(SyntheticFaceModule module, ScriptedAudio script)
{
    var records = new List<StepRecord>(script.Frames.Count * 3);
    var face = new FaceFrame();
    for (int i = 0; i < script.Frames.Count; i++)
    {
        int steps = i % 5 is 1 or 3 ? 3 : 2;
        for (int s = 0; s < steps; s++)
        {
            module.Step(script.Frames[i], 1f / 120f, face);
            records.Add(new StepRecord(
                (float[])face.Expressions.Clone(),
                face.Eye.Left.Openness,
                face.Eye.Left.GazeX,
                face.Eye.Left.GazeY));
        }
    }

    return records;
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "WKOpenVR.SyntheticFaceModule.sln")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    throw new InvalidOperationException("Repo root not found.");
}

static void AssertTrue(bool value)
{
    if (!value)
    {
        throw new InvalidOperationException("Assertion failed");
    }
}

static void AssertCount(int expected, int actual, string what)
{
    if (expected != actual)
    {
        throw new InvalidOperationException($"{what}: expected {expected} but got {actual}");
    }
}

static void AssertEqual(float expected, float actual)
{
    if (Math.Abs(expected - actual) > 0.0001f)
    {
        throw new InvalidOperationException("Expected " + expected + " but got " + actual);
    }
}

sealed record StepRecord(float[] Expressions, float Openness, float GazeX, float GazeY);

// A 20 ms-per-frame audio timeline carrying the prosody cues the module keys on. Plain speech
// wobbles pitch 10% at 5 Hz and carries onset flux, so it reads as neither a monotone nor a rising
// terminal. Keep speech runs under 3 s: the noise floor tracks up (tau 4 s) and closes the VAD on a
// constant level.
sealed class ScriptedAudio
{
    private const float FrameSeconds = 0.02f;

    public List<AudioAnalysisFrame> Frames { get; } = new();

    public ScriptedAudio Speech(float seconds, float rms = 0.1f, float pitch = 150f)
        => Add(seconds, t => Voice(rms, pitch * (1f + (0.10f * MathF.Sin(2f * MathF.PI * 5f * t))), flux: 0.1f));

    public ScriptedAudio Rise(float seconds, float fromHz, float toHz, float rms = 0.1f)
        => Add(seconds, t => Voice(rms, fromHz + ((toHz - fromHz) * t / seconds), flux: 0.1f));

    public ScriptedAudio Monotone(float seconds, float rms = 0.1f, float pitch = 150f)
        => Add(seconds, _ => Voice(rms, pitch, flux: 0f));

    public ScriptedAudio Pulses(float seconds, float hz, float rms = 0.1f, float pitch = 150f)
        => Add(seconds, t => (t * hz) % 1f < 0.5f ? Voice(rms, pitch, flux: 0.1f) : Quiet(rms * 0.2f));

    // Breathy laughter: the same rhythm and loudness lift with no periodicity at all, so every
    // frame reports Voiced = false and carries no pitch.
    public ScriptedAudio UnvoicedPulses(float seconds, float hz, float rms)
        => Add(seconds, t => (t * hz) % 1f < 0.5f ? Quiet(rms) : Quiet(rms * 0.2f));

    public ScriptedAudio Silence(float seconds) => Add(seconds, _ => Quiet(1e-4f));

    private ScriptedAudio Add(float seconds, Func<float, AudioAnalysisFrame> make)
    {
        int count = (int)MathF.Round(seconds / FrameSeconds);
        for (int i = 0; i < count; i++)
        {
            AudioAnalysisFrame frame = make(i * FrameSeconds);
            frame.TimestampSeconds = Frames.Count * FrameSeconds;
            Frames.Add(frame);
        }

        return this;
    }

    private static AudioAnalysisFrame Voice(float rms, float pitch, float flux) => new(13)
    {
        Rms = rms,
        Voiced = true,
        PitchHz = pitch,
        SpectralCentroidHz = 1200f,
        SpectralRolloffHz = 1800f,
        SpectralFlux = flux,
        SampleRate = 16000,
        DurationSeconds = FrameSeconds,
    };

    private static AudioAnalysisFrame Quiet(float rms) => new(13)
    {
        Rms = rms,
        SpectralCentroidHz = 1200f,
        SpectralRolloffHz = 1800f,
        SampleRate = 16000,
        DurationSeconds = FrameSeconds,
    };
}

sealed class FixedAudioAnalysisSource : IAudioAnalysisSource
{
    private readonly AudioAnalysisFrame _frame;

    public FixedAudioAnalysisSource(AudioAnalysisFrame frame)
    {
        _frame = frame;
    }

    public bool Started { get; private set; }

    public bool Disposed { get; private set; }

    public void Start() => Started = true;

    public bool TryRead([NotNullWhen(true)] out AudioAnalysisFrame? frame)
    {
        frame = _frame;
        return true;
    }

    public void Dispose() => Disposed = true;
}

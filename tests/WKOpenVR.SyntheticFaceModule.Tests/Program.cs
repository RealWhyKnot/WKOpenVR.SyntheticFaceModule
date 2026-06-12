using System.Diagnostics.CodeAnalysis;
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
    ("Mouth opens on loud vowel", MouthOpensOnLoudVowel),
    ("Mouth close does not fight open speech", MouthCloseDoesNotFightOpenSpeech),
    ("Mouth rounded vs front mapping", MouthRoundedVsFrontMapping),
    ("Emotion coloring respects caps and avoids mouth shapes", EmotionColoringCapsAndMouth),
    ("Emotion coloring suppressed at low confidence", EmotionColoringSuppressedLowConfidence),
    ("Mixer composes mouth and emotion", MixerComposesMouthAndEmotion),
    ("Mixer omits eye flag when no eyes", MixerOmitsEyeFlag),
    ("Mixer sets symmetric eyes", MixerSetsSymmetricEyes),
    ("Blink closes faster than it opens", BlinkClosesFasterThanOpens),
    ("Gaze stays within cone", GazeStaysWithinCone),
    ("Procedural eyes are bounded", ProceduralEyesBounded),
    ("Speaker baseline produces z-scores", SpeakerBaselineZScores),
    ("Heuristic arousal rises with loudness", HeuristicArousalRisesWithLoudness),
    ("Crossfade falls back to heuristic without model", CrossfadeFallsBackWithoutModel),
    ("Module writes mouth frame", ModuleWritesMouthFrame),
    ("Module sets eye flag when eyes enabled", ModuleSetsEyeFlagWhenEnabled),
    ("Module leaves eyes to VRChat by default", ModuleLeavesEyesByDefault),
    ("Package dependencies are allowed", PackageDependenciesAreAllowed),
};

foreach (var test in tests)
{
    test.Body();
    Console.WriteLine("PASS " + test.Name);
}

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

static void MouthRoundedVsFrontMapping()
{
    var roundedSolver = new MouthSolver();
    var rounded = new float[FaceExpressionCount.Value];
    var roundedFrame = MakeVoiceFrame(rms: 0.3f, centroid: 400f);

    var frontSolver = new MouthSolver();
    var front = new float[FaceExpressionCount.Value];
    var frontFrame = MakeVoiceFrame(rms: 0.3f, centroid: 3200f);

    for (int i = 0; i < 40; i++)
    {
        roundedSolver.Solve(roundedFrame, 1f, 0.02f, 1f, rounded);
        frontSolver.Solve(frontFrame, 1f, 0.02f, 1f, front);
    }

    AssertTrue(rounded[(int)FaceExpression.LipFunnelUpperRight] > front[(int)FaceExpression.LipFunnelUpperRight]);
    AssertTrue(front[(int)FaceExpression.MouthStretchRight] > rounded[(int)FaceExpression.MouthStretchRight]);
}

// ---- Emotion coloring ----

static void EmotionColoringCapsAndMouth()
{
    var layer = new EmotionColoringLayer();
    var offsets = new float[FaceExpressionCount.Value];
    var prosody = new ProsodyState(Arousal: 0.5f, Valence: 0.9f, Confidence: 0.9f, SpeechActive: true);
    for (int i = 0; i < 60; i++)
    {
        layer.Apply(prosody, intensity: 1f, dtSeconds: 0.02f, offsets);
    }

    AssertTrue(offsets[(int)FaceExpression.CheekSquintRight] > 0f);
    AssertTrue(offsets[(int)FaceExpression.CheekSquintRight] <= 0.19f);
    // Emotion must never touch mouth shapes; the audio mouth solver is the sole owner.
    AssertEqual(0f, offsets[(int)FaceExpression.JawOpen]);
    AssertEqual(0f, offsets[(int)FaceExpression.MouthClosed]);
    AssertEqual(0f, offsets[(int)FaceExpression.LipFunnelUpperRight]);
    AssertEqual(0f, offsets[(int)FaceExpression.MouthCornerPullRight]);
    AssertEqual(0f, offsets[(int)FaceExpression.MouthFrownRight]);
    AssertEqual(0f, offsets[(int)FaceExpression.MouthPressRight]);
}

static void EmotionColoringSuppressedLowConfidence()
{
    var layer = new EmotionColoringLayer();
    var offsets = new float[FaceExpressionCount.Value];
    var prosody = new ProsodyState(Arousal: 0.9f, Valence: 0.9f, Confidence: 0.1f, SpeechActive: true);
    for (int i = 0; i < 60; i++)
    {
        layer.Apply(prosody, intensity: 1f, dtSeconds: 0.02f, offsets);
    }

    AssertTrue(offsets[(int)FaceExpression.MouthCornerPullRight] < 0.01f);
    AssertTrue(offsets[(int)FaceExpression.BrowOuterUpRight] < 0.01f);
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

    mixer.Compose(frame, mouth, mouthActive: true, emotion, emotionActive: true, eyes: null);

    AssertTrue((frame.Flags & FaceFrameFlags.ExpressionsValid) != 0);
    AssertTrue((frame.Flags & FaceFrameFlags.EyeValid) == 0);
    AssertEqual(0.5f, frame.GetExpression(FaceExpression.JawOpen));
    AssertEqual(0.2f, frame.GetExpression(FaceExpression.MouthCornerPullRight));
}

static void MixerOmitsEyeFlag()
{
    var mixer = new SyntheticFrameMixer();
    var frame = new FaceFrame();
    var mouth = new float[FaceExpressionCount.Value];
    mouth[(int)FaceExpression.JawOpen] = 0.3f;

    mixer.Compose(frame, mouth, mouthActive: true, emotion: null, emotionActive: false, eyes: null);

    AssertTrue((frame.Flags & FaceFrameFlags.EyeValid) == 0);
}

static void MixerSetsSymmetricEyes()
{
    var mixer = new SyntheticFrameMixer();
    var frame = new FaceFrame();
    var eye = new EyeOutput(Openness: 0.7f, GazeX: 0.2f, GazeY: -0.1f, PupilMm: 4f, MinDilationMm: 3f, MaxDilationMm: 5f);

    mixer.Compose(frame, mouth: null, mouthActive: false, emotion: null, emotionActive: false, eye);

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
    for (int i = 0; i < 1500; i++)
    {
        gaze.Update(0.016f);
        AssertTrue(MathF.Abs(gaze.GazeX) <= 0.40f);
        AssertTrue(MathF.Abs(gaze.GazeY) <= 0.30f);
        minX = MathF.Min(minX, gaze.GazeX);
        maxX = MathF.Max(maxX, gaze.GazeX);
    }

    AssertTrue(maxX - minX > 0.05f);
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

static void ModuleLeavesEyesByDefault()
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

    AssertTrue((frame.Flags & FaceFrameFlags.EyeValid) == 0);
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

static AudioAnalysisFrame MakeVoiceFrame(float rms, float centroid = 1200f, float pitch = 150f, bool voiced = true)
{
    return new AudioAnalysisFrame(13)
    {
        Rms = rms,
        Voiced = voiced,
        PitchHz = pitch,
        SpectralCentroidHz = centroid,
        SpectralRolloffHz = centroid * 1.5f,
        SampleRate = 16000,
        DurationSeconds = 0.02f,
    };
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

static void AssertEqual(float expected, float actual)
{
    if (Math.Abs(expected - actual) > 0.0001f)
    {
        throw new InvalidOperationException("Expected " + expected + " but got " + actual);
    }
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

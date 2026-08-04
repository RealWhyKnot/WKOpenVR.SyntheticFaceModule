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
    ("Mouth lip openers follow jaw", MouthLipOpenersFollowJaw),
    ("Fricative drives tightener and damps jaw", FricativeDrivesTightenerAndDampsJaw),
    ("Mouth close does not fight open speech", MouthCloseDoesNotFightOpenSpeech),
    ("Mouth rounded vs front mapping", MouthRoundedVsFrontMapping),
    ("Emotion coloring respects caps and avoids mouth shapes", EmotionColoringCapsAndMouth),
    ("Emotion coloring suppressed at low confidence", EmotionColoringSuppressedLowConfidence),
    ("Smile mirrors tracker pairings", SmileMirrorsTrackerPairings),
    ("Smile decays slower than it attacks", SmileDecaysSlowerThanItAttacks),
    ("Frown is fast and bounded", FrownIsFastAndBounded),
    ("Smile intensity zero keeps corners still", SmileIntensityZeroKeepsCornersStill),
    ("Mixer composes mouth and emotion", MixerComposesMouthAndEmotion),
    ("Mixer idle alone sets expressions", MixerIdleAloneSetsExpressions),
    ("Idle layer produces calibrated activity", IdleLayerProducesCalibratedActivity),
    ("Idle layer deterministic for seed", IdleLayerDeterministicForSeed),
    ("Mixer omits eye flag when no eyes", MixerOmitsEyeFlag),
    ("Mixer sets symmetric eyes", MixerSetsSymmetricEyes),
    ("Blink closes faster than it opens", BlinkClosesFasterThanOpens),
    ("Gaze stays within cone", GazeStaysWithinCone),
    ("Saccades are paced fast", SaccadesArePacedFast),
    ("Procedural eyes are bounded", ProceduralEyesBounded),
    ("Pupil span follows arousal", PupilSpanFollowsArousal),
    ("Speaker baseline produces z-scores", SpeakerBaselineZScores),
    ("Heuristic arousal rises with loudness", HeuristicArousalRisesWithLoudness),
    ("Crossfade falls back to heuristic without model", CrossfadeFallsBackWithoutModel),
    ("Module writes mouth frame", ModuleWritesMouthFrame),
    ("Module sets eye flag when eyes enabled", ModuleSetsEyeFlagWhenEnabled),
    ("Module leaves eyes to VRChat by default", ModuleLeavesEyesByDefault),
    ("Module reports healthy status when active", ModuleReportsHealthyStatus),
    ("Module reports no-channels status when idle", ModuleReportsNoChannelsStatus),
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
    AssertEqual(jaw * 0.65f, expr[(int)FaceExpression.MouthUpperUpRight]);
    AssertEqual(expr[(int)FaceExpression.MouthUpperUpRight], expr[(int)FaceExpression.MouthUpperDeepenRight]);
    AssertEqual(jaw * 0.52f, expr[(int)FaceExpression.MouthLowerDownLeft]);
}

static void FricativeDrivesTightenerAndDampsJaw()
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

    AssertTrue(fricExpr[(int)FaceExpression.MouthTightenerRight] > 0.02f);
    AssertTrue(fricExpr[(int)FaceExpression.JawOpen] < vowelExpr[(int)FaceExpression.JawOpen]);
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
        layer.Apply(prosody, intensity: 1f, smileIntensity: 1f, dtSeconds: 0.02f, offsets);
    }

    AssertTrue(offsets[(int)FaceExpression.CheekSquintRight] > 0f);
    // Positive valence engages the Duchenne pairing: cheeks rise with the smile beyond
    // the old subtle-coloring cap, and the corners pull toward a full smile.
    AssertTrue(offsets[(int)FaceExpression.CheekSquintRight] > 0.19f);
    AssertTrue(offsets[(int)FaceExpression.MouthCornerPullRight] > 0.5f);
    // Viseme-critical shapes stay owned by the audio mouth solver.
    AssertEqual(0f, offsets[(int)FaceExpression.JawOpen]);
    AssertEqual(0f, offsets[(int)FaceExpression.MouthClosed]);
    AssertEqual(0f, offsets[(int)FaceExpression.LipFunnelUpperRight]);
    AssertEqual(0f, offsets[(int)FaceExpression.MouthStretchRight]);
    AssertEqual(0f, offsets[(int)FaceExpression.MouthTightenerRight]);
    AssertEqual(0f, offsets[(int)FaceExpression.MouthUpperUpRight]);
    AssertEqual(0f, offsets[(int)FaceExpression.MouthPressRight]);
}

static void SmileMirrorsTrackerPairings()
{
    var layer = new EmotionColoringLayer();
    var offsets = new float[FaceExpressionCount.Value];
    var prosody = new ProsodyState(Arousal: 0.5f, Valence: 0.9f, Confidence: 1.0f, SpeechActive: true);
    for (int i = 0; i < 400; i++)
    {
        layer.Apply(prosody, intensity: 1f, smileIntensity: 1f, dtSeconds: 0.02f, offsets);
    }

    float pull = offsets[(int)FaceExpression.MouthCornerPullRight];
    AssertTrue(pull > 0.9f);
    AssertEqual(pull, offsets[(int)FaceExpression.MouthCornerSlantRight]);
    AssertEqual(pull * 0.37f, offsets[(int)FaceExpression.MouthDimpleRight]);
    AssertEqual(0f, offsets[(int)FaceExpression.MouthFrownRight]);
}

static void SmileDecaysSlowerThanItAttacks()
{
    var layer = new EmotionColoringLayer();
    var offsets = new float[FaceExpressionCount.Value];
    var happy = new ProsodyState(Arousal: 0.5f, Valence: 0.9f, Confidence: 1.0f, SpeechActive: true);
    var silent = new ProsodyState(Arousal: 0.0f, Valence: 0.0f, Confidence: 0.0f, SpeechActive: false);
    for (int i = 0; i < 400; i++)
    {
        layer.Apply(happy, intensity: 1f, smileIntensity: 1f, dtSeconds: 0.02f, offsets);
    }

    // One second after speech stops, the smile is fading but still clearly present.
    for (int i = 0; i < 50; i++)
    {
        layer.Apply(silent, intensity: 1f, smileIntensity: 1f, dtSeconds: 0.02f, offsets);
    }

    float after = offsets[(int)FaceExpression.MouthCornerPullRight];
    AssertTrue(after > 0.25f);
    AssertTrue(after < 0.9f);
}

static void FrownIsFastAndBounded()
{
    var layer = new EmotionColoringLayer();
    var offsets = new float[FaceExpressionCount.Value];
    var negative = new ProsodyState(Arousal: 0.5f, Valence: -0.6f, Confidence: 1.0f, SpeechActive: true);
    for (int i = 0; i < 10; i++)
    {
        layer.Apply(negative, intensity: 1f, smileIntensity: 1f, dtSeconds: 0.02f, offsets);
    }

    // Frowns reach depth within a couple hundred milliseconds and never pass their cap.
    AssertTrue(offsets[(int)FaceExpression.MouthFrownRight] > 0.2f);
    AssertTrue(offsets[(int)FaceExpression.MouthFrownRight] <= 0.5f);
    AssertTrue(offsets[(int)FaceExpression.MouthCornerPullRight] < 0.01f);
}

static void SmileIntensityZeroKeepsCornersStill()
{
    var layer = new EmotionColoringLayer();
    var offsets = new float[FaceExpressionCount.Value];
    var prosody = new ProsodyState(Arousal: 0.5f, Valence: 0.9f, Confidence: 1.0f, SpeechActive: true);
    for (int i = 0; i < 120; i++)
    {
        layer.Apply(prosody, intensity: 1f, smileIntensity: 0f, dtSeconds: 0.02f, offsets);
    }

    AssertEqual(0f, offsets[(int)FaceExpression.MouthCornerPullRight]);
    AssertEqual(0f, offsets[(int)FaceExpression.MouthDimpleRight]);
    // Subtle coloring still runs at its old caps.
    AssertTrue(offsets[(int)FaceExpression.CheekSquintRight] > 0f);
    AssertTrue(offsets[(int)FaceExpression.CheekSquintRight] <= 0.19f);
}

static void EmotionColoringSuppressedLowConfidence()
{
    var layer = new EmotionColoringLayer();
    var offsets = new float[FaceExpressionCount.Value];
    var prosody = new ProsodyState(Arousal: 0.9f, Valence: 0.9f, Confidence: 0.1f, SpeechActive: true);
    for (int i = 0; i < 60; i++)
    {
        layer.Apply(prosody, intensity: 1f, smileIntensity: 1f, dtSeconds: 0.02f, offsets);
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

    var idle = new float[FaceExpressionCount.Value];
    idle[(int)FaceExpression.BrowInnerUpRight] = 0.04f;
    idle[(int)FaceExpression.MouthCornerPullRight] = 0.1f;

    mixer.Compose(frame, mouth, mouthActive: true, emotion, emotionActive: true, idle, idleActive: true, eyes: null);

    AssertTrue((frame.Flags & FaceFrameFlags.ExpressionsValid) != 0);
    AssertTrue((frame.Flags & FaceFrameFlags.EyeValid) == 0);
    AssertEqual(0.5f, frame.GetExpression(FaceExpression.JawOpen));
    // Layers sum: emotion 0.2 + idle 0.1 on the same shape.
    AssertEqual(0.3f, frame.GetExpression(FaceExpression.MouthCornerPullRight));
    AssertEqual(0.04f, frame.GetExpression(FaceExpression.BrowInnerUpRight));
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

    // Two simulated minutes at ~12 events/min; allow wide slack for the random draw.
    AssertTrue(browEvents is > 10 and < 45);
    AssertTrue(browMax <= 0.06f);
    AssertTrue(cornerMax <= 0.05f);

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

    // 60 simulated seconds; short exponential dwells put the rate near real
    // recordings (roughly 90-170 per minute), far above the old leisurely pace.
    AssertTrue(saccades is > 70 and < 220);
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

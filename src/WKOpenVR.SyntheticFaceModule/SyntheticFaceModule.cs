using System.Diagnostics;
using WKOpenVR.FaceTracking.Sdk;
using WKOpenVR.SyntheticFaceModule.Audio;
using WKOpenVR.SyntheticFaceModule.Coloring;
using WKOpenVR.SyntheticFaceModule.Config;
using WKOpenVR.SyntheticFaceModule.Dsp.Vad;
using WKOpenVR.SyntheticFaceModule.Eyes;
using WKOpenVR.SyntheticFaceModule.Head;
using WKOpenVR.SyntheticFaceModule.Mixer;
using WKOpenVR.SyntheticFaceModule.Mouth;
using WKOpenVR.SyntheticFaceModule.Prosody;
using WKOpenVR.SyntheticFaceModule.Ser;

namespace WKOpenVR.SyntheticFaceModule;

/// <summary>
/// No-hardware synthetic face source. Drives mouth shapes from the microphone (two-stage MFCC/VAD
/// lip-sync), adds subtle prosody-driven expression coloring, and optionally generates procedural
/// eyes. Layers are combined by a priority mixer. Configuration is read from a JSON file and
/// hot-reloaded. The intensive ONNX emotion model is opt-in behind the quality tier; everything else
/// is lightweight and always-on. Eyes are off by default so VRChat's native idle eyes run.
///
/// Diagnostics: at Debug the module emits a periodic per-stage snapshot plus state transitions; at
/// Trace it emits the same snapshot every frame (a firehose for deep diagnosis). Both are gated by
/// the logger so they cost nothing when the host is not verbose.
/// </summary>
public sealed class SyntheticFaceModule : IFaceTrackingModule, IFaceModuleStatusSource, IDisposable
{
    private const double DiagnosticIntervalSeconds = 0.5;
    private const float UpdateRateHz = 120.0f;

    private readonly IAudioAnalysisSource? _injectedSource;
    private readonly Random _rng;
    private readonly bool _configIsFixed;

    private readonly MouthSolver _mouth = new();
    private readonly NoiseFloorTracker _noiseFloor = new();
    private readonly SpeechActivityDetector _vad = new();
    private readonly EmotionColoringLayer _coloring = new();
    private readonly ProsodyEventDetector _events = new();
    private readonly SyntheticFrameMixer _mixer = new();
    private readonly HeadMotionTracker _head = new();
    private readonly DozeStateMachine _doze = new();
    private readonly float[] _mouthBuffer = new float[FaceExpressionCount.Value];
    private readonly float[] _emotionBuffer = new float[FaceExpressionCount.Value];
    private readonly float[] _idleBuffer = new float[FaceExpressionCount.Value];
    private readonly Stopwatch _clock = new();
    private readonly FrameRateLimiter _pacer = new(UpdateRateHz);

    private SyntheticConfig _config;
    private SyntheticConfigLoader? _configLoader;
    private IAudioAnalysisSource? _source;
    private IProsodyEstimator? _prosody;
    private ProceduralEyes? _eyes;
    private IdleMotionLayer? _idle;
    private AsymmetryLayer? _asymmetry;
    private IFaceModuleLogger _log = NullFaceModuleLogger.Instance;

    private bool _expressionAllowed;
    private bool _eyeAllowed;
    private bool _active;
    private string? _micStartError;
    private double _lastUpdateSeconds;
    private double _diagAccumSeconds;
    private bool _lastSpeech;

    public SyntheticFaceModule()
    {
        _rng = new Random();
        _config = new SyntheticConfig();
        _configIsFixed = false;
    }

    /// <summary>Test/host-injection constructor: supply a fixed audio source, config, and RNG seed.</summary>
    public SyntheticFaceModule(IAudioAnalysisSource source, SyntheticConfig? config = null, Random? rng = null)
    {
        _injectedSource = source;
        _rng = rng ?? new Random(12345);
        _config = config ?? new SyntheticConfig();
        _configIsFixed = config is not null;
    }

    public FaceModuleInfo ModuleInfo { get; } = new(
        "4df7850f-1d75-4665-9eab-6f07e0f3b5dc",
        "WKOpenVR Synthetic Face Module",
        "WhyKnot",
        new Version(0, 6, 0));

    public FaceModuleCapabilities Capabilities =>
        FaceModuleCapabilities.Eye | FaceModuleCapabilities.Expression | FaceModuleCapabilities.AudioInput;

    public ValueTask<FaceModuleInitResult> InitializeAsync(
        FaceModuleContext context,
        FaceModuleInitRequest request,
        CancellationToken cancellationToken)
    {
        _log = context.Logger;

        if (!_configIsFixed)
        {
            _configLoader = new SyntheticConfigLoader(context.ConfigDirectory, _log);
            _configLoader.LoadNow();
            _config = _configLoader.Current;
        }

        _expressionAllowed = request.ExpressionAvailable;
        _eyeAllowed = request.EyeAvailable;

        bool wantExpression = _expressionAllowed && (_config.DriveMouth || _config.DriveEmotion);
        bool wantEyes = _eyeAllowed && _config.DriveEyes;
        _active = wantExpression || wantEyes;

        _eyes = new ProceduralEyes(_rng);
        // Child RNGs so idle and asymmetry draws never perturb the eye stream's determinism.
        _idle = new IdleMotionLayer(new Random(_rng.Next()));
        _asymmetry = new AsymmetryLayer(new Random(_rng.Next()));
        _prosody = BuildProsodyEstimator(_config);

        if (_active)
        {
            _source = _injectedSource ?? new MicrophoneAudioSource(
                MicrophoneAudioSource.ResolveDeviceNumber(_config.MicDeviceNumber, _config.MicDeviceName),
                log: _log);
            if (wantExpression)
            {
                try
                {
                    _source.Start();
                    _micStartError = null;
                }
                catch (Exception ex)
                {
                    // A missing or broken capture device should degrade the module, not kill it;
                    // the health status below carries the reason to the app UI.
                    _micStartError = ex.Message;
                    _log.Error($"[synthetic/mic] capture failed to start: {ex.Message}");
                }
            }

            _clock.Restart();
            _lastUpdateSeconds = 0;
            _diagAccumSeconds = 0;
        }

        string mic = string.IsNullOrEmpty(_config.MicDeviceName)
            ? _config.MicDeviceNumber.ToString()
            : $"{_config.MicDeviceNumber}/{_config.MicDeviceName}";
        _log.Info(
            $"[synthetic] init mouth={_config.DriveMouth} emotion={_config.DriveEmotion} eyes={_config.DriveEyes} " +
            $"quality={_config.QualityMode} emoIntensity={_config.EmotionIntensity:F2} " +
            $"mouthIntensity={_config.MouthIntensity:F2} " +
            $"mic={mic} sdkAbi={FaceModuleAbi.Version} sdk={FaceModuleAbi.SdkVersion} " +
            $"config={_configLoader?.LoadedPath ?? "(programmatic)"}");

        return ValueTask.FromResult(new FaceModuleInitResult(
            EyeActive: wantEyes,
            ExpressionActive: wantExpression,
            HeadActive: false));
    }

    public ValueTask UpdateAsync(FaceFrame frame, CancellationToken cancellationToken)
    {
        // The host drives this in a tight loop with no delay; pace to the downstream
        // consumer rate. Injected-source harnesses drive their own timeline unpaced.
        if (_injectedSource is null)
        {
            _pacer.WaitForNext(cancellationToken);
        }

        // Both this Clear and the mixer's wipe frame.Inputs, so the host's head pose is read first.
        HeadInput head = ReadHeadInput(frame);
        frame.Clear();
        if (!_active)
        {
            return ValueTask.CompletedTask;
        }

        double now = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - _lastUpdateSeconds, 0.0, 0.1);
        _lastUpdateSeconds = now;

        if (_configLoader is not null && _configLoader.Poll(now))
        {
            _config = _configLoader.Current;
            _log.Info(
                $"[synthetic] config reloaded mouth={_config.DriveMouth} emotion={_config.DriveEmotion} " +
                $"eyes={_config.DriveEyes} quality={_config.QualityMode}");
        }

        AudioAnalysisFrame? audio = null;
        if (_source is not null && _source.TryRead(out AudioAnalysisFrame? snapshot))
        {
            audio = snapshot;
        }

        Step(audio, dt, frame, head);
        return ValueTask.CompletedTask;
    }

    private static HeadInput ReadHeadInput(FaceFrame frame)
    {
        FaceHeadInput source = frame.Inputs.Head;
        if (!source.IsValid)
        {
            return HeadInput.None;
        }

        return new HeadInput(
            Valid: true,
            Rotation: source.Rotation,
            AngularVelocity: source.AngularVelocity,
            SampleIndex: source.SampleIndex,
            AgeSeconds: source.AgeSeconds);
    }

    // One pipeline pass at an explicit dt, so a scripted timeline can be driven deterministically.
    internal void Step(AudioAnalysisFrame? audio, float dt, FaceFrame frame)
    {
        Step(audio, dt, frame, HeadInput.None);
    }

    internal void Step(AudioAnalysisFrame? audio, float dt, FaceFrame frame, in HeadInput head)
    {
        bool driveMouth = _expressionAllowed && _config.DriveMouth;
        bool driveEmotion = _expressionAllowed && _config.DriveEmotion;
        bool driveEyes = _eyeAllowed && _config.DriveEyes;

        _head.Update(head, dt, _config.HeadMovingThreshold, DozePitchRadians() * 0.5f);

        float activity = 0f;
        bool isSpeech = false;
        if (audio is not null)
        {
            _noiseFloor.Update(audio.Rms, dt);
            isSpeech = _vad.Update(audio.Rms, _noiseFloor.Floor, dt);
            activity = _vad.Activity;
        }

        bool mouthActive = driveMouth && audio is not null;
        if (mouthActive)
        {
            _mouth.Solve(audio!, activity, dt, _config.MouthIntensity, _mouthBuffer);
        }

        ProsodyState prosody = ProsodyState.Neutral;
        ProsodyEvents events = default;
        bool emotionActive = driveEmotion && audio is not null && _prosody is not null;
        if (emotionActive)
        {
            prosody = _prosody!.Estimate(audio!, activity, isSpeech, dt);
            events = _events.Update(audio!, isSpeech, prosody.Arousal);
            if (events != default)
            {
                _log.Debug(
                    $"[synthetic] event question={events.Question} emphasis={events.Emphasis} engagement={events.Engagement} " +
                    $"hesitation={events.Hesitation} laughter={events.Laughter} arousal={prosody.Arousal:F2}");
            }

            _coloring.Apply(events, _config, dt, _emotionBuffer);
        }

        // Idle micro-motion runs whenever emotion channels are allowed, with or without
        // audio, so the face never goes dead during mic silence.
        _doze.Update(
            _head,
            isSpeech,
            dt,
            _config.DozeEnabled && driveEyes,
            DozePitchRadians(),
            _config.DozeDwellSeconds,
            _config.SleepDwellSeconds);

        bool idleActive = driveEmotion && _idle is not null;
        if (idleActive)
        {
            float idleArousal = prosody.SpeechActive ? prosody.Arousal : 0f;
            _idle!.Update(dt, idleArousal, _config.IdleIntensity, _idleBuffer);
            if (_doze.Breath > 0f)
            {
                AddBreath(_idleBuffer, _doze.Breath);
            }
        }

        EyeOutput? eyes = null;
        if (driveEyes && _eyes is not null)
        {
            float arousal = prosody.SpeechActive ? prosody.Arousal : 0f;
            var context = new EyeContext(
                HeadValid: _head.Valid,
                HeadYawRate: _head.YawRate,
                HeadPitchRate: _head.PitchRate,
                HeadMoving: _head.Moving,
                MotionOnset: _head.MotionOnset,
                Speaking: isSpeech,
                Hesitation: events.Hesitation,
                SocialGaze: _config.SocialGazeEnabled,
                VorGain: _config.VorGain,
                VorRecenterSeconds: _config.VorRecenterSeconds,
                LidClosure: _doze.LidClosure,
                Asleep: _doze.Asleep);
            eyes = _eyes.Update(dt, arousal, _config.BlinkRatePerMinute * _doze.BlinkRateScale, context);
        }

        _mixer.Compose(
            frame,
            mouthActive ? _mouthBuffer : null,
            mouthActive,
            emotionActive ? _emotionBuffer : null,
            emotionActive,
            idleActive ? _idleBuffer : null,
            idleActive,
            eyes);

        if (driveEmotion)
        {
            _asymmetry?.Apply(frame, dt, _config.AsymmetryIntensity);
        }

        FaceFrameValidator.Sanitize(frame);

        LogDiagnostics(dt, audio, activity, isSpeech, prosody, eyes, frame);
    }

    private float DozePitchRadians() => _config.DozePitchDegrees * MathF.PI / 180f;

    private static void AddBreath(float[] idle, float breath)
    {
        idle[(int)FaceExpression.BrowInnerUpRight] = MathF.Max(idle[(int)FaceExpression.BrowInnerUpRight], breath);
        idle[(int)FaceExpression.BrowInnerUpLeft] = MathF.Max(idle[(int)FaceExpression.BrowInnerUpLeft], breath);
        idle[(int)FaceExpression.JawOpen] = MathF.Max(idle[(int)FaceExpression.JawOpen], breath);
    }

    public ValueTask TeardownAsync(CancellationToken cancellationToken)
    {
        Shutdown();
        return ValueTask.CompletedTask;
    }

    public FaceModuleStatus GetStatus()
    {
        if (!_active)
        {
            return new FaceModuleStatus(FaceModuleHealth.Healthy, "no channels enabled");
        }

        if (_micStartError is not null)
        {
            return new FaceModuleStatus(FaceModuleHealth.DeviceLost, _micStartError);
        }

        if (_source is MicrophoneAudioSource mic && mic.DeviceLost)
        {
            return new FaceModuleStatus(FaceModuleHealth.DeviceLost, mic.LastError ?? "audio capture stopped");
        }

        return new FaceModuleStatus(FaceModuleHealth.Healthy);
    }

    public void Dispose() => Shutdown();

    private IProsodyEstimator BuildProsodyEstimator(SyntheticConfig config)
    {
        var heuristic = new HeuristicProsodyEstimator();
        if (!config.QualityMode)
        {
            return heuristic;
        }

        var model = new OnnxProsodyEstimator(config.EmotionModelPath, log: _log);
        return new CrossfadeProsodyEstimator(heuristic, model);
    }

    private void LogDiagnostics(
        float dt,
        AudioAnalysisFrame? audio,
        float activity,
        bool isSpeech,
        in ProsodyState prosody,
        in EyeOutput? eyes,
        FaceFrame frame)
    {
        if (isSpeech != _lastSpeech)
        {
            _lastSpeech = isSpeech;
            _log.Debug($"[synthetic] speech {(isSpeech ? "start" : "stop")} activity={activity:F2}");
        }

        bool trace = _log.IsEnabled(FaceModuleLogLevel.Trace);
        _diagAccumSeconds += dt;
        bool periodic = _diagAccumSeconds >= DiagnosticIntervalSeconds;
        if (periodic)
        {
            _diagAccumSeconds = 0;
        }

        bool debug = periodic && _log.IsEnabled(FaceModuleLogLevel.Debug);
        if (!trace && !debug)
        {
            return;
        }

        float rms = audio?.Rms ?? 0f;
        float centroid = audio?.SpectralCentroidHz ?? 0f;
        float pitch = audio?.PitchHz ?? 0f;
        bool voiced = audio?.Voiced ?? false;
        string eyeText = eyes is { } e
            ? $"open={e.Openness:F2} gx={e.GazeX:F2} gy={e.GazeY:F2} pupil={e.PupilMm:F1}"
            : "off";

        string snapshot =
            $"[synthetic/diag] dt={dt * 1000f:F1}ms rms={rms:F3} floor={_noiseFloor.Floor:F3} act={activity:F2} " +
            $"speech={isSpeech} voiced={voiced} centroid={centroid:F0} pitch={pitch:F0} | " +
            $"jaw={_mouth.LastJawOpen:F2} mclose={_mouth.LastMouthClosed:F2} open={_mouth.LastOpenWeight:F2} " +
            $"front={_mouth.LastFrontWeight:F2} round={_mouth.LastRoundedWeight:F2} fric={_mouth.LastFricativeWeight:F2} | " +
            $"arousal={prosody.Arousal:F2} valence={prosody.Valence:F2} conf={prosody.Confidence:F2} | " +
            $"top={TopExpressions(frame.Expressions, 5)} | eyes {eyeText}";

        if (trace)
        {
            _log.Trace(snapshot);
        }
        else
        {
            _log.Debug(snapshot);
        }
    }

    private static string TopExpressions(float[] expressions, int count)
    {
        Span<int> topIndices = stackalloc int[count];
        Span<float> topValues = stackalloc float[count];
        int used = 0;

        for (int i = 0; i < expressions.Length; i++)
        {
            float value = expressions[i];
            if (value <= 0.001f)
            {
                continue;
            }

            int insert = used;
            while (insert > 0 && value > topValues[insert - 1])
            {
                insert--;
            }

            if (insert >= count)
            {
                continue;
            }

            int copyStart = Math.Min(used, count - 1);
            for (int j = copyStart; j > insert; j--)
            {
                topIndices[j] = topIndices[j - 1];
                topValues[j] = topValues[j - 1];
            }

            topIndices[insert] = i;
            topValues[insert] = value;
            if (used < count)
            {
                used++;
            }
        }

        if (used == 0)
        {
            return "none";
        }

        var parts = new string[used];
        for (int i = 0; i < used; i++)
        {
            parts[i] = $"{(FaceExpression)topIndices[i]}:{topValues[i]:F2}";
        }

        return string.Join(",", parts);
    }

    private void Shutdown()
    {
        _active = false;

        if (_source is not null)
        {
            _source.Dispose();
            _source = null;
        }

        if (_prosody is IDisposable disposableProsody)
        {
            disposableProsody.Dispose();
        }

        _mouth.Reset();
        _coloring.Reset();
        _events.Reset();
        _idle?.Reset();
        _vad.Reset();
        _prosody?.Reset();
        _clock.Reset();
    }
}

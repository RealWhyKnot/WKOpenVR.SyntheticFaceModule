using System.Diagnostics;
using WKOpenVR.FaceTracking.Sdk;
using WKOpenVR.SyntheticFaceModule.Audio;
using WKOpenVR.SyntheticFaceModule.Coloring;
using WKOpenVR.SyntheticFaceModule.Config;
using WKOpenVR.SyntheticFaceModule.Dsp.Vad;
using WKOpenVR.SyntheticFaceModule.Eyes;
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
public sealed class SyntheticFaceModule : IFaceTrackingModule, IDisposable
{
    private const double DiagnosticIntervalSeconds = 0.5;

    private readonly IAudioAnalysisSource? _injectedSource;
    private readonly Random _rng;
    private readonly bool _configIsFixed;

    private readonly MouthSolver _mouth = new();
    private readonly NoiseFloorTracker _noiseFloor = new();
    private readonly SpeechActivityDetector _vad = new();
    private readonly EmotionColoringLayer _coloring = new();
    private readonly SyntheticFrameMixer _mixer = new();
    private readonly float[] _mouthBuffer = new float[FaceExpressionCount.Value];
    private readonly float[] _emotionBuffer = new float[FaceExpressionCount.Value];
    private readonly Stopwatch _clock = new();

    private SyntheticConfig _config;
    private SyntheticConfigLoader? _configLoader;
    private IAudioAnalysisSource? _source;
    private IProsodyEstimator? _prosody;
    private ProceduralEyes? _eyes;
    private IFaceModuleLogger _log = NullFaceModuleLogger.Instance;

    private bool _expressionAllowed;
    private bool _eyeAllowed;
    private bool _active;
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
        new Version(0, 2, 0));

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
            _configLoader.WriteDefaultIfMissing();
            _configLoader.LoadNow();
            _config = _configLoader.Current;
        }

        _expressionAllowed = request.ExpressionAvailable;
        _eyeAllowed = request.EyeAvailable;

        bool wantExpression = _expressionAllowed && (_config.DriveMouth || _config.DriveEmotion);
        bool wantEyes = _eyeAllowed && _config.DriveEyes;
        _active = wantExpression || wantEyes;

        _eyes = new ProceduralEyes(_rng);
        _prosody = BuildProsodyEstimator(_config);

        if (_active)
        {
            _source = _injectedSource ?? new MicrophoneAudioSource(
                MicrophoneAudioSource.ResolveDeviceNumber(_config.MicDeviceNumber, _config.MicDeviceName),
                log: _log);
            if (wantExpression)
            {
                _source.Start();
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
            $"quality={_config.QualityMode} emoIntensity={_config.EmotionIntensity:F2} mouthIntensity={_config.MouthIntensity:F2} " +
            $"mic={mic} sdkAbi={FaceModuleAbi.Version} sdk={FaceModuleAbi.SdkVersion} " +
            $"config={_configLoader?.LoadedPath ?? "(programmatic)"}");

        return ValueTask.FromResult(new FaceModuleInitResult(
            EyeActive: wantEyes,
            ExpressionActive: wantExpression,
            HeadActive: false));
    }

    public ValueTask UpdateAsync(FaceFrame frame, CancellationToken cancellationToken)
    {
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

        bool driveMouth = _expressionAllowed && _config.DriveMouth;
        bool driveEmotion = _expressionAllowed && _config.DriveEmotion;
        bool driveEyes = _eyeAllowed && _config.DriveEyes;

        AudioAnalysisFrame? audio = null;
        if (_source is not null && _source.TryRead(out AudioAnalysisFrame? snapshot))
        {
            audio = snapshot;
        }

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
        bool emotionActive = driveEmotion && audio is not null && _prosody is not null;
        if (emotionActive)
        {
            prosody = _prosody!.Estimate(audio!, activity, isSpeech, dt);
            _coloring.Apply(prosody, _config.EmotionIntensity, dt, _emotionBuffer);
        }

        EyeOutput? eyes = null;
        if (driveEyes && _eyes is not null)
        {
            float arousal = prosody.SpeechActive ? prosody.Arousal : 0f;
            eyes = _eyes.Update(dt, arousal);
        }

        _mixer.Compose(
            frame,
            mouthActive ? _mouthBuffer : null,
            mouthActive,
            emotionActive ? _emotionBuffer : null,
            emotionActive,
            eyes);

        FaceFrameValidator.Sanitize(frame);

        LogDiagnostics(dt, audio, activity, isSpeech, prosody, eyes, frame);
        return ValueTask.CompletedTask;
    }

    public ValueTask TeardownAsync(CancellationToken cancellationToken)
    {
        Shutdown();
        return ValueTask.CompletedTask;
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
        _vad.Reset();
        _prosody?.Reset();
        _clock.Reset();
    }
}

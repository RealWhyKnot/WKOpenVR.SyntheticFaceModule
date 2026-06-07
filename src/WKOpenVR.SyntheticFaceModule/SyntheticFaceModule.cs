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
/// </summary>
public sealed class SyntheticFaceModule : IFaceTrackingModule, IDisposable
{
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
    private Action<string>? _log;

    private bool _expressionAllowed;
    private bool _eyeAllowed;
    private bool _active;
    private double _lastUpdateSeconds;

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
        _log = context.Log;

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
        }

        _log?.Invoke($"[synthetic] init: mouth={_config.DriveMouth} emotion={_config.DriveEmotion} " +
                     $"eyes={_config.DriveEyes} quality={_config.QualityMode}");

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

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using WKOpenVR.SyntheticFaceModule.Audio;
using WKOpenVR.SyntheticFaceModule.Prosody;

namespace WKOpenVR.SyntheticFaceModule.Ser;

/// <summary>
/// Quality-tier prosody estimator backed by a tiny ONNX speech-emotion model. Accumulates a rolling
/// window of MFCC frames and runs inference on a background thread at a low rate (not per audio
/// frame), so the per-frame cost stays negligible. No model weights are bundled (license gate): if
/// no license-clean model file is supplied, or the runtime/model fails to load, it reports
/// unavailable and the crossfade falls back to the heuristic. Output contract: input tensor
/// [1, T, mfcc]; first output's first element -> arousal, last element -> valence.
/// </summary>
public sealed class OnnxProsodyEstimator : IProsodyEstimator, IDisposable
{
    private readonly string? _modelPath;
    private readonly int _mfccCount;
    private readonly int _frameCount;
    private readonly double _intervalSeconds;
    private readonly Action<string>? _log;

    private readonly object _gate = new();
    private readonly float[] _rolling;
    private int _writeIndex;
    private int _filled;
    private double _lastFrameTimestamp = double.NegativeInfinity;

    private InferenceSession? _session;
    private string? _inputName;
    private bool _triedLoad;
    private bool _available;

    private Task? _inferenceTask;
    private double _lastInferenceSeconds = double.NegativeInfinity;
    private float _modelArousal = 0.5f;
    private float _modelValence;

    public OnnxProsodyEstimator(
        string? modelPath,
        int mfccCount = 13,
        int frameCount = 150,
        double intervalSeconds = 0.75,
        Action<string>? log = null)
    {
        _modelPath = modelPath;
        _mfccCount = mfccCount;
        _frameCount = frameCount;
        _intervalSeconds = intervalSeconds;
        _log = log;
        _rolling = new float[frameCount * mfccCount];
    }

    /// <summary>True once a model and runtime have loaded successfully.</summary>
    public bool Available => _available;

    public ProsodyState Estimate(AudioAnalysisFrame frame, float activity, bool isSpeech, float dtSeconds)
    {
        EnsureLoaded();
        if (!_available)
        {
            return new ProsodyState(0f, 0f, 0f, isSpeech);
        }

        PushFrame(frame);

        double now = frame.TimestampSeconds;
        bool idle = _inferenceTask is null || _inferenceTask.IsCompleted;
        if (isSpeech && idle && now - _lastInferenceSeconds >= _intervalSeconds && _filled >= _frameCount)
        {
            _lastInferenceSeconds = now;
            float[] snapshot = SnapshotOrdered();
            _inferenceTask = Task.Run(() => RunInference(snapshot));
        }

        float arousal;
        float valence;
        lock (_gate)
        {
            arousal = _modelArousal;
            valence = _modelValence;
        }

        float confidence = isSpeech ? Math.Clamp(activity, 0f, 1f) : 0f;
        return new ProsodyState(Math.Clamp(arousal, 0f, 1f), Math.Clamp(valence, -1f, 1f), confidence, isSpeech);
    }

    public void Reset()
    {
        lock (_gate)
        {
            _writeIndex = 0;
            _filled = 0;
            _lastFrameTimestamp = double.NegativeInfinity;
            _modelArousal = 0.5f;
            _modelValence = 0f;
        }
    }

    public void Dispose()
    {
        try
        {
            _inferenceTask?.Wait(250);
        }
        catch (Exception)
        {
            // Ignore inference shutdown errors.
        }

        _session?.Dispose();
        _session = null;
        _available = false;
    }

    private void EnsureLoaded()
    {
        if (_triedLoad)
        {
            return;
        }

        _triedLoad = true;
        if (string.IsNullOrWhiteSpace(_modelPath) || !File.Exists(_modelPath))
        {
            _log?.Invoke("[synthetic/ser] no model file; quality tier falls back to heuristic.");
            return;
        }

        try
        {
            _session = new InferenceSession(_modelPath);
            _inputName = _session.InputMetadata.Keys.FirstOrDefault();
            _available = _inputName is not null;
            _log?.Invoke(_available
                ? $"[synthetic/ser] loaded model {_modelPath}"
                : "[synthetic/ser] model has no inputs; falling back to heuristic.");
        }
        catch (Exception ex)
        {
            _available = false;
            _log?.Invoke($"[synthetic/ser] failed to load model ({ex.Message}); falling back to heuristic.");
        }
    }

    private void PushFrame(AudioAnalysisFrame frame)
    {
        if (frame.TimestampSeconds <= _lastFrameTimestamp)
        {
            return;
        }

        _lastFrameTimestamp = frame.TimestampSeconds;
        lock (_gate)
        {
            int offset = _writeIndex * _mfccCount;
            int n = Math.Min(_mfccCount, frame.Mfcc.Length);
            for (int i = 0; i < n; i++)
            {
                _rolling[offset + i] = frame.Mfcc[i];
            }

            _writeIndex = (_writeIndex + 1) % _frameCount;
            if (_filled < _frameCount)
            {
                _filled++;
            }
        }
    }

    private float[] SnapshotOrdered()
    {
        var ordered = new float[_frameCount * _mfccCount];
        lock (_gate)
        {
            for (int t = 0; t < _frameCount; t++)
            {
                int src = ((_writeIndex + t) % _frameCount) * _mfccCount;
                Array.Copy(_rolling, src, ordered, t * _mfccCount, _mfccCount);
            }
        }

        return ordered;
    }

    private void RunInference(float[] orderedMfcc)
    {
        try
        {
            if (_session is null || _inputName is null)
            {
                return;
            }

            var tensor = new DenseTensor<float>(orderedMfcc, new[] { 1, _frameCount, _mfccCount });
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs);

            float[] output = results.First().AsEnumerable<float>().ToArray();
            if (output.Length == 0)
            {
                return;
            }

            float arousal = Normalize01(output[0]);
            float valence = output.Length > 1 ? MathF.Tanh(output[^1]) : 0f;

            lock (_gate)
            {
                _modelArousal = arousal;
                _modelValence = valence;
            }
        }
        catch (Exception ex)
        {
            _available = false;
            _log?.Invoke($"[synthetic/ser] inference error ({ex.Message}); falling back to heuristic.");
        }
    }

    private static float Normalize01(float x)
    {
        if (x >= 0f && x <= 1f)
        {
            return x;
        }

        return 1f / (1f + MathF.Exp(-x));
    }
}

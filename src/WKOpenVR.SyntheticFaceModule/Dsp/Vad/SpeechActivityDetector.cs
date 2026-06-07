namespace WKOpenVR.SyntheticFaceModule.Dsp.Vad;

/// <summary>
/// Hysteresis voice-activity detector. Opens when RMS rises well above the adaptive noise floor,
/// closes only after dropping below a lower threshold for a hangover period. Suppresses breathing,
/// fans, and keystrokes (which sit just above the floor) so the mouth does not flap on non-speech.
/// </summary>
public sealed class SpeechActivityDetector
{
    private readonly float _openFactor;
    private readonly float _closeFactor;
    private readonly float _absoluteFloor;
    private readonly float _hangoverSeconds;

    private bool _speech;
    private float _hangoverRemaining;

    public SpeechActivityDetector(
        float openFactor = 3.5f,
        float closeFactor = 1.8f,
        float absoluteFloor = 2e-3f,
        float hangoverSeconds = 0.15f)
    {
        _openFactor = openFactor;
        _closeFactor = closeFactor;
        _absoluteFloor = absoluteFloor;
        _hangoverSeconds = hangoverSeconds;
    }

    public bool IsSpeech => _speech;

    /// <summary>Normalized speech strength, 0..1; 0 while closed.</summary>
    public float Activity { get; private set; }

    public bool Update(float rms, float noiseFloor, float dtSeconds)
    {
        float openThreshold = MathF.Max(noiseFloor * _openFactor, _absoluteFloor);
        float closeThreshold = MathF.Max(noiseFloor * _closeFactor, _absoluteFloor * 0.6f);

        if (!_speech)
        {
            if (rms > openThreshold)
            {
                _speech = true;
                _hangoverRemaining = _hangoverSeconds;
            }
        }
        else if (rms >= closeThreshold)
        {
            _hangoverRemaining = _hangoverSeconds;
        }
        else
        {
            _hangoverRemaining -= dtSeconds;
            if (_hangoverRemaining <= 0f)
            {
                _speech = false;
            }
        }

        if (_speech)
        {
            float span = MathF.Max(1e-4f, openThreshold * 2f - closeThreshold);
            Activity = Math.Clamp((rms - closeThreshold) / span, 0f, 1f);
        }
        else
        {
            Activity = 0f;
        }

        return _speech;
    }

    public void Reset()
    {
        _speech = false;
        _hangoverRemaining = 0f;
        Activity = 0f;
    }
}

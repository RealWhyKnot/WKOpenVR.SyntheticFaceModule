namespace WKOpenVR.SyntheticFaceModule.Coloring;

// One timed expression episode: smoothstep rise over the onset, hold, smoothstep fall over the last
// `offset` seconds of the duration. Triggering while active is ignored, which is every channel's
// refractory period.
internal sealed class Episode
{
    private readonly float _onset;
    private readonly float _duration;
    private readonly float _offsetStart;
    private float _t = -1f;

    public Episode(float onsetSeconds, float durationSeconds, float offsetSeconds)
    {
        _onset = onsetSeconds;
        _duration = durationSeconds;
        _offsetStart = Math.Max(onsetSeconds, durationSeconds - offsetSeconds);
    }

    public bool Active => _t >= 0f;

    public void Trigger()
    {
        if (_t < 0f)
        {
            _t = 0f;
        }
    }

    // Advances by dt and returns the envelope 0..1; 0 once the episode has run its course.
    public float Advance(float dtSeconds)
    {
        if (_t < 0f)
        {
            return 0f;
        }

        _t += dtSeconds;
        if (_t >= _duration)
        {
            _t = -1f;
            return 0f;
        }

        if (_t < _onset)
        {
            return SmoothStep01(_t / _onset);
        }

        if (_t < _offsetStart)
        {
            return 1f;
        }

        return 1f - SmoothStep01((_t - _offsetStart) / Math.Max(0.001f, _duration - _offsetStart));
    }

    public void Reset() => _t = -1f;

    private static float SmoothStep01(float x)
    {
        float t = Math.Clamp(x, 0f, 1f);
        return t * t * (3f - (2f * t));
    }
}

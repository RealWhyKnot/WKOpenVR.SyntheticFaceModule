using System.Text.Json;
using WKOpenVR.FaceTracking.Sdk;

namespace WKOpenVR.SyntheticFaceModule.Config;

/// <summary>
/// Loads <see cref="SyntheticConfig"/> from disk and hot-reloads it on change. Resolution order:
/// the stable per-user path <c>%LocalAppDataLow%\WKOpenVR\profiles\synthetic_face.json</c> (which an
/// overlay can write and which survives module updates), then a fallback file in the module's own
/// config directory. Missing or malformed files yield defaults; the last good config is retained on
/// a parse error. Reloads are throttled so polling from the per-frame update loop is cheap.
/// </summary>
public sealed class SyntheticConfigLoader
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _primaryPath;
    private readonly string _fallbackPath;
    private readonly IFaceModuleLogger? _log;
    private readonly double _reloadThrottleSeconds;

    private double _lastCheckSeconds = double.NegativeInfinity;
    private string? _loadedPath;
    private DateTime _loadedStampUtc;

    public SyntheticConfigLoader(string fallbackDirectory, IFaceModuleLogger? log = null, double reloadThrottleSeconds = 1.0)
    {
        _fallbackPath = Path.Combine(fallbackDirectory, "synthetic_face.json");
        _primaryPath = ResolvePrimaryPath();
        _log = log;
        _reloadThrottleSeconds = reloadThrottleSeconds;
        Current = new SyntheticConfig();
    }

    /// <summary>The most recently loaded configuration (never null).</summary>
    public SyntheticConfig Current { get; private set; }

    /// <summary>Stable per-user config path the loader prefers and writes defaults to.</summary>
    public string PrimaryPath => _primaryPath;

    /// <summary>The path the current config was loaded from, or null if defaults are in use.</summary>
    public string? LoadedPath => _loadedPath;

    /// <summary>
    /// Writes a default config to the primary path if neither the primary nor fallback file exists,
    /// so users have a discoverable, editable file. Best-effort; failures are logged, not thrown.
    /// </summary>
    public void WriteDefaultIfMissing()
    {
        if (File.Exists(_primaryPath) || File.Exists(_fallbackPath))
        {
            return;
        }

        try
        {
            string? dir = Path.GetDirectoryName(_primaryPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(_primaryPath, JsonSerializer.Serialize(new SyntheticConfig(), WriteOptions));
            _log?.Info($"[synthetic/config] wrote default config to {_primaryPath}");
        }
        catch (Exception ex)
        {
            _log?.Warn($"[synthetic/config] could not write default config ({ex.Message}).");
        }
    }

    /// <summary>Force an immediate (re)load from disk regardless of the throttle.</summary>
    public void LoadNow()
    {
        if (TryPickFile(out string path, out DateTime stampUtc) && TryReadConfig(path, out SyntheticConfig loaded))
        {
            Current = loaded;
            _loadedPath = path;
            _loadedStampUtc = stampUtc;
            _log?.Info($"[synthetic/config] loaded {path}");
        }
        else
        {
            Current = new SyntheticConfig();
            _loadedPath = null;
            _log?.Info("[synthetic/config] using defaults (no config file)");
        }
    }

    /// <summary>
    /// Reload if the file changed since the last load. Throttled by <c>reloadThrottleSeconds</c>.
    /// Pass a monotonic clock (e.g. a Stopwatch's elapsed seconds). Returns true if the config changed.
    /// </summary>
    public bool Poll(double nowSeconds)
    {
        if (nowSeconds - _lastCheckSeconds < _reloadThrottleSeconds)
        {
            return false;
        }

        _lastCheckSeconds = nowSeconds;

        bool found = TryPickFile(out string path, out DateTime stampUtc);
        bool changed = found ? (path != _loadedPath || stampUtc != _loadedStampUtc) : _loadedPath != null;
        if (!changed)
        {
            return false;
        }

        LoadNow();
        return true;
    }

    /// <summary>Reads and deserializes a config file. Returns false on any IO/parse failure.</summary>
    public static bool TryReadConfig(string path, out SyntheticConfig config)
    {
        config = new SyntheticConfig();
        try
        {
            string json = File.ReadAllText(path);
            SyntheticConfig? parsed = JsonSerializer.Deserialize<SyntheticConfig>(json, ReadOptions);
            if (parsed is null)
            {
                return false;
            }

            config = parsed;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool TryPickFile(out string path, out DateTime stampUtc)
    {
        if (File.Exists(_primaryPath))
        {
            path = _primaryPath;
            stampUtc = SafeWriteTimeUtc(_primaryPath);
            return true;
        }

        if (File.Exists(_fallbackPath))
        {
            path = _fallbackPath;
            stampUtc = SafeWriteTimeUtc(_fallbackPath);
            return true;
        }

        path = string.Empty;
        stampUtc = default;
        return false;
    }

    private static DateTime SafeWriteTimeUtc(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception)
        {
            return default;
        }
    }

    private static string ResolvePrimaryPath()
    {
        // %LocalAppDataLow% is not a SpecialFolder; derive it from the user profile.
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, "AppData", "LocalLow", "WKOpenVR", "profiles", "synthetic_face.json");
    }
}

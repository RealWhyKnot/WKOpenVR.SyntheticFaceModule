using WKOpenVR.FaceTracking.Sdk;
using WKOpenVR.SyntheticFaceModule;

var tests = new (string Name, Action Body)[]
{
    ("RMS noise gate", RmsNoiseGate),
    ("Envelope smoothing", EnvelopeSmoothing),
    ("Broad mouth shape mapping", BroadMouthShapeMapping),
    ("Module writes face frame", ModuleWritesFaceFrame),
    ("Package dependencies are allowed", PackageDependenciesAreAllowed)
};

foreach (var test in tests)
{
    test.Body();
    Console.WriteLine("PASS " + test.Name);
}

static void RmsNoiseGate()
{
    var processor = new SyntheticMouthProcessor();
    var silence = processor.Process(new AudioFeatureFrame(0.005f, 0.1f, 16000));
    AssertEqual(0.0f, silence.JawOpen);
    AssertEqual(1.0f, silence.MouthClosed);
}

static void EnvelopeSmoothing()
{
    var processor = new SyntheticMouthProcessor();
    var first = processor.Process(new AudioFeatureFrame(0.20f, 0.1f, 16000));
    var second = processor.Process(new AudioFeatureFrame(0.20f, 0.1f, 16000));
    var release = processor.Process(new AudioFeatureFrame(0.0f, 0.1f, 16000));

    AssertTrue(first.JawOpen > 0.0f);
    AssertTrue(second.JawOpen > first.JawOpen);
    AssertTrue(release.JawOpen < second.JawOpen);
    AssertTrue(release.JawOpen > 0.0f);
}

static void BroadMouthShapeMapping()
{
    var roundedProcessor = new SyntheticMouthProcessor();
    var frontProcessor = new SyntheticMouthProcessor();
    var rounded = roundedProcessor.Process(new AudioFeatureFrame(0.20f, 0.02f, 16000));
    var front = frontProcessor.Process(new AudioFeatureFrame(0.20f, 0.22f, 16000));

    AssertTrue(rounded.LipFunnel > front.LipFunnel);
    AssertTrue(front.MouthStretch > rounded.MouthStretch);
}

static void ModuleWritesFaceFrame()
{
    var source = new FixedAudioFeatureSource(new AudioFeatureFrame(0.20f, 0.22f, 16000));
    var module = new SyntheticFaceModule(source);
    var init = module.InitializeAsync(
        new FaceModuleContext(Path.GetTempPath()),
        new FaceModuleInitRequest(EyeAvailable: false, ExpressionAvailable: true, HeadAvailable: false),
        CancellationToken.None).AsTask().GetAwaiter().GetResult();

    var frame = new FaceFrame();
    module.UpdateAsync(frame, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    module.TeardownAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();

    AssertTrue(init.ExpressionActive);
    AssertTrue(source.Started);
    AssertTrue(source.Disposed);
    AssertTrue((frame.Flags & FaceFrameFlags.ExpressionsValid) != 0);
    AssertTrue(frame.GetExpression(FaceExpression.JawOpen) > 0.0f);
    AssertTrue(frame.GetExpression(FaceExpression.MouthStretchRight) > 0.0f);
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
        "BabbleCore"
    };

    foreach (var name in forbidden)
    {
        if (xml.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            throw new InvalidOperationException("Restricted dependency found: " + name);
        }
    }
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

sealed class FixedAudioFeatureSource : IAudioFeatureSource
{
    private readonly AudioFeatureFrame frame;

    public FixedAudioFeatureSource(AudioFeatureFrame frame)
    {
        this.frame = frame;
    }

    public bool Started { get; private set; }

    public bool Disposed { get; private set; }

    public void Start()
    {
        Started = true;
    }

    public bool TryRead(out AudioFeatureFrame next)
    {
        next = frame;
        return true;
    }

    public void Dispose()
    {
        Disposed = true;
    }
}

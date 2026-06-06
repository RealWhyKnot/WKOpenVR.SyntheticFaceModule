using WKOpenVR.FaceTracking.Sdk;

namespace WKOpenVR.SyntheticFaceModule;

public sealed class SyntheticFaceModule : IFaceTrackingModule
{
    private readonly IAudioFeatureSource source;
    private readonly SyntheticMouthProcessor mouth = new();
    private bool active;

    public SyntheticFaceModule()
        : this(new MicrophoneFeatureSource())
    {
    }

    public SyntheticFaceModule(IAudioFeatureSource source)
    {
        this.source = source;
    }

    public FaceModuleInfo ModuleInfo { get; } = new(
        "4df7850f-1d75-4665-9eab-6f07e0f3b5dc",
        "WKOpenVR Synthetic Face Module",
        "WhyKnot",
        new Version(0, 1, 0));

    public FaceModuleCapabilities Capabilities =>
        FaceModuleCapabilities.Expression | FaceModuleCapabilities.AudioInput;

    public ValueTask<FaceModuleInitResult> InitializeAsync(
        FaceModuleContext context,
        FaceModuleInitRequest request,
        CancellationToken cancellationToken)
    {
        active = request.ExpressionAvailable;
        if (active)
        {
            source.Start();
        }

        return ValueTask.FromResult(new FaceModuleInitResult(
            EyeActive: false,
            ExpressionActive: active,
            HeadActive: false));
    }

    public ValueTask UpdateAsync(FaceFrame frame, CancellationToken cancellationToken)
    {
        frame.Clear();

        if (!active || !source.TryRead(out var audio))
        {
            return ValueTask.CompletedTask;
        }

        var weights = mouth.Process(audio);
        ApplyMouth(frame, weights);
        FaceFrameValidator.Sanitize(frame);
        return ValueTask.CompletedTask;
    }

    public ValueTask TeardownAsync(CancellationToken cancellationToken)
    {
        active = false;
        source.Dispose();
        mouth.Reset();
        return ValueTask.CompletedTask;
    }

    private static void ApplyMouth(FaceFrame frame, MouthShapeWeights weights)
    {
        frame.SetExpression(FaceExpression.JawOpen, weights.JawOpen);
        frame.SetExpression(FaceExpression.MouthClosed, weights.MouthClosed);
        frame.SetExpression(FaceExpression.LipFunnelUpperRight, weights.LipFunnel);
        frame.SetExpression(FaceExpression.LipFunnelUpperLeft, weights.LipFunnel);
        frame.SetExpression(FaceExpression.LipFunnelLowerRight, weights.LipFunnel);
        frame.SetExpression(FaceExpression.LipFunnelLowerLeft, weights.LipFunnel);
        frame.SetExpression(FaceExpression.LipPuckerUpperRight, weights.LipPucker);
        frame.SetExpression(FaceExpression.LipPuckerUpperLeft, weights.LipPucker);
        frame.SetExpression(FaceExpression.LipPuckerLowerRight, weights.LipPucker);
        frame.SetExpression(FaceExpression.LipPuckerLowerLeft, weights.LipPucker);
        frame.SetExpression(FaceExpression.MouthStretchRight, weights.MouthStretch);
        frame.SetExpression(FaceExpression.MouthStretchLeft, weights.MouthStretch);
        frame.SetExpression(FaceExpression.MouthRaiserLower, weights.MouthRaiserLower);
    }
}

# WKOpenVR Synthetic Face Module

Downloadable native WKOpenVR face module built on `WKOpenVR.FaceTracking.Sdk`.

It drives a VRChat avatar's face from the microphone for users with no face- or eye-tracking
hardware:

- **Mouth** - two-stage lip-sync: an RMS/voice-activity jaw envelope plus an MFCC/spectral
  broad-viseme classifier (open / front / rounded vowels and fricatives) for nuanced lip shapes,
  with fast-attack/slow-release smoothing and coarticulation.
- **Emotion** - subtle, confidence-gated expression coloring (brows, cheeks, mouth corners, eye
  squint/wide) derived from prosody relative to a per-speaker baseline. It never overrides the
  viseme-critical mouth shapes.
- **Eyes** - optional procedural blinks (hazard-scheduled, fast-close/slow-open) and micro-saccade
  gaze. Off by default so VRChat's own idle eyes run; enable to drive the avatar's eye parameters.
- **Quality tier** (opt-in) - a tiny ONNX speech-emotion model can replace the heuristic estimator
  for better valence, with a smooth crossfade and heuristic fallback. No model weights are bundled;
  supply a license-clean model to enable it.

Settings are read from `synthetic_face.json` (under `%LocalAppDataLow%\WKOpenVR\profiles\`, falling
back to the module directory) and hot-reload at runtime. The DSP path is pure managed code and is
tested without audio hardware.

```powershell
.\build.ps1
.\test.ps1
.\pack.ps1
```

`pack.ps1` writes the installable payload to `artifacts\packages` and a registry-ready manifest
beside it. No public package feed or registry publication is performed by these scripts.
Tagged releases attach the module zip and manifest to GitHub Releases. The native module registry
points at the latest release asset by default, with prerelease versions exposed as beta entries.

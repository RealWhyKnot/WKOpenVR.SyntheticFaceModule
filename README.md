# WKOpenVR Synthetic Face Module

Downloadable native WKOpenVR face module built on `WKOpenVR.FaceTracking.Sdk`.

It drives a VRChat avatar's face from the microphone for users with no face- or eye-tracking
hardware:

- **Mouth** - two-stage lip-sync: an RMS/voice-activity jaw envelope plus an MFCC/spectral
  broad-viseme classifier (open / front / rounded vowels and fricatives) for nuanced lip shapes,
  with fast-attack/slow-release smoothing and coarticulation.
- **Emotion** - confidence-gated expression from prosody relative to a per-speaker baseline:
  subtle coloring on brows and eye squint/wide, plus a full-range smile/frown channel on the
  mouth corners with cheek raise, tuned to real face-tracking recordings. It never overrides
  the viseme-critical mouth shapes (jaw, lips, stretch, tightener).
- **Eyes** - optional procedural blinks (hazard-scheduled, fast-close/slow-open) and micro-saccade
  gaze. Off by default so VRChat's own idle eyes run; enable to drive the avatar's eye parameters.
- **Quality tier** (opt-in) - a tiny ONNX speech-emotion model can replace the heuristic estimator
  for better valence, with a smooth crossfade and heuristic fallback. No model weights are bundled;
  supply a license-clean model to enable it.

## Configuration

Settings are read from `synthetic_face.json` (under `%LocalAppDataLow%\WKOpenVR\profiles\`, falling
back to the module directory) and hot-reload at runtime. A default file is written on first run if
none exists, so it is easy to find and edit; see [`synthetic_face.example.json`](synthetic_face.example.json)
for an annotated copy. Unknown or missing fields fall back to the defaults below.

| Setting | Default | What it does |
| --- | --- | --- |
| `DriveMouth` | `true` | Microphone-driven mouth shapes (lip-sync). |
| `DriveEmotion` | `true` | Prosody-driven expression: subtle arousal-driven brow/eye coloring plus the episodic smile; never overrides the lip-sync mouth. |
| `DriveEyes` | `false` | Procedural blink + gaze. Off keeps VRChat's own idle eyes; on drives the avatar's eyes from this module. |
| `QualityMode` | `false` | Use a local ONNX speech-emotion model for a better arousal estimate (needs `EmotionModelPath`); CPU-only, opt-in. Falls back to the heuristic when no model is present. |
| `EmotionIntensity` | `1.0` | Scales the emotion coloring (0 disables, 1 = full conservative caps). |
| `SmileIntensity` | `0.0` | Scales the episodic smile on the mouth corners. Audio cannot detect smiling, so this is opt-in: 0 keeps the corners still, 1 = calibrated episode amplitudes. |
| `MouthIntensity` | `1.0` | Scales the mouth output. |
| `IdleIntensity` | `1.0` | Scales the always-on idle micro-motion (small brow/squint events while quiet; 0 disables). |
| `MicDeviceNumber` | `-1` | Capture device index; `-1` = system default. |
| `MicDeviceName` | `null` | Prefer the first capture device whose name contains this text (overrides the index when matched). |
| `EmotionModelPath` | `null` | Path to a license-clean ONNX speech-emotion model used when `QualityMode` is on. |

The DSP path is pure managed code and is tested without audio hardware. Set the host's log level to
Debug for a periodic per-stage snapshot, or Trace for a per-frame firehose, to diagnose behavior.

```powershell
.\build.ps1
.\test.ps1
.\pack.ps1
```

`pack.ps1` writes the installable payload to `artifacts\packages` and a registry-ready manifest
beside it. No public package feed or registry publication is performed by these scripts.
Tagged releases attach the module zip and manifest to GitHub Releases. The native module registry
points at the latest release asset by default, with prerelease versions exposed as beta entries.

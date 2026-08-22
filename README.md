# WKOpenVR Synthetic Face Module

Downloadable native WKOpenVR face module built on `WKOpenVR.FaceTracking.Sdk`.

It drives a VRChat avatar's face from the microphone for users with no face- or eye-tracking
hardware:

- **Mouth** - two-stage lip-sync: an RMS/voice-activity jaw envelope plus an MFCC/spectral
  broad-viseme classifier (open / front / rounded vowels and fricatives) for nuanced lip shapes,
  with fast-attack/slow-release smoothing and coarticulation.
- **Expression** - vocal-tone events judged against a per-speaker baseline (rising terminal,
  stress, sustained animation, monotone, rhythmic laughter) fire timed brow, eye and smile
  episodes whose onset, duration and peak come from real face-tracking recordings. It never
  overrides the viseme-critical mouth shapes (jaw, lips, stretch).
- **Eyes** - procedural blinks (hazard-scheduled, fast-close/slow-open) and micro-saccade gaze
  timed to tracked-eye recordings. On by default; turn off to keep VRChat's own idle eyes.
- **Quality tier** (opt-in) - a tiny ONNX speech-emotion model can replace the heuristic estimator
  for better valence, with a smooth crossfade and heuristic fallback. No model weights are bundled;
  supply a license-clean model to enable it.

## Configuration

Settings are read from `synthetic_face.json` (under `%LocalAppDataLow%\WKOpenVR\profiles\`, falling
back to the module directory) and hot-reload at runtime. The WKOpenVR app edits this file from the
module's own tab and writes only the settings you change; see
[`synthetic_face.example.json`](synthetic_face.example.json) for an annotated copy. Unknown or
missing fields fall back to the defaults below.

| Setting | Default | What it does |
| --- | --- | --- |
| `DriveMouth` | `true` | Microphone-driven mouth shapes (lip-sync). |
| `DriveEmotion` | `true` | Expression from vocal tone: question, emphasis, engagement, hesitation and laughter episodes on brows, eyes and mouth corners; never overrides the lip-sync mouth. |
| `DriveEyes` | `true` | Procedural blink + gaze timed to tracked-eye recordings. Off keeps VRChat's own idle eyes. |
| `QualityMode` | `false` | Use a local ONNX speech-emotion model for a better arousal estimate (needs `EmotionModelPath`); CPU-only, opt-in. Falls back to the heuristic when no model is present. |
| `EmotionIntensity` | `1.0` | Master gain over every tone-driven episode (0 disables, 1 = tracked amplitudes). |
| `QuestionEnabled` / `QuestionGain` | `true` / `1.0` | Brow raise on a rising pitch at the end of an utterance. |
| `EmphasisEnabled` / `EmphasisGain` | `true` / `1.0` | Outer-brow flash on a loud, high-pitched stress. |
| `EngagementEnabled` / `EngagementGain` | `true` / `1.0` | Eye widen while speech stays animated. |
| `HesitationEnabled` / `HesitationGain` | `true` / `1.0` | Brow furrow on a flat, sustained monotone. |
| `LaughterEnabled` / `LaughterGain` | `true` / `1.0` | Smile with cheek and eye squint on rhythmic laughter bursts. |
| `MouthIntensity` | `0.4` | Scales the mouth output. 0.4 matches tracked jaw travel; 1.0 is the raw solver level. |
| `BlinkRatePerMinute` | `15.9` | Spontaneous blinks per minute. Gaze shifts and vocal arousal still modulate it around this figure. |
| `IdleIntensity` | `1.0` | Scales the always-on idle micro-motion (small brow/squint events while quiet; 0 disables). |
| `VorGain` | `0.95` | How strongly the eyes counter-rotate against head movement, so gaze holds still while the head turns. 0 turns head coupling off. Needs a headset; ignored without one. |
| `VorRecenterSeconds` | `0.3` | How long the eyes take to drift back to centre once the head stops. |
| `HeadMovingThreshold` | `0.5` | Head speed, in radians per second, above which idle saccades pause: a turning head carries the gaze shift itself. |
| `SocialGazeEnabled` | `true` | Hold the listener's face more while quiet than while speaking, and glance away at the start of an utterance. |
| `AsymmetryIntensity` | `1.0` | Left/right imbalance in expressions: one side leads slightly. 0 restores a perfectly even face. |
| `DozeEnabled` | `true` | Let the eyes close when the head has been down, still and silent for a long time. Needs a headset; without one the eyes never close on their own. |
| `DozeDwellSeconds` | `45.0` | How long every condition must hold before the lids start to fall. |
| `SleepDwellSeconds` | `60.0` | Further dozing before the eyes go to nearly shut. |
| `DozePitchDegrees` | `25.0` | How far below its own resting angle the head must hang to count as down. The resting angle is learned, so a habitually low head does not trigger it. |
| `MicDeviceNumber` | `-1` | Capture device index; `-1` = system default. |
| `MicDeviceName` | `null` | Prefer the first capture device whose name contains this text (overrides the index when matched). |
| `EmotionModelPath` | `null` | Path to a license-clean ONNX speech-emotion model used when `QualityMode` is on. |

Head coupling and dozing both need a headset pose from the host. Running outside SteamVR, or on a
host that predates it, leaves the eyes exactly as they were before: no counter-rotation, and no
automatic eye closure from audio alone.

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

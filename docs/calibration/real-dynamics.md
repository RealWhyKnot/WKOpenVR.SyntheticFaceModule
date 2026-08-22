# Real-hardware expression dynamics

Tuning targets for the synthetic module, measured from Virtual Desktop (Quest) face tracking
recordings. Numbers come from `WKOpenVR.FaceModuleHost.exe analyze-face-replay` run over fifteen
sessions; `real-dynamics.json` next to this file holds the full output. All values are pre-driver
(recorded before eyelid sync, vergence, and shape tuning), which is the same point in the pipeline
where module output is recorded, so synthetic targets compare apples to apples.

## Source recordings

| file | frames | duration | rate |
|---|---|---|---|
| face_replay.20260815_181955_542.jsonl | 54,340 | 0.7 h | 21.5 Hz |
| face_replay.20260816_013029_641.jsonl | 514,432 | 6.7 h | 21.4 Hz |
| face_replay.20260816_183837_277.jsonl | 226,401 | 2.9 h | 21.4 Hz |
| face_replay.20260817_032449_048.jsonl | 253,806 | 3.3 h | 21.5 Hz |
| face_replay.20260818_042519_597.jsonl | 109,879 | 1.4 h | 21.5 Hz |
| face_replay.20260819_044540_011.jsonl | 3,921 | 0.1 h | 21.5 Hz |
| face_replay.20260819_045058_064.jsonl | 114,628 | 1.5 h | 21.5 Hz |
| face_replay.20260819_195151_429.jsonl | 162,858 | 2.1 h | 21.5 Hz |
| face_replay.20260820_004716_642.jsonl | 4,991 | 0.1 h | 21.5 Hz |
| face_replay.20260820_005115_355.jsonl | 392,394 | 5.1 h | 21.5 Hz |
| face_replay.20260821_005826_961.jsonl | 77,851 | 1.0 h | 21.4 Hz |
| face_replay.20260821_101636_387.jsonl | 171,496 | 2.2 h | 21.4 Hz |
| face_replay.20260821_220414_424.jsonl | 43,737 | 0.6 h | 21.1 Hz |
| face_replay.20260821_231129_158.jsonl | 105,973 | 1.4 h | 21.5 Hz |
| face_replay.20260822_004125_080.jsonl | 13,558 | 0.2 h | 21.5 Hz |

2,250,265 frames, 29.1 hours, one user and tracker, 2026-08-15 to 2026-08-22. Sessions vary a lot
by mood, so pooled medians are the tuning anchor and single-session extremes set the plausible
range.

## Headline targets (pooled medians)

| metric | value | maps to |
|---|---|---|
| blink rate | 15.9/min | `BlinkScheduler` blinks-per-minute |
| blink closed time p50 | 94 ms | close+hold+open total (~47 ms frames; treat as coarse) |
| double-blink fraction | 16% | `BlinkScheduler` double-blink probability |
| eyelid rest | mean 0.970, p50 1.000 | rest openness ~1.0 with occasional droop; do NOT lower baseline |
| gaze X p05..p95 | -0.346..0.238 | near-symmetric cone, slight left lean |
| gaze Y p05..p95 | -0.413..0.168 (p50 -0.128) | down-biased center, 0.30 up / 0.29 down from it |
| saccades | 102.4/min, amplitude p50 0.138 | `MicroSaccadeGaze` rate/reach |
| fixation dwell | p50 233 ms, mean ~550 ms, p90 past 1 s | log-normal dwell, median 0.233 s, sigma 1.3 |
| pupil | flat 0.5 (VD does not report pupil) | keep physiological defaults, nothing to match |

## Expression episodes (threshold crossings above 0.30)

| shape | /min | peak p95 | duration p50 | onset p50 | offset p50 |
|---|---|---|---|---|---|
| MouthCornerPullLeft | 1.4 | 1.00 | 4337 ms | 651 ms | 1723 ms |
| MouthFrownLeft | 0.2 | 0.67 | 376 ms | 92 ms | 185 ms |
| BrowInnerUpLeft | 0.4 | 0.54 | 2143 ms | 561 ms | 933 ms |
| BrowOuterUpLeft | 0.2 | 0.48 | 1862 ms | 513 ms | 607 ms |
| MouthStretchLeft | 0.2 | 0.84 | 280 ms | 93 ms | 140 ms |
| EyeWideLeft | 4.3 | 0.93 | 701 ms | 186 ms | 233 ms |
| EyeSquintLeft | 2.2 | 0.69 | 2381 ms | 701 ms | 746 ms |
| MouthPressLeft | 0.0 | 0.00 | absent; do not drive | | |

These rows are the episode timings the vocal-tone channels use: question = BrowInnerUp row (with
outer brow at 0.48/0.54), emphasis = BrowOuterUp row, engagement = EyeWide row, laughter =
MouthCornerPull row. Smile mean while speaking / quiet is 0.447 / 0.052: smiles co-occur with
speech, strongly and positively.

Companion ratios during smiles: `MouthDimple ~= 0.37 x MouthCornerPull`, `CheekSquint ~= 0.55 x`,
`EyeSquint ~= 0.35 x` (Duchenne pairing).

## Shape pairings and ratios observed in Virtual Desktop output

These pairs move identically, so synthetic output mirrors them:

- `MouthCornerSlant* = MouthCornerPull*` (ratio 1.0)
- `MouthUpperDeepen* = MouthUpperUp*` (ratio 1.0)
- `BrowPinch* = BrowLowerer*` (ratio 1.0)

Mutually exclusive: rounding (`LipFunnel*`, `LipPucker*`) never co-occurs with spreading
(`MouthStretch*`). Over 77,851 sampled frames, funnel-and-stretch above 0.10 together: 0 frames;
pucker-and-stretch: 0 frames. Funnel and pucker are NOT antagonists -- they co-occur in 81 frames,
about half of all funnel-active frames, so treat them as one rounding posture. Duty cycles are low:
funnel 0.22%, pucker 0.29%, stretch 3.28%, tightener 0.31% of all frames above 0.10. With speech in
about a quarter of frames, that is ~1% of speaking frames rounded and ~13% spread; the posture
classifier reproduces those fractions from the speaker's own centroid z-score (rounded below
z -2.3, spread above z +1.1).

Jaw-linked ratios over speaking frames (jaw > 0.2): `MouthLowerDown/JawOpen = 0.83` pooled,
`MouthUpperUp/JawOpen = 0.82`.

## Idle behavior (jaw quiet >= 1 s)

| shape | events/min | amplitude p90 |
|---|---|---|
| BrowInnerUpLeft | 16.3 | 0.083 |
| BrowOuterUpLeft | 12.5 | 0.066 |
| EyeSquintLeft | 29.4 | 0.106 |
| MouthCornerPullLeft | 5.0 | 0.093 |
| MouthPressLeft | 0.0 | 0.000 |

Idle micro-motion is constant, small, and brow/squint-led. MouthPress is essentially absent in
this tracker; do not schedule it.

## Per-frame slew (face_replay.20260822_004125_080.jsonl, 13,557 frame pairs)

`|dv| / dt` per second, p99 / p99.9 / max. A 21 Hz tracker under-samples fast motion, so these are
ceilings the 120 Hz synthetic stream must stay inside; a violation is a discontinuity, not a fast
expression.

| family | p99/s | p99.9/s | max/s |
|---|---|---|---|
| MouthUpperUp, MouthLowerDown | 2.5 | 9.3 | 13.8 |
| LipFunnel, LipPucker | 2.3 | 6.6 | 10.7 |
| MouthCornerPull/Slant, CheekSquint, Dimple | 2.8 | 6.3 | 9.6 |
| EyeSquint, EyeWide | 3.2 | 9.1 | 14.8 |
| JawOpen | 1.7 | 3.5 | 4.9 |
| MouthClosed, MouthStretch | 0.5 | 1.2 | 2.5 |
| Brow* | 1.0 | 2.1 | 3.7 |
| eye openness (blinks) | 13.4 | -- | 21.6 |
| gaze xy | 5.5 | -- | 10.6 |

The jaw smoother deliberately moves faster than the tracked jaw (lip-sync latency); tracked jaw
slew would need ~160 ms of lag to reproduce, so mouth shapes are judged against the solver's own
attack instead.

## Synthetic 0.4.0 before this calibration (face_replay.20260822_003347_286.jsonl, 7.6 min)

| metric | synthetic 0.4.0 | tracked | ratio |
|---|---|---|---|
| JawOpen max / active | 0.850 / 42.7% | 0.483 / 24.4% | 1.75x |
| LipFunnel active | 23.4% | 0.22% | 100x |
| LipPucker active | 21.6% | 0.29% | 75x |
| MouthStretch active | 0.2% | 3.28% | 0.06x |
| upperUp/jaw, lowerDown/jaw | 0.65 / 0.52 | 0.82 / 0.83 | |
| brow/eye/smile episodes | none | 0.2-4.3/min | |

## Channels not to chase

These diverge between any real reference and voice-driven synthetic output by design
(chewing, drinking, tongue play, physical artifacts). Ignore them in comparisons:
`Tongue*`, `CheekPuff*`, `CheekSuck*`, `JawLeft/Right/Forward/Clench`, `NoseSneer*`,
`LipSuck*`, `MouthUpperDeepen*` beyond its UpperUp pairing, `SoftPalateClose`,
`ThroatSwallow`, `NeckFlex*`.

## Acceptance bands for synthetic candidates

Compare a synthetic session against a reference from the table above using
`compare-face-replays` plus `analyze-face-replay` on both:

| channel | metric | band |
|---|---|---|
| JawOpen | activeFraction / mean | reference +-0.05 / +-0.03 |
| LipFunnel + LipPucker | activeFraction | < 1% |
| MouthStretch | activeFraction | 1..5% |
| MouthCornerPullL/R | episode rate / peak p95 | 0.5..3/min / 0.6..1.0 |
| BrowInnerUpL/R, EyeWideL/R | episode rate | 0.2..5/min |
| MouthClosed | p95 | <= 0.10 |
| eye openness | rest mean / blinks per min | >= 0.95 / 8..18 |
| gaze | Y p50 / Y p05 | -0.25..-0.05 / -0.50..-0.30 |
| overall | comparer | no stuck shapes; no divergence score > 0.25 on smile/brow/jaw |

Sessions differ by mood; judge against the band, not any single reference file.

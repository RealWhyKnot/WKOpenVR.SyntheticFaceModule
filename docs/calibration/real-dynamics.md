# Real-hardware expression dynamics

Tuning targets for the synthetic module, measured from Virtual Desktop (Quest) face
tracking recordings. Numbers come from `WKOpenVR.FaceModuleHost.exe analyze-face-replay`
run over six sessions; `real-dynamics.json` next to this file holds the full output.
All values are pre-driver (recorded before eyelid sync, vergence, and shape tuning),
which is the same point in the pipeline where module output is recorded, so synthetic
targets compare apples to apples.

## Source recordings

| file | date | frames | duration | notes |
|---|---|---|---|---|
| face_replay.20260729_202144_460.jsonl | 2026-07-29 | 680,239 | 8.8 h | long mixed session |
| face_replay.20260731_142249_644.jsonl | 2026-07-31 | 259,752 | 3.4 h | expressive, high squint |
| face_replay.20260801_175948_550.jsonl | 2026-08-01 | 330,622 | 4.3 h | mixed |
| face_replay.20260802_033622_208.jsonl | 2026-08-02 | 366,874 | 4.8 h | very social, heavy smiling |
| face_replay.20260803_235031_724.jsonl | 2026-08-03 | 260,005 | 3.4 h | quiet session |
| face_replay.20260804_091007_174.jsonl | 2026-08-04 | 71,154 | 0.9 h | brow-heavy, little smiling |

All report `module='Virtual Desktop'` at ~21.4 Hz effective. Sessions vary a lot
(smile strong-fraction spans 0.9%..21%), so pooled medians are the tuning anchor and
single-session extremes set the plausible range.

## Headline targets (pooled medians)

| metric | value | maps to |
|---|---|---|
| blink rate | 12.0/min (range 10.4..17.3) | `BlinkScheduler` blinks-per-minute |
| blink closed time p50 | 93 ms | close+hold+open total (~33 ms frames; treat as coarse) |
| double-blink fraction | 12% | `BlinkScheduler` double-blink probability (0.15 is fine) |
| eyelid rest | mean 0.980, p50 1.000, p10 0.857..0.999 | rest openness ~1.0 with occasional droop; do NOT lower baseline |
| gaze X p05..p95 | -0.334..0.233 (p50 ~-0.03) | near-symmetric cone, slight left lean |
| gaze Y p05..p95 | -0.395..0.112 (p50 -0.126) | down-biased center + asymmetric cone |
| saccades | ~110/min, amplitude p50 0.150 | `MicroSaccadeGaze` rate/reach |
| fixation dwell | p25/50/75/90 = 93/187/374..605/745..1258 ms | dwell distribution; current 0.4..3.0 s is ~5x too slow |
| pupil | flat 0.5 (VD does not report pupil) | keep physiological defaults, nothing to match |

## Smile and emotion channels

| metric | value |
|---|---|
| smile mean while speaking / quiet | 0.380 / 0.030 (per-session speaking mean 0.064..0.835) |
| smile strong-fraction while speaking / quiet | 31..85% / 1..13% |
| smile episode rate | 0.7/min pooled (0.5..2.0 by mood) |
| smile episode peak p95 | 1.00 (smiles saturate; cap ~1.0) |
| smile onset p50 (start to 90% of peak) | ~610 ms |
| smile offset p50 (90% of peak to end) | ~2.3 s, episode duration p50 ~5.3 s |
| frown episodes | 0.2/min, peak p95 0.79, onset ~93 ms, duration ~280 ms (fast microexpressions) |
| brow inner-up episodes | 0.2/min, peak p95 0.60, onset ~560 ms, duration ~2.3 s |
| eye-wide episodes | 2.9/min, peak p95 0.94, onset ~140 ms, duration ~610 ms (fast flashes) |
| eye-squint episodes | 1.3/min, peak p95 0.64, duration ~2.8 s (sustained, does the eyelid "character" work) |

Key correction to earlier assumptions: smiling co-occurs with speech, strongly and
positively. Drive smile probability up around speech activity; the guard needed is
only against additive saturation with viseme shapes, not a general speech damp.

## Shape pairings and ratios observed in Virtual Desktop output

These pairs move identically, so synthetic output should mirror them for a matching look:

- `MouthCornerSlant* = MouthCornerPull*` (ratio 1.0)
- `MouthUpperDeepen* = MouthUpperUp*` (ratio 1.0)
- `BrowPinch* = BrowLowerer*` (ratio 1.0)

Companion ratios during smiles: `MouthDimple ~= 0.37 x MouthCornerPull`,
`CheekSquint` rises with smile (Duchenne pairing; per-session mean up to 0.15,
p95 up to 0.96 -- far above any small coloring cap).

Jaw-linked ratios over speaking frames (jaw > 0.2):
`MouthLowerDown/JawOpen = 0.52` pooled (0.23..1.31), `MouthUpperUp/JawOpen = 0.65`
(0.51..1.07).

## Idle behavior (jaw quiet >= 1 s; 81..94% of session time)

| shape | events/min | amplitude p90 |
|---|---|---|
| BrowInnerUpLeft | 12.1 | 0.044 |
| BrowOuterUpLeft | 7.8 | 0.024 |
| EyeSquintLeft | 19.2 | 0.058 |
| MouthCornerPullLeft | 2.8 | 0.019 |
| MouthPressLeft | ~0 | 0.000 |

Idle micro-motion is constant, small, and brow/squint-led. MouthPress is essentially
absent in this tracker; do not schedule it.

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
| MouthCornerPullL/R | strong-fraction / episode peak p95 | 3..20% / 0.6..1.0 |
| BrowInnerUpL/R | activeFraction | 0.06..0.20 |
| EyeWideL/R | strong-fraction | <= 0.07 |
| MouthClosed | p95 | <= 0.10 |
| eye openness | rest mean / blinks per min | >= 0.95 / 8..18 |
| gaze | Y p50 / Y p05 | -0.25..-0.05 / -0.50..-0.30 |
| overall | comparer | no stuck shapes; no divergence score > 0.25 on smile/brow/jaw |

Sessions differ by mood; judge against the band, not any single reference file.

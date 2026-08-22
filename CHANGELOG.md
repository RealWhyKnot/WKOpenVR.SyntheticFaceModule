# Changelog

All notable user-visible changes to WKOpenVR Synthetic Face Module. The "Unreleased" section is auto-appended by `.github/workflows/changelog-append.yml` from conventional-commit subjects on `main`; tagged sections are promoted by `.github/workflows/release.yml` on a `v*` tag push.

The `release.yml` body for each tag is composed mechanically from the commit slice, package artifact hashes, and the templated sections under `.github/release-template/`. Hand-writing release bodies is not part of the workflow.

## Unreleased

### Added
- **eyes:** Couple gaze to head motion and add a head-down doze state (3167b81)

### Fixed
- **face:** Stop over-blinking, rest the mouth, and make laughter reachable (f78ced5)
- **prosody:** Detect unvoiced laughter (6360f69)

---

## v2026.8.22.0-beta -- 2026-08-22

### Added
- **module:** Fire brow, eye and smile episodes from vocal tone (d273ce0)
- **module:** Expose every expression channel in the settings descriptor (fb3156d)
- **module:** Share one episode envelope across expression channels (4b7cd59)
- **module:** Retune eyes and idle to tracked sessions and guard the output stream (70fd29b)

### Changed
- **module:** Drive the per-frame pipeline through a testable step (85e93b5)

### Fixed
- **module:** Drive only shape combinations a real face can hold (0a7f9e1)
- **module:** Correct the smile default in the settings descriptor (f84dff3)
- **module:** Match mouth level and lip posture duty cycles to tracked speech (326c4db)

---

## v2026.8.5.0-beta -- 2026-08-05

### Added
- **module:** Pace updates, report health, ship settings descriptor (9523aa2)
- **module:** Add calibrated smile and frown channel on the mouth corners (9772305)
- **module:** Add always-on idle micro-expression layer (47279ce)
- **module:** Calibrate eye dynamics to recorded gaze and blink behavior (5514f05)
- **module:** Widen lip-opener visemes to measured jaw ratios (15fba48)

---

## v2026.6.18.0-beta -- 2026-06-18

### Fixed
- **release:** Tolerate empty beta release notes (a864ed8)
- **release:** Reject empty beta release notes (e304f4f)

---

## v2026.6.16.0-beta -- 2026-06-16

### Fixed
- **release:** Use central date for release automation (f2ad8e4)

---

## v2026.6.15.0-beta -- 2026-06-15

_Maintenance release; see commit log for details._

---

## v2026.6.14.0-beta -- 2026-06-14

### Fixed
- **module:** Reduce synthetic mouth conflicts (bbf8f1a)

---

## v2026.6.7.1-beta -- 2026-06-08

### Added
- **module:** Leveled diagnostics, config toggles and default config (6ed49e2)
- **module:** Add procedural eyes, MFCC lip-sync, prosody and ONNX emotion (8ecc20d)

---

## v2026.6.7.0-beta -- 2026-06-08

### Fixed
- **module:** Include native registry metadata (ad54166)

---

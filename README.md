# WKOpenVR Synthetic Face Module

Downloadable native WKOpenVR face module built on `WKOpenVR.FaceTracking.Sdk`.

The module starts with microphone-level mouth motion. The DSP path is pure managed code and tested without audio hardware. Eye, emotion, and model-backed layers are left behind interfaces for later versions.

```powershell
.\build.ps1
.\test.ps1
.\pack.ps1
```

`pack.ps1` writes the installable payload to `artifacts\packages` and a registry-ready manifest beside it. No public package feed or registry publication is performed by these scripts.

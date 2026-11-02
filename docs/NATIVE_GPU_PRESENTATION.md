# Native GPU video and display color state

Settings → Video → Renderer → **Native GPU video and HDR display state** opens an owned native Direct3D11 session. It presents into a real child HWND without the WPF RV32 memory callback. This keeps the source's native video/color metadata in VLC's Direct3D11 output path; VLC owns its swap chain, subtitle composition and HDR/tone-mapping decisions.

This is a separate session. Choose or retain a source, then press Play source. Main playback stops when this session starts. Pause/resume, seeking, native-session volume and subtitle-track selection operate this player. Main Stop or another main source cancels it. Closing releases its player and native window. It does not inherit the main queue, HUD, arbitrary WPF overlays or live PCM DSP chain.

**Inspect display color state** reads the active Windows display topology, friendly names, color mode, per-channel bits and RGB/YCbCr encoding. Windows 11's newer Advanced Color query distinguishes SDR/WCG/HDR and HDR support/user state; older systems use the legacy query and explicitly leave HDR-specific support unknown. The legacy ICC filename is informational. It is not applied as a second transform on top of Windows Advanced Color, and it is not proof of the effective HDR calibration profile. Display inspection never changes the OS mode.

## Evidence and limits

The native fixture creates a 10-bit PQ/BT.2020 HEVC source with HDR10 mastering/content-light metadata, opens a real Direct3D11 video window, verifies video output/clock, pause/resume, main Stop forwarding and owned-player disposal. The installed GPU is NVIDIA GeForce RTX 4060 Ti.

Windows currently reports an HDR-capable **LG ULTRAGEAR** with HDR disabled and an SDR **Dell AW3418DW**. Both active paths report RGB, eight bits/channel and SDR. The application does not change these settings during testing. Native 10-bit source decoding on these outputs does not establish HDR swap-chain precision, luminance, gamut or colorimetric accuracy. F0085 therefore remains pending end-to-end acceptance on an enabled HDR/calibrated display path; the default WPF memory presenter is still SDR.

The separate [compatibility inspector](../FluxPlayer/Services/PlaybackCompatibility.cs) runs selected local fixtures against the four decoder policies using memory sinks. Its actual fixture set covers six audio codecs and five video codecs, with a malformed-file negative case: 44 valid file/policy combinations decode, and four malformed-file combinations are rejected. Audio evidence is PCM callback frames; video evidence is WPF-presented frames. Requested hardware policy and observed opaque surface are separate fields. This finite matrix does not close F0031's exhaustive compatibility certification.

Current machine-readable reports are [display-color-status.json](display-color-status.json), [native-gpu-output.json](native-gpu-output.json) and [playback-compatibility.json](playback-compatibility.json).

Primary references: the installed Windows SDK `wingdi.h` definitions for `DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2`/path/device packets, [Microsoft Advanced Color guidance](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range), [ICC behavior with Advanced Color](https://learn.microsoft.com/en-us/windows/win32/wcs/advanced-color-icc-profiles), and [VLC 3.0.21 Direct3D11 output](https://github.com/videolan/vlc/blob/3.0.21/modules/video_output/win32/direct3d11.c).

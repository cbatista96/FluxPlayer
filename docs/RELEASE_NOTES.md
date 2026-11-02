# FluxPlayer development preview

Windows x64 audio/video player. The portable ZIP includes the .NET runtime; extract the entire archive and run FluxPlayer.exe. Self-contained does not mean a single executable.

The current source includes a configurable video HUD, vendor-specific GPU decoding policy, automatic exact-duplicate index consolidation with durable exclusions, reviewed remote catalogue synchronization, OpenSubtitles downloads and Chromecast/DLNA sending panels, alongside the earlier playback/library/DSP tools.

This is development software. Read FEATURES.md for implementation and acceptance status. External-provider, receiver, physical-output and full hardware compatibility certification remain open. Unsigned artifacts are identified in release-manifest.json. MSIX/App Installer automatic updates are emitted only when a real signing certificate is supplied and verified. No automatic update mechanism is enabled by the unsigned ZIP.

Verify SHA256SUMS.txt before using downloaded artifacts. LICENSE and THIRD_PARTY_NOTICES.md accompany the application.

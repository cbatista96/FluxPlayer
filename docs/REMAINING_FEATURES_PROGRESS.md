# Remaining feature implementation

Source checkpoint: October 30, 2026. These three catalog gaps have implementations and categorized entry points. **The solution has now been compiled; automated LRCLIB transport/export and real loopback mobile tests pass.** Full provider, phone/LAN and OCR engine acceptance remain open. [Continuation scope and validation](CONTINUATION_PROGRESS.md). The table below tracks complete acceptance rather than just successful compilation or focused tests.

| Catalog | Implemented source scope | Settings entry | Acceptance |
| --- | --- | --- | --- |
| F0051 | Explicit LRCLIB search and metadata/duration matching, synchronized/plain/instrumental result review, current-session synchronized lyrics, new LRC/TXT export | Audio → Lyrics → Online lyrics · LRCLIB | [ ] Untested |
| F0083 | Local video-region sampling, installed Tesseract OCR, approximate cue timing, adjacent duplicate merging, manual text/timing corrections and new SRT/WebVTT export | Subtitles → Recognition and extraction → Burned-in subtitle OCR | [ ] Untested |
| F0090 | Paired mobile browser page controlling the main player's existing queue, transport, seeking, volume, mute and speed | Playback → External control → Mobile web remote | [ ] Untested |

## Online lyrics

Search sends the metadata/query you enter to [LRCLIB](https://lrclib.net/docs). The provider is fixed to HTTPS LRCLIB; there is no startup search, publish operation, automatic attachment or provider fallback. Results distinguish synchronized, plain text and instrumental records. Select and inspect a result before explicitly applying synchronized lyrics to the currently open track. Export creates a new copy without overwriting an existing destination. Requests have a 30-second timeout, an 8 MiB response limit and at most 100 displayed records. A rate-limit response asks you to wait without retrying automatically.

## Burned-in subtitle OCR

Install FFmpeg and a trusted [Tesseract](https://tesseract-ocr.github.io/tessdoc/Command-Line-Usage.html) executable with the desired language data separately. Choose a local video, the engine path and languages such as `eng+spa`. Select a region using normalized left/top/width/height coordinates. Recognition is limited to a 1–120 second excerpt, sampling every 1–5 seconds, scaled to at most 1280×720, with at most 120 images and a 15-minute job deadline. Tesseract receives a 30-second per-sample deadline and a two-thread limit.

Cue timings describe sampling intervals; recognition can miss short captions, misread text or extend cues beyond the actual caption's disappearance. Review every cue. Corrections affect the draft; export creates a new SRT or WebVTT file. Replacing a nonempty draft requires an explicit review dialog. This is subtitle text recognition, not audio transcription or translation. No engine is bundled or downloaded automatically.

## Mobile browser remote

The remote is disabled until explicitly started. Choose an available private IPv4 interface for a phone, or loopback for the same computer. Port 18766 is the default. Open the displayed address and enter the session pairing code. Windows URL reservation and firewall permissions may need configuration; the application does not change them automatically.

The listener uses HTTP and requires a trusted local network. It does not provide Internet access, TLS, arbitrary source opening, file browsing or media downloads. A random 256-bit session credential authenticates API requests; the page keeps it only in memory. Closing the panel or stopping the listener revokes pairing. Connections are limited to four concurrent requests; state polling runs every two seconds only while the paired page is visible. The page displays at most 200 queued titles; the total queue count is separate. No service runs automatically at application startup.

## Implementation and deferred acceptance

Services: `OnlineLyrics.cs`, `SubtitleOcr.cs`, `MobileRemote.cs`. Panels: `RemainingFeatureWindows.cs`. Browser asset: `Assets/mobile-remote.html`, embedded in the assembly. Entry points use existing Settings categories and Fluent workbench controls; only lyrics search and the remote panel are quick-access actions.

- [x] Compile the WPF application and retrieve the packaged embedded page with CSP/nonce checks. Visual/mobile review remains open.
- [ ] Exercise LRCLIB success, empty, rate-limit, malformed and canceled responses; inspect lyric matching and exports.
- [ ] Exercise OCR engine/language failures, cancellation, sample limits, Unicode correction and SRT/WebVTT output.
- [ ] Exercise URL/firewall permission failures, pairing, origin/host rejection, mobile controls, background tabs and listener shutdown.
- [ ] Review keyboard access, screen readers, light/dark themes, DPI and small-window layouts.

The broader megalist is still incomplete. The public catalog retains pending work for configurable HUD, advanced touch, extensions, GPU policy, exhaustive formats/hardware, native DSD/bit-perfect output, gapless/crossfade validation, advanced DSP quality, library synchronization, subtitle downloads, HDR/color validation, casting, release automation, signed updates, duplicate consolidation, accelerated subtitle composition, linked-chapter playback and physical surround certification. Individual original requirements and partial coverage remain in the original inventory. No GitHub publication was performed.

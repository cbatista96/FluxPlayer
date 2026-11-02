# Online sources, mobile control and catalogue synchronization — October 31, 2026

Eleven more capability IDs passed individual acceptance: F0066, F0090, F0359 and F0363–F0370. This continuation adds twelve distinct test groups, including a real browser check in addition to native mobile-control checks. Earlier evidence remains scoped separately in [the previous session](LOCAL_SESSION_ACCEPTANCE.md). No publication or signing was performed.

## Changes and actual acceptance

| IDs | Evidence | Limits |
| --- | --- | --- |
| F0066 | Real WsgiDAV 4.4.0 with two isolated library profiles: missing-object preview, conditional creation, metadata union, new remote readback, stale-preview rejection, cancellation and cleanup. A transport hook edits the real served file between final GET and PUT; WsgiDAV rejects the obsolete If-Match and preserves the competing bytes. | Catalogue metadata only; local media paths do not relocate files. The server must enforce conditional PUT. A failed upload can leave the documented local merge saved. |
| F0090 | Actual paired HTTP requests control native playback, pause, absolute seek, speed, volume, mute and queue selection. Invalid commands/origins are rejected, and stopping/restarting revokes old pairing. Isolated Edge at 390 × 844 performs pairing, play/pause, speed and mute through the actual page; native settings are checked afterward. Disconnect clears the input and neither localStorage nor sessionStorage retains the code. | Loopback IPv4 on this machine; physical phone/Wi-Fi/router/firewall acceptance is separate. Previous/next across multiple queue entries are not covered by this new fixture. |
| F0359 | Independently authored PGS packets are muxed alongside a text track into a real MKV, then extracted by the product to SUP. An independent FFmpeg decode compares visible bitmap pixels before/after extraction. The text track is rejected as SUP; existing output and original input are preserved. | A synthetic bitmap/clear display set; not every Blu-ray subtitle variation. The fixture explicitly preserves timestamps while muxing and comparing so FFmpeg input normalization does not change the test's timeline. |
| F0363–F0365 | Real yt-dlp inspects local HTML5 video/audio pages and direct media, then actual native playback decodes selected formats. URL boundaries, pre-cancellation and an explicit trusted executable path are checked. | No commercial provider, browser cookies, account or DRM support is implied. Unknown source codecs can remain unknown until playback. |
| F0366 | Real yt-dlp identifies HLS without ENDLIST as live; the resolved URL starts native decoding. | Finite, pregenerated synthetic segments advertised without ENDLIST; continuously changing broadcasts and provider authorization are outside this fixture. |
| F0367 | Two HTML5 entries sharing one page retain separate playlist positions. Each resolves independently; selecting/downloading the second returns the exact authored audio bytes instead of the first entry's video. | At most 100 entries. A provider can change its playlist after inspection; source immutability is not guaranteed. |
| F0368–F0369 | Real native-format video/audio downloads are published as new files and independently decoded. Existing files, foreign selections and live-download attempts are rejected. | No transcoding or merging of separate audio/video streams. The actual tool and provider must support the source. |
| F0370 | An authored Spanish WebVTT track is discovered and downloaded byte-for-byte, including Unicode, and independently parsed. Existing destinations and non-WebVTT responses are rejected. | Authored VTT only; no subtitle generation or automatic translation. |

The resolver now handles direct-media metadata without a formats array, reports a multi-entry page as a playlist instead of silently displaying no formats, and preserves the selected occurrence when inspecting or downloading a shared page. The source/playlist panels carry the explicitly selected portable executable through their actions. Remote pages require HTTPS; HTTP is accepted only for numeric loopback IP addresses without credentials. The live-download guard also applies in the service, not only in the panel.

Nineteen actual feature windows, including both online-source panels, passed the existing WPF layout and dark/light input contrast checks. The online-source screenshot was also visually inspected. The mobile screenshot was visually inspected at phone width; horizontal scroll width equaled client width (390 px).

## Server compatibility finding

dufs 0.47.0 provides strong GET ETags but does **not** enforce the tested stale If-Match on overwrite. It is suitable for the separately accepted file-transfer workflows, but it is not certified for catalogue synchronization. ETag presence alone does not prove conditional-write support. The production synchronization service already requires that support; no unsafe server probe that writes to a user's real catalogue was added. WsgiDAV passed the actual conflicting-write test. Failed exploratory fixtures remain under ignored session-state, and are excluded from the passing report.

## Tools and provenance

- [yt-dlp 2026.10.26](https://github.com/yt-dlp/yt-dlp/releases/tag/2026.10.26), official Windows x64 executable. SHA-256 `f5389d3698ecde4ef06f4e6b1ce0294fe9ac1396228c8990b8d3416d6d177a7d` matched the GitHub release API digest before execution. The [versioned generic extractor](https://github.com/yt-dlp/yt-dlp/blob/2026.10.26/yt_dlp/extractor/generic.py) documents direct, HTML5 and live-manifest handling. The portable test copy is `session-state/tools/yt-dlp-2026.10.26/yt-dlp.exe`; it is not bundled or installed globally.
- [WsgiDAV 4.4.0](https://github.com/mar10/wsgidav/releases/tag/v4.4.0), official wheel SHA-256 `bd8ac483c63e11e9be3a17dcd5158da76c181fdfe6f5ce7066213b956002e344`, matched the release API. Cheroot 11.1.2 and dependencies were placed only under ignored `session-state/tools/python-3.14-embed/packages`; the package-manager report retains their URLs and hashes. The child server binds 127.0.0.1, authenticates a disposable test account and serves only its newly generated fixture directory. No Windows service was installed.
- [CPython embeddable distribution](https://docs.python.org/3.14/using/windows.html#the-embeddable-package), official 3.14.0 ZIP from python.org, observed SHA-256 `8d4d3590c10449d78aa4375f534e6d5f3027d67fdc362dd1a882279db6f90fdf`. It is extracted only under ignored test-tools, with isolated module paths. No global Python installation, PATH change or certificate change was made.
- PGS packet layout was checked against the [FFmpeg PGS decoder](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/pgssubdec.c). Tests use the already available FFmpeg/ffprobe binaries.
- Browser verification used the [agent-browser skill](<C:/Users/World Marketing Labs/.agents/skills/agent-browser/SKILL.md>) through a temporary npx CLI and a dedicated isolated Edge session, which was closed afterward. Browser evidence is `session-state/tests/mobile-browser-verification.json` and `mobile-390.png`; the temporary pairing details are deleted when the test ends.

## Reproduction

Build Release x64 first. Test runners overwrite their results file, so run them sequentially and retain each result before the next invocation.

```powershell
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- '--filter=yt-dlp actual acceptance'
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- '--filter=F0359 Subtitle'
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- '--filter=F0066 WebDAV'
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- '--filter=F0090 mobile'
```

The optional actual-browser fixture requires `FLUXPLAYER_TEST_MOBILE_BROWSER=1`. It writes temporary pairing information to `session-state/tests/mobile-browser-ready.json` and waits for the external browser driver to create the indicated completion file. The driver must exercise the page and native settings, not simply create the marker; ordinary runs do not wait for a browser. The Python/WsgiDAV fixture runs only when its local portable dependency is present. yt-dlp can also be selected with `FLUXPLAYER_TEST_YTDLP`; the product UI's **Choose yt-dlp…** supports an explicit portable executable without global PATH configuration.

[Current consolidated evidence](local-completion-test-results.json) and [remaining work](LOCAL_COMPLETION_PROGRESS.md) distinguish these bounded checks from external account, device and hardware certification.

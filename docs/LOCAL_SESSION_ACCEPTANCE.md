# Local session continuation — October 31, 2026

94 additional capability IDs passed individual functional checks after the [previous local backend checkpoint](LOCAL_BACKEND_ACCEPTANCE.md). The application remains local: no release upload, signing certificate, Windows service installation or personal server/library change was performed.

## Completed implementation and fixes

- **Automatic media profiles:** editing preserves first-match priority. The panel now supports moving and deleting rules. Validation rejects ambiguous negative volumes, duplicate identities/extensions and control characters. A pending activation cannot reactivate rules after Disable or shutdown.
- **Appearance:** custom white/black accents retain readable text; selection fills and text accents have separate resources. Actual save/apply actions persist preferences. An invalid background is decoded/rejected before saving; a failed apply restores the previous saved settings. The main idle canvas keeps its current image until replacement decoding succeeds.
- **Equalizer docking:** initializes when either window loads, follows owner movement/size and child resizing, and detaches handlers on disposal.
- **Windows subtitle OCR:** an empty engine path uses an installed Windows OCR language. The panel lists available language tags and retains explicit Tesseract selection, including combined Tesseract models. Recognition, adjacent-sample merging, corrected Unicode text/timestamps, SRT/WebVTT exports and cancellation were exercised with an actual burned-caption video. FFmpeg remains required for frame extraction. Recognition accuracy for arbitrary fonts, languages and real-world footage is not certified.
- **WebDAV resume:** file locking protects each owned partial and its ETag together, including across source instances/processes. A complete partial retained after interrupted publication recovers from HTTP 416 by making a fresh full request. Only completed output is published.
- **Subsonic offline selections:** repeated playlist occurrences download each remote source once, preserving first-selection order. Offline playlists still retain their selected duplicate occurrences; the second copy no longer stops the remaining download batch with a filename conflict.
- **Cover Art Archive:** the provider's documented and actual JSON contains legacy HTTP image URLs. Known artwork origins are upgraded to HTTPS; each image redirect is checked with a five-redirect limit. Unsupported hosts, credentials and nonstandard ports are rejected. Both real cover sides now download and decode successfully.
- **GPU subtitle regression:** the previous test assumed an 80 ms delay was enough for WPF presentation after native subtitle arrival. It now waits for a presented video frame and an arranged, populated subtitle layer. Hardware-surface and colored-pixel assertions remain unchanged; both SRT and ASS passed in the full ordered regression. Failed composites/native regions are retained as diagnostic PNGs if a future check fails.

## Individual acceptance scope

| IDs | Runtime evidence | Limits |
| --- | --- | --- |
| F0272 | Actual rule table move/delete/activate controls, persisted priority, case-insensitive matching during native playback and activation/disable ordering. | Session snapshots and explicit activation; no arbitrary conditional scripting. |
| F0286 | Four independently constructed GIF frames with partial-frame placement, background/previous disposal, delays and finite repeats. The actual photo window pauses/resumes/replays, stops on minimize and releases its image on close. | Synthetic two-pixel animation and cancellation, not every GIF encoder or malformed-file variation. |
| F0288–F0290 | Actual appearance save/apply controls, persisted accent/background/dock settings, theme resource readback, background decoding, failed-save/apply preservation and real owned-window docking. | Current desktop/DPI; a complete multi-monitor/DPI matrix remains outside these tests. |
| F0321–F0332 | Real Navidrome session authentication, artists/albums/tracks/search/discovery/favorites/playlists, original playback through the authenticated relay, requested MP3 transcoding, byte-identical downloads and offline local M3U8 export. | Navidrome 0.64.2, two synthetic FLAC tracks and a disposable account/database. |
| F0333–F0342 | Real dufs PROPFIND, recursive listing, authenticated ranges/relay playback, downloads/batch, conditional PUT, MKCOL, Unicode relative M3U8, range resumption and ETag cache revalidation/change. Existing local/remote destinations remain intact. | dufs 0.47.0 over loopback, not universal server certification or a deployed HTTPS account. The server's PROPFIND omits ETags; GET supplies the strong validator used by resume/cache. |
| F0343–F0352 | Live public MusicBrainz searches, discography, release tracks and relationships; real Cover Art Archive front/back images; actual review/apply buttons writing selected fields to a synthetic FLAC, rejecting stale review and preserving decoded PCM/unselected tags. | Read-only public API access plus explicitly reviewed changes to a test file. These checks are opt-in and depend on provider availability. |
| F0381–F0390 | Real Jellyfin session-token rejection/acceptance, library roots, movies, series/seasons/episodes, music albums/tracks, search, resume/favorites, relayed playback and original-byte downloads. | Jellyfin 12.4, isolated music/movie/television libraries with local metadata and internet providers disabled. |
| F0581–F0583, F0588–F0610 | 26 real Subsonic actions: facets/discovery/metadata/search, playlist occurrence review and explicit create/append/rename/visibility/removal, favorites/ratings, fixture listening reports, bookmarks, authored local lyrics and library roots. Mutations are read back from the server; duplicate-occurrence removal retains the other occurrence. | External enrichment F0584–F0587 remains unverified because external services are deliberately disabled in the fixture. No real listening account receives a report. |
| F0611–F0630 | All 20 additional Jellyfin actions, with positive local genres/artists/years/studios/tags, discovery/next-up/similar items, actual special features/trailers/details, state/ratings and a user-owned ordered playlist. State changes and playlist order are checked in independent server responses. | Local fixture server/account only. Emby, Plex and UPnP retain independent acceptance status. |
| F0083 | Actual Windows OCR on three video samples, merged recognition and reviewed SRT/WebVTT output with exact millisecond timing and Unicode. | Installed English Windows OCR model and a clear synthetic caption. The separate Tesseract path still depends on its installation/models. |

## Reproduction and isolation

The executable tests use trusted portable tools under ignored `session-state/tools`, or these explicit overrides: `FLUXPLAYER_TEST_DUFS`, `FLUXPLAYER_TEST_NAVIDROME`, `FLUXPLAYER_TEST_JELLYFIN`. They create fresh fixture directories under `session-state/tests`, bind only `127.0.0.1`, drain output and terminate only their owned child process trees. dufs permits fixture uploads but not deletion or outside-root symlinks. Navidrome external services/telemetry/plugins and Jellyfin discovery/internet metadata providers are disabled. No system-wide service is registered.

```powershell
dotnet build FluxPlayer.sln -c Release -p:Platform=x64 --no-restore
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- '--filter=Personal experience native acceptance'
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- '--filter=WebDAV native acceptance'
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- '--filter=Subsonic native acceptance'
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- '--filter=Jellyfin native acceptance'
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- '--filter=Windows OCR native acceptance'
$env:FLUXPLAYER_TEST_PUBLIC_METADATA = '1'
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- '--filter=Public metadata acceptance'
```

Each invocation replaces `session-state/tests/results.json`; run them sequentially. Public metadata checks are absent from ordinary runs unless explicitly enabled. The current consolidated results and package verification are linked from [local progress](LOCAL_COMPLETION_PROGRESS.md).

## Dependency and API provenance

- [dufs 0.47.0](https://github.com/sigoden/dufs/releases/tag/v0.47.0), official x64 Windows ZIP, SHA-256 `39a000b70de423b7200f17099fa734cb6e154d8c6ef6a7d4ec9b2961cb66d0f1`, matched the release API digest.
- [Navidrome 0.64.2](https://github.com/navidrome/navidrome/releases/tag/v0.64.2), official x64 Windows ZIP, SHA-256 `72e06c9101fca6b14ff1437f9a56beb7ad6fe4bd7f25ff62cd5b70a075fee305`, matched the release API digest. Its documented [portable Windows usage](https://www.navidrome.org/docs/installation/windows/) and versioned source were used to configure the isolated fixture.
- [Jellyfin Windows downloads](https://jellyfin.org/downloads/windows/) / [12.4 archive mirror information](https://repo.jellyfin.org/files/server/windows/latest-stable/amd64/jellyfin_12.4-amd64.zip?mirrorlist), SHA-256 `d62b5b30390213680fd1fae5484082ae27b2a778cfd40a473ad7b1e4147d75a6`, matched before extraction. Versioned controller/configuration sources define the setup and requests used by the fixture.
- [Windows OCR API](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine) provides installed-language discovery and recognition. Its actual SDK projection and runtime were exercised by the unpackaged test application.
- [MusicBrainz rate limiting](https://musicbrainz.org/doc/MusicBrainz_API/Rate_Limiting) is respected by the shared 1.1-second request gate. The release UUID used for track/cover acceptance comes from the official [Cover Art Archive API example](https://musicbrainz.org/doc/Cover_Art_Archive/API); that example also documents legacy HTTP image links.

The portable test servers are not bundled with FluxPlayer. Actual account/device integrations and the original broad parent requirements are not silently certified by these narrower checks.

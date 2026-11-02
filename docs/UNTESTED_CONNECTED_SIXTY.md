> **Verification update — 2026-10-28:** F0371–F0380 passed acceptance tests. F0321–F0370 retain their unverified status. The original deferral narrative below is historical. Current evidence: [100 verified capabilities](VERIFIED_HUNDRED.md).

**Current acceptance update:** Subsonic, WebDAV, MusicBrainz and reviewed text-subtitle production now have actual functional acceptance within their documented bounds. F0359 and F0363–F0370 now also pass actual PGS and yt-dlp acceptance; see [online continuation](LOCAL_ONLINE_ACCEPTANCE.md). See [current session](LOCAL_SESSION_ACCEPTANCE.md) and [previous backend acceptance](LOCAL_BACKEND_ACCEPTANCE.md). Source-only statements below describe the earlier checkpoint.

# Sixty connected-media, subtitle and restoration capabilities — untested

**Source implementation only. This entire batch was not compiled, launched, exercised, visually reviewed, benchmarked or tested.** No application API connection, relay listener, metadata query, upload, download, tag write, external-tool probe or media job was executed during development. Only documentation was consulted. The existing executable predates all five untested batches, and historical passing results do not validate these additions.

The public catalog now tracks **179 previously implemented, 180 implemented without testing, and 21 pending capabilities (380 total)**. Original inventory counts are **492 previously verified, 116 implemented without testing, and 1,082 pending or partial (1,690 total)**. The two catalogs intentionally count different things: independently usable subfeatures can share one original parent requirement. Original references below are grouped once, with aggregate evidence; repeated references do not count as additional completed original requirements. Asterisks indicate broader requirements that remain partial.

## Implemented — verification pending

All boxes remain unchecked because verification is deferred, not because source code is absent.

| Pending verification | Capability | Original parent |
|---|---|---|
| [ ] F0321 | Explicit session-authenticated Subsonic connection | R0563 |
| [ ] F0322 | Subsonic artist-index browsing | R0563 |
| [ ] F0323 | Albums of a selected remote artist | R0563 |
| [ ] F0324 | Tracks of a selected remote album | R0563 |
| [ ] F0325 | Remote track text search | R0563 |
| [ ] F0326 | Newest/recent/random album views | R0563 |
| [ ] F0327 | Read-only remote starred-track browsing | R0563 |
| [ ] F0328 | Remote playlists and ordered playlist tracks | R0563 |
| [ ] F0329 | Original-format remote stream playback | R0563 |
| [ ] F0330 | Selected-bitrate server transcoding request | R0563 |
| [ ] F0331 | Selected-track downloads to new local copies | R0563 |
| [ ] F0332 | Offline M3U8 from downloaded selections | R0563 |
| [ ] F0333 | Authenticated WebDAV collection browsing | R0443* |
| [ ] F0334 | Bounded recursive WebDAV file view | R0443* |
| [ ] F0335 | Relayed playback of authenticated WebDAV files | R0443* |
| [ ] F0336 | New-copy single-file WebDAV downloads | R0443* |
| [ ] F0337 | Selected-file WebDAV download batches | R0443* |
| [ ] F0338 | Explicit no-replace WebDAV file uploads | R0443* |
| [ ] F0339 | Explicit remote collection creation | R0443* |
| [ ] F0340 | Remote M3U/M3U8 reading and relative sources | R0443* |
| [ ] F0341 | Resumable WebDAV downloads | R0443* |
| [ ] F0342 | ETag-revalidated local media cache | R0443* |
| [ ] F0343 | MusicBrainz recording search | R0573* |
| [ ] F0344 | MusicBrainz release search | R0573* |
| [ ] F0345 | MusicBrainz artist search | R0573* |
| [ ] F0346 | Online artist discographies | R0127* |
| [ ] F0347 | Release media and numbered tracks | R0485* |
| [ ] F0348 | Recording-credit relationships | R0127* |
| [ ] F0349 | Artist/work/URL relationships | R0127* |
| [ ] F0350 | Selected-release front-cover copies | R0574* |
| [ ] F0351 | Selected-release back-cover copies | R0574* |
| [ ] F0352 | Reviewed metadata application to one local file | R0202* |
| [ ] F0353 | SAMI cue import and optional class selection | R1416* |
| [ ] F0354 | SubViewer text-cue import | R1415* |
| [ ] F0355 | New-copy ASS generation | R0642* |
| [ ] F0356 | New-copy SSA generation | R0643* |
| [ ] F0357 | New-copy WebVTT conversion | R0992* |
| [ ] F0358 | Embedded text-stream extraction to SRT | R1420* |
| [ ] F0359 | Existing PGS extraction to SUP | R0837* |
| [ ] F0360 | New MKV with a language-tagged external text track | R1419* |
| [ ] F0361 | Single-ASS libass burn-in | R0990* |
| [ ] F0362 | Bilingual bottom/top text burn-in | R1000* |
| [ ] F0363 | Installed yt-dlp page inspection | R0858 |
| [ ] F0364 | Resolved format browsing and playback | R1211 |
| [ ] F0365 | Resolved audio-only playback | R0888 |
| [ ] F0366 | Explicit resolved live-source playback | R1133* |
| [ ] F0367 | Flat playlist-page browser and per-page resolver | R1132* |
| [ ] F0368 | Selected native-format video downloads | R1212 |
| [ ] F0369 | Selected native-format audio-only downloads | R1135* |
| [ ] F0370 | Authored WebVTT language selection/download | R1107* |
| [x] F0371 | Allowlisted ordered offline restoration chains | R0029 |
| [x] F0372 | Named recipes and portable recipe JSON | R0086* |
| [x] F0373 | Impulsive click-removal attempts | R0980* |
| [x] F0374 | Clipped-sample interpolation attempts | R0980* |
| [x] F0375 | Spectral audio denoising | R0980* |
| [x] F0376 | Noise gating of quiet passages | R0980* |
| [x] F0377 | Speech-oriented half-cycle normalization | R1085 |
| [x] F0378 | Existing discrete center-channel isolation | R1084* |
| [x] F0379 | Two-source crossfade file joining | R0152* |
| [x] F0380 | Measured two-pass EBU loudness normalization | R0500* |

## Categorized access

| Settings category | Section | Panel |
|---|---|---|
| Library | Remote libraries | Subsonic music library |
| Files | Cloud and network | WebDAV media files |
| Library | Online metadata | MusicBrainz metadata and artwork |
| Subtitles | Files and production | Subtitle files and production |
| Video | Online sources | Online sources with yt-dlp |
| Audio | Restoration and transitions | Audio restoration and transitions |

Each main entry activates its existing owned panel on subsequent clicks. The online-source resolver is also registered for the existing curated player context menu. Specialized remote-write, conversion and recipe commands stay inside their relevant grouped action menus. All six panels reuse the existing Fluent/Mica workbench, styled controls/checkboxes, expandable sections, virtualized tables, operation lock and separate cancel button. There is one Settings entry point; no catch-all Tools page was added. This reuse has not been visually reviewed at any DPI/theme yet.

## Subsonic library

Enter an HTTPS server root, username and password, then explicitly connect. HTTP is accepted only on loopback. Credentials remain in memory in this panel; closing/reconnecting disposes its client. Authentication uses the Subsonic salted-token protocol. Browsing calls are explicit: artists, artist albums, album tracks, track search, newest/recent/random albums, starred songs and playlists. Search/discovery views are bounded to 100 results; normal catalog views to 500. There is no background server indexing, provider-account discovery or automatic favorites synchronization.

Original and selected-bitrate playback request server media and use a **session-only loopback relay** so upstream authentication parameters do not enter playlist/history URLs. Bitrate choices are 64, 128, 192 and 320 kb/s with an MP3 transcoding request; server capabilities and permissions determine the result. Original requests ask for raw format. The relay forwards byte ranges with up to four active clients and at most 500 registered session sources. It rejects browser Origin requests and exposes no arbitrary upstream URL parameter.

**Keep the panel open for relayed playback.** Reconnecting or closing removes those links; restoring an old queue cannot revive them. HTTP-listener binding can fail because of Windows URL permissions or a port race. The app does not change URL ACLs or firewall rules. Relay negotiation, HEAD/range behavior and playback lifecycle remain unverified. This relay serves single media resources, not rewritten adaptive-stream manifests or a LAN/mobile player.

Download 1–50 selected tracks explicitly into a chosen existing folder. Each copy is bounded to 1 GiB and twenty minutes; completed files survive cancellation, existing destinations are refused. The in-panel downloaded-track map supports new-copy M3U8 export using local paths only. It is a session map, not a remote synchronization database. There is no provider-specific DRM, Roon, RAAT, multiroom or bidirectional mobile synchronization claim.

## WebDAV files

Connect to a user-provided HTTPS WebDAV root, optionally with session Basic credentials. Browsing uses depth-one PROPFIND and successful property rows. Recursive view is explicit and limited to depth three, 100 folders and 500 files. Returned sources and relative playlist references must stay inside the connected root. XML forbids DTDs and has byte/character limits.

Selected files can be streamed through the same authenticated relay, downloaded individually, or downloaded sequentially in a batch of 1–50. Downloads publish new files only and have per-file 1 GiB/twenty-minute limits. Duplicate names in a batch can stop later copies; completed earlier files remain. Read remote UTF-8 M3U/M3U8 files up to 1 MiB and 100 entries, then explicitly select an item to play/download.

Remote writes are distinct commands. **Upload new remote file** sends the chosen local file with `If-None-Match: *`; it never retries as an overwrite. Correct no-replace behavior depends on the server honoring HTTP preconditions. **Create remote collection** sends MKCOL for a validated new child name and expects creation success. Neither command performs delete, MOVE, server-side account changes or background synchronization. Local source stamps are checked after uploads; detecting a changed local source cannot automatically roll back a remote write already accepted by the server.

Resume uses profile-owned partial bytes and ETag metadata keyed to source and final destination. With a valid strong ETag, it requests a byte range with If-Range, checks the returned offset and declared lengths, then publishes the completed copy into a new user filename. If the server sends a full response, the owned partial file restarts. Cancellation retains owned partial state for the same source/destination. This is not a cryptographic integrity proof and cannot establish completion if a server supplies no trustworthy length. Weak/missing ETags disable append reuse.

The explicit cache action keeps versioned media under the current profile, with URI/user-derived keys, ETag and local size/time stamps. Valid local state enables conditional GET; HTTP 304 reuses the local file. New versions get new owned filenames. Cache files/partials remain on disk; there is no automatic purge or global quota. Refresh still requires a network request and does not silently fall back on a server error. Local completed media can be opened independently when offline.

The broad cloud/WebDAV original requirement remains partial: this is a WebDAV client, not OAuth integration for every storage provider, filesystem mounting, full mirroring or conflict-aware two-way sync.

## MusicBrainz and artwork

Search recording/release/artist text explicitly. The client uses a descriptive User-Agent and a shared request gate, with bounded JSON and request deadlines. Release browsing exposes disc/track positions; discographies retain editions/country duplicates and show the first 100 releases. Recording and artist relationships are displayed as rows, including URL text. Results are not automatically imported into the existing local people/cinema catalogs.

Selected releases can request front/back covers from Cover Art Archive. The client prefers a listed thumbnail, restricts the requested artwork origin to archive hosts, accepts bounded JPEG/PNG bytes up to 10 MiB and saves a new image file. It does not replace embedded artwork, decode/render arbitrary cover images in this panel or search several artwork providers.

For local tags, choose a recording/track, check the proposed fields, and select one local target. A separate review window reads current values, displays current/proposed rows and captures a source stamp. Only its **Apply reviewed changes** command invokes the existing batch-tag writer for that one file. Title/artist/album, available track number and release year are selective; unchecked fields are preserved. Loaded media is excluded by the existing writer. Unsupported tag formats can partially write selected fields before readback reports a failure; the UI reports those existing semantics.

Text lookup is not acoustic fingerprint recognition or automatic album identification. Manual review is not unattended auto tagging. One artwork provider is not a multi-provider search. These broader original requirements retain partial status.

## Subtitle files and production

SAMI import handles SYNC intervals, optional ASCII paragraph-class selection, HTML line breaks/entities and readable cue text. With no class selected it combines available paragraph text. SubViewer import accepts its timestamp-block syntax and `[br]` breaks, not binary VobSub/MicroDVD or every file using `.sub`. Text reading follows UTF-8/BOM-aware .NET behavior; arbitrary legacy code pages are not detected. Input/output limits are 16 MB and 100,000 cues, with bounded regular-expression execution and valid time ranges.

Conversion writes a new SRT, ASS, SSA or WebVTT file. Generated ASS/SSA uses a default Segoe UI style and centisecond events; VTT escapes cue text. Converting readable cues flattens source markup, positioning, karaoke timing and animation. Single-ASS burn-in is different: it copies the chosen ASS script to an owned fixed filename and delegates its own styles/animations to libass. Required fonts and FFmpeg filter support must already exist.

Container extraction maps an explicit zero-based subtitle stream 0–63. Text can become SRT; bitmap captions cannot be converted without OCR. SUP extraction copies an already-PGS stream; another codec fails rather than becoming PGS. The MKV workflow copies source video/audio, excludes prior subtitle/data tracks, adds the chosen external text as SRT and labels it with a three-letter language code.

Burn-in exports a new up-to-1080p H.264/AAC MP4 intended for SDR material. A second text file generates independently timed bottom/top ASS styles; it does not translate, align languages automatically or preserve both rich source scripts. Temporary script names are fixed inside newly owned directories, keeping selected paths out of filter expressions. Import/extraction/export scope does not certify original native playback-renderer requirements, which remain partial.

## Installed yt-dlp

No downloader is installed or probed at startup. Each explicit action locates an existing `yt-dlp.exe` in the application folder/PATH, ignores its external configuration and passes allowlisted arguments through `ProcessStartInfo.ArgumentList`. The user supplies an HTTPS page without URL credentials. The implementation does not read browser cookies, request credentials, execute user-supplied command text or bypass DRM.

Inspection returns bounded format metadata and authored WebVTT languages. Format views distinguish combined, audio-only and video-only results. Playback sends a selected temporary resolved URL to the current engine; special provider headers/cookies are not reconstructed. URLs expire and can appear in the ordinary queue/history. An audio-only action chooses a returned audio format. Live playback requires metadata identifying a live source and a selected returned format. Availability still depends on yt-dlp, provider protocols and the installed player environment.

Flat playlist browsing loads at most 100 page references into an owned list. Selecting one opens its resolver with the page prefilled; pages are not treated as already playable media. Native video/audio downloads request one selected format ID and fixed owned output filename, preserve the returned native container, and publish a new user file. Video-only selections remain silent; audio-only downloads do not extract/transcode video. Live recording is left to the separate finite stream recorder. Caption downloads accept selected authored WebVTT and check its signature; generated/translated captions remain out of scope.

Inspection/playlist jobs have a two-minute deadline; downloads thirty minutes. Tool stdout/stderr are bounded, active job disk usage is polled only while that job runs, and an observed 1 GiB/32-file guard stops oversized jobs. Polling is not an instantaneous hard quota. Ignored configuration/default native-download behavior is assumed; provider/tool-version compatibility, interrupted cleanup and process-tree termination need future testing.

## Restoration and transitions

Build an explicit 1–10-step allowlisted chain. Steps can be added, removed and reordered; no arbitrary filter expression or plugin code is loaded. Profile-local storage retains up to 32 named recipes, with explicit loading/saving and bounded versioned JSON import/new-copy export. A saved recipe does not activate during live playback.

The chain provides declick/declipping interpolation attempts, spectral noise reduction, noise gating, speech-oriented normalization, existing-center-channel selection and optional measured EBU loudness. Center selection requires a real FC channel and duplicates it into stereo; it does not isolate vocals from arbitrary stereo music. Repair filters do not guarantee recovery of missing detail. Spectral reduction can alter desirable sound. The UI exposes chain order and these limits without claiming AI separation.

Two-pass loudness must appear once, last. The first pass measures the processed chain, then a second pass supplies measured values for −16 LUFS, −1.5 dBTP and 11 LU targets. Silent/non-finite measurements reject the job. FFmpeg can revert to dynamic normalization when linear conditions are infeasible; target accuracy has not been measured. The separate crossfade action joins two normalized sources with a 0.1–30-second triangular overlap, independently of the restoration chain. Each source must be longer than the requested overlap. It does not add live gapless transitions.

Outputs are new stereo 48 kHz / 24-bit WAV or FLAC copies. WAV requests RF64-auto. Source stamps, a two-hour deadline, cancellation and existing new-copy publication are reused. There is no new audio device, native plugin host or work added to the live playback loop.

## Deferred acceptance and source references

Future acceptance work includes compilation; every HTTP/auth/server error path; relay start/stop/reconnect, ranges and binding failures; completed and interrupted downloads; WebDAV preconditions, ETags and resume integrity; cache corruption/retention; metadata query shapes/rate policy and tag readback; subtitle format fixtures/timing/fonts/muxing; provider URL/header/expiry handling; external-process cancellation; restoration measurements and output decoding; and Fluent/keyboard/DPI review. None has been run. Historical 92-group passing results describe the earlier checkpoint only.

Source services: `RemoteMediaIO`, `RemoteMediaRelay`, `SubsonicSource`, `WebDavSource`, `MusicBrainzSource`, `SubtitleProduction`, `OnlineMediaSource`, `AudioRestoration`. Entry points: `MainWindow.ConnectedMedia` and the categorized registry. No API or FFmpeg/yt-dlp process starts merely from constructing the main player; work begins from panel actions. No performance benchmark, leak audit or security certification is claimed.

Implementation documentation consulted: [Subsonic API](https://www.subsonic.org/pages/api.jsp), [MusicBrainz API](https://musicbrainz.org/doc/MusicBrainz_API), [Cover Art Archive API](https://musicbrainz.org/doc/Cover_Art_Archive/API), [yt-dlp options](https://github.com/yt-dlp/yt-dlp#usage-and-options) and [FFmpeg filters](https://ffmpeg.org/ffmpeg-filters.html). These references informed source wiring, not runtime validation.

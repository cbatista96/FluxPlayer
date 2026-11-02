# Local backend acceptance — October 31, 2026

158 previously unverified capability IDs now have individual acceptance groups. These checks use isolated profiles, synthetic media and loopback listeners. They do not access personal microphones, cameras, account libraries or remote devices. Publication and signing remain deferred by the user.

The latest continuation adds **500 loopback Kodi/MPD checks**: 184 request contracts for every Kodi JSON-RPC operation, 116 command contracts for every MPD command, 100 malformed Kodi response cases, and 100 invalid MPD input cases. These validate generated parameter examples, request fields, credential headers, command prefix/argument escaping, response correlation/parsing, rejection of RPC errors/incomplete or malformed IDs and non-object JSON, and input bounds/control-character rejection. They use synthetic responders or disconnected clients rather than Kodi/MPD servers, so they do not validate server-version support, permissions, command semantics, or resulting library/playback state; the corresponding catalog entries remain `implemented_unverified`.

The full Release regression passed **1,451 groups, zero failures** with portable MKVToolNix and mpv available. Reproduce the interop group independently with:

```powershell
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- --interop-contracts-only
```

This command runs 500 groups. Real Kodi/MPD backend acceptance is still required before changing feature-catalog status.

An additional focused suite adds **200 server-action input guards** across Subsonic, Jellyfin/Emby, Plex and UPnP: 80 generated valid inputs and 120 invalid/boundary values covering text bounds and controls, integer ranges, choices, ID/index lists and URL restrictions. It tests only `ServerActionCatalog.Validate` locally; it does not send requests to a server and does not change any feature status. Reproduce it with:

```powershell
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release -- --server-action-guards-only
```

A second focused suite executes **73 provider action calls** through actual loopback HTTP/XML/SOAP listeners: all Jellyfin and Emby personal actions, all Plex actions, all ten UPnP operations, plus unauthorized-response checks for Jellyfin, Emby and Plex. It verifies route/verb mapping, token-header scope, SOAPAction/body agreement and explicit HTTP error surfacing. These synthetic responders do not validate remote provider semantics, device state or compatibility. Reproduce with:

```powershell
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release -- --server-action-loopback-only
```

The managed regression passes **572 groups with zero failures**, including both new server-action suites. The full 1,451-group native regression was rerun after these additions. It also passed 30 actual Jellyfin-server acceptance checks (F0381–F0390 and F0611–F0630); those checks are distinct from the simulated-provider loopback contracts.

| IDs | Acceptance evidence | Limits |
| --- | --- | --- |
| F0261–F0271 | Real named pipe, TCP, HTTP, WebSocket and registered window messages; authenticated JSON-RPC commands, rejected tokens/origins, player snapshots, bounded journal/export and actual PowerShell CLI with a Unicode source path. | Loopback/current-user control only. A WebSocket subscription now acknowledges graceful client closure instead of leaving the client waiting. |
| F0278 | FFmpeg alignment outputs independently decoded to PCM; measured silence/tone onset for positive and negative offsets; source bytes preserved. | Manual offset, not automatic fingerprint alignment. |
| F0279–F0285 | Persisted IPTV/XMLTV/recording catalogs, actual VLC playback over loopback HTTP, finite stream copies, an explicitly armed recording scheduler and three independently playable buffer segments. | Synthetic finite MPEG-TS source, not a provider or tuner compatibility certificate. |
| F0353–F0358, F0360–F0362 | SAMI class selection, SubViewer precision, ASS/SSA/WebVTT round trips, actual embedded text extraction, language-tagged mux, independently decoded red ASS burn-in pixels and bilingual top/bottom caption pixels. | Existing PGS extraction F0359 remains unverified. OCR is separate. |
| F0441–F0450 | Actual Windows SQLite database persistence, projection/upsert/cancellation, combined predicates, literal wildcard escaping, stable paging, facets, duplicate candidates, saved Focus views, CSV copies, VACUUM and quick_check. | Synthetic 125-row catalogs; no large-library performance certification. |
| F0451–F0460 | Four owned native players, independently loaded queues, real per-player volume/mute/rate, transport, offset seeks, repeat behavior, saved profiles and sampled clock/drift reports. | Shared default physical endpoint with silent sources. Group calls are best effort; no sample-accurate multiroom synchronization or certification of every endpoint. |
| F0461–F0470 | Edition/tree editing, ordered intervals, segment links, multilingual displays, flags, XML copies and actual MKVToolNix write/extract round trips preserving original MKV bytes. | Authoring acceptance; broader playback bounds remain documented separately. |
| F0681–F0780 | All 100 catalog controls dispatched to an actual owned mpv process over Windows named-pipe IPC. Runtime properties are read back; startup controls launch a working process with synthetic video/audio/subtitle/chapter/edition tracks. Queue edits and external-track removals check resulting native state. HTTP media comes from an isolated loopback fixture. | The tested startup values and selected runtime values are covered. Backend, GPU, driver and alternate option support remain conditional. IPC acceptance is not calibrated HDR, exclusive hardware-output or image-quality certification. No synchronization with the main LibVLC player is implied. |

## Corrections found through execution

- VLC's default Windows backend shares process-level audio-session volume/mute. Audio zones now explicitly use the DirectSound output module, which keeps gain/mute on each player's buffer; snapshots report actual native volume/mute/rate. The two-player test failed before this change and passes afterward. See VideoLAN's [DirectSound volume implementation](https://github.com/videolan/vlc/blob/3.0.x/modules/audio_output/directsound.c) and [mmdevice session implementation](https://github.com/videolan/vlc/blob/3.0.x/modules/audio_output/mmdevice.c).
- mpv peak luminance now accepts integers. Fractional values were allowed by the UI catalog but rejected by the real backend.
- Subtitle conversions retain line breaks and decode text entities on import. WebVTT conversion previously turned `ñ` into the literal text `&#241;` on reopening. Timestamp parsing uses decimal arithmetic to retain exact millisecond boundaries such as `1.001` instead of truncating binary floating-point values.
- IPTV imports retain commas in channel titles and quoted attributes. XMLTV rejects invalid offset minutes instead of normalizing them into another hour.
- A normally completed television buffer exposes its final short segment. A segment potentially interrupted by cancellation/failure remains excluded.
- WebSocket event subscriptions now read and acknowledge client Close frames and observe/cancel the pending receive on disposal.
- Non-numeric Kodi response IDs and non-object JSON roots now produce a protocol `IOException` instead of leaking a `JsonElement` type exception. A canceled loopback media response aborts an incomplete HTTP response during relay shutdown instead of allowing an `InvalidOperationException` to escape cleanup.

## Reproduction

Build Release x64 before using `--no-build`. Native checks require the Windows runtime and an available audio output. FFmpeg/ffprobe are required for media fixtures. Put MKVToolNix on the test process's PATH or use the packaged portable tools. `FLUXPLAYER_TEST_MPV` selects a trusted local mpv.exe; the native mpv group is optional when that executable is absent.

```powershell
dotnet build FluxPlayer.sln -c Release -p:Platform=x64 --no-restore
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- --managed-only
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- --filter=native
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- '--filter=Advanced chapter acceptance'
dotnet run --project FluxPlayer.Tests/FluxPlayer.Tests.csproj -c Release --no-build -- '--filter=Television acceptance'
```

Each test invocation replaces `session-state/tests/results.json`; invocations must be sequential. The consolidated report deduplicates test names and retains the documented prior native/processing/100-capability results alongside current focused regressions.

## Portable test dependency provenance

The [mpv installation page](https://mpv.io/installation/) lists shinchiro's Windows builds. The isolated test used [20261021 / e471496c56](https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261021), reported version `v0.41.0-1227-ge471496c5`, with FFmpeg 63 and libplacebo 7.377. Downloaded archive SHA-256 matched the digest returned by GitHub's release API: `4a355f3e89c83fabc730d33fb4e759dfec747806ed2287b61a90d1681b83e8d3`. It resides only under ignored `session-state/tools/mpv-20261021`; mpv was not installed globally or added to the distributable. The optional mpv panel still uses a separately selected executable.

MKVToolNix 103.0 provenance and packaged license notices are recorded in [local progress](LOCAL_COMPLETION_PROGRESS.md). No real account credentials or signing certificate were supplied or needed for this checkpoint.

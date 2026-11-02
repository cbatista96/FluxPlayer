# One hundred expansion capabilities — acceptance tracked individually

These are IDs F0381–F0480. F0441–F0470 pass SQLite, zone-player and Matroska acceptance groups; see [backend scope](LOCAL_BACKEND_ACCEPTANCE.md). F0381–F0390 also pass against an actual isolated Jellyfin server; see [server scope](LOCAL_SESSION_ACCEPTANCE.md). Other unchecked entries still await acceptance. Several subfeatures share an original parent; this table does not claim 100 completed original requirements. Advanced Matroska production does not certify every advanced playback case.

Scope, access paths, limits and deferred acceptance: [Expansion guide](UNTESTED_EXPANSION_HUNDRED.md).

## Desktop and HTPC

| Verification pending | Capability | Original reference |
|---|---|---|
| [ ] F0471 | Opt-in XInput transport, seek and volume controls. | R1562 |
| [ ] F0472 | Playback-scoped Windows display/sleep requests and active-player screensaver handling. | R1570 — partial |
| [ ] F0473 | Explicit fullscreen placement on a selected monitor. | R1486 |
| [ ] F0474 | Session-activated monitor recall for audio and video modes. | R1065 — partial |
| [ ] F0475 | Named window-placement presets for desktop and HTPC layouts. | R1557 — partial |
| [ ] F0476 | Opt-in native track-change desktop notifications. | R0580 |
| [ ] F0477 | Explicit Windows sleep requests. | R0452 |
| [ ] F0478 | Explicit Windows hibernation requests. | R0452 |
| [ ] F0479 | Cancelable shutdown-after-current-item policy. | R0453 |
| [ ] F0480 | Opt-in removable-media arrival queueing. | R1465 — partial |

## Emby

| Verification pending | Capability | Original reference |
|---|---|---|
| [ ] F0391 | Emby session-token connection and explicit user identification. | R1275 — partial |
| [ ] F0392 | Emby library-root and selected-collection navigation. | R1275 — partial |
| [ ] F0393 | Bounded Emby movie browsing. | R1275 — partial |
| [ ] F0394 | Emby series, season and episode navigation. | R1275 — partial |
| [ ] F0395 | Emby music-album and audio-track navigation. | R1275 — partial |
| [ ] F0396 | Explicit text search of Emby library items. | R1275 — partial |
| [ ] F0397 | Read-only Emby resumable-item list. | R1275 — partial |
| [ ] F0398 | Read-only Emby favorite-item browsing. | R1275 — partial |
| [ ] F0399 | Original Emby media playback through a session relay. | R1275 — partial |
| [ ] F0400 | Selected Emby original-media downloads to new local copies. | R1275 — partial |

## Independent zones

| Verification pending | Capability | Original reference |
|---|---|---|
| [x] F0451 | Up to four independently owned local audio playback zones. | R0039 |
| [x] F0452 | Independent output-device assignment for each zone. | R0040 |
| [x] F0453 | Independent local audio queues per zone. | R0039 |
| [x] F0454 | Independent zone volume and mute. | R0170 — partial |
| [x] F0455 | Independent zone play, pause and stop controls. | R0039 |
| [x] F0456 | Best-effort grouped zone start and transport commands. | R0039 |
| [x] F0457 | Grouped timeline seeking with per-zone offsets. | R0039 |
| [x] F0458 | Independent zone speed and repeat-queue behavior. | R0039 |
| [x] F0459 | Saved zone configurations and ordered queues. | R0039 |
| [x] F0460 | Explicit zone position and relative-drift snapshots. | R0039 |

## Jellyfin

| Verification pending | Capability | Original reference |
|---|---|---|
| [x] F0381 | Jellyfin session-token connection and explicit user identification. | R1275 — partial |
| [x] F0382 | Jellyfin library-root and selected-collection navigation. | R1275 — partial |
| [x] F0383 | Bounded Jellyfin movie browsing. | R1275 — partial |
| [x] F0384 | Jellyfin series, season and episode navigation. | R1275 — partial |
| [x] F0385 | Jellyfin music-album and audio-track navigation. | R1275 — partial |
| [x] F0386 | Explicit text search of Jellyfin library items. | R1275 — partial |
| [x] F0387 | Read-only Jellyfin resumable-item list. | R1275 — partial |
| [x] F0388 | Read-only Jellyfin favorite-item browsing. | R1275 — partial |
| [x] F0389 | Original Jellyfin media playback through a session relay. | R1275 — partial |
| [x] F0390 | Selected Jellyfin original-media downloads to new local copies. | R1275 — partial |

## Last.fm

| Verification pending | Capability | Original reference |
|---|---|---|
| [ ] F0431 | Last.fm desktop authorization-token request and approval URL. | R0411 |
| [ ] F0432 | Exchange of an approved Last.fm token for a session. | R0411 |
| [ ] F0433 | Explicit Last.fm playing-now metadata submission. | R0411 |
| [ ] F0434 | Reviewed historical single-track Last.fm scrobbling. | R0411 |
| [ ] F0435 | Reviewed retained-history import into a Last.fm outbox. | R0411 |
| [ ] F0436 | Read-only recent Last.fm track browsing. | R0411 |
| [ ] F0437 | Read-only Last.fm loved-track browsing. | R0411 |
| [ ] F0438 | Last.fm similar-artist browsing. | R0411 |
| [ ] F0439 | Last.fm similar-track browsing. | R0411 |
| [ ] F0440 | Explicit Last.fm track love and unlove actions. | R0411 |

## ListenBrainz

| Verification pending | Capability | Original reference |
|---|---|---|
| [ ] F0421 | Session-only ListenBrainz token validation and owner binding. | R0413 |
| [ ] F0422 | Explicit ListenBrainz playing-now submissions. | R0413 |
| [ ] F0423 | Reviewed manual historical-listen submissions to ListenBrainz. | R0413 |
| [ ] F0424 | Reviewed eligible local-history staging for ListenBrainz. | R0413 |
| [ ] F0425 | Persistent account-bound listening outbox with uncertain-send handling. | R0413 |
| [ ] F0426 | Read-only recent ListenBrainz listen browsing. | R0413 |
| [ ] F0427 | Read-only ListenBrainz playing-now browsing. | R0413 |
| [ ] F0428 | ListenBrainz top-artist statistics browsing. | R0413 |
| [ ] F0429 | ListenBrainz top-recording statistics browsing. | R0413 |
| [ ] F0430 | Explicit ListenBrainz love/dislike/clear recording feedback. | R0413 |

## Matroska chapter production

| Verification pending | Capability | Original reference |
|---|---|---|
| [x] F0461 | Alternate Matroska chapter-edition construction. | R0438 — verified parent unchanged |
| [x] F0462 | Nested Matroska chapter-tree editing. | R0438 — verified parent unchanged |
| [x] F0463 | Explicit ordered-chapter source intervals. | R0438 — verified parent unchanged |
| [x] F0464 | Linked chapter segment and edition references. | R0438 — verified parent unchanged |
| [x] F0465 | Multiple language-specific Matroska chapter displays. | R0438 — verified parent unchanged |
| [x] F0466 | Matroska chapter hidden/enabled flags. | R0438 — verified parent unchanged |
| [x] F0467 | Default, hidden and ordered edition flags. | R0438 — verified parent unchanged |
| [x] F0468 | Advanced chapter XML import and new-copy export. | R0438 — verified parent unchanged |
| [x] F0469 | Writing advanced chapters into a new Matroska copy. | R0438 — verified parent unchanged |
| [x] F0470 | Existing Matroska chapter extraction through installed MKVToolNix. | R0438 — verified parent unchanged |

## Plex

| Verification pending | Capability | Original reference |
|---|---|---|
| [ ] F0401 | Explicit Plex server identity and authenticated-library connection. | R0538 |
| [ ] F0402 | Plex library-section browsing. | R0538 |
| [ ] F0403 | Hierarchical Plex artist, album, show and season navigation. | R0538 |
| [ ] F0404 | Explicit Plex library text search. | R0538 |
| [ ] F0405 | Read-only Plex on-deck item browsing. | R0538 |
| [ ] F0406 | Read-only Plex recently-added browsing. | R0538 |
| [ ] F0407 | Plex server-playlist browsing. | R0538 |
| [ ] F0408 | Ordered Plex playlist-track browsing. | R0538 |
| [ ] F0409 | Direct Plex media-part playback through a session relay. | R0538 |
| [ ] F0410 | Selected Plex original-part downloads. | R0538 |

## SQL catalogs and Focus

| Verification pending | Capability | Original reference |
|---|---|---|
| [x] F0441 | Named profile-local SQLite derived media catalogs. | R0068 — partial |
| [x] F0442 | Transactional stamp-aware library projection into SQLite. | R0043 — partial |
| [x] F0443 | Bounded compound all/any Focus queries over the SQL index. | R0138 — partial |
| [x] F0444 | Indexed-catalog facet counts by selected metadata fields. | R0043 — partial |
| [x] F0445 | Deterministic metadata/numeric sorting for SQL result pages. | R0043 — partial |
| [x] F0446 | Case-folded artist/title duplicate-candidate queries. | R0043 — partial |
| [x] F0447 | Named saved compound Focus views per derived catalog. | R0138 — partial |
| [x] F0448 | New-copy CSV export of reviewed SQL result selections. | R0043 — partial |
| [x] F0449 | Explicit checkpoint and compaction of the derived SQLite database. | R0043 — partial |
| [x] F0450 | On-demand SQLite integrity reporting. | R0043 — partial |

## UPnP

| Verification pending | Capability | Original reference |
|---|---|---|
| [ ] F0411 | Opt-in bounded SSDP discovery of local media devices. | R0059 — partial |
| [ ] F0412 | UPnP device and service-description inspection. | R0059 — partial |
| [ ] F0413 | Paged UPnP ContentDirectory child browsing. | R0059 — partial |
| [ ] F0414 | Explicit UPnP ContentDirectory search criteria. | R0059 — partial |
| [ ] F0415 | Local playback of a selected UPnP media resource. | R0059 — partial |
| [ ] F0416 | Loading selected server media onto a UPnP renderer. | R0059 — partial |
| [ ] F0417 | Remote UPnP renderer play, pause and stop controls. | R0059 — partial |
| [ ] F0418 | Absolute timeline seeking on a selected UPnP renderer. | R0059 — partial |
| [ ] F0419 | Remote renderer master-volume and mute controls. | R0059 — partial |
| [ ] F0420 | Explicit renderer position and transport-state snapshots. | R0059 — partial |

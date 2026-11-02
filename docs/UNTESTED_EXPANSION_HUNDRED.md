# New hundred-capability expansion — implementation awaiting verification

**Current acceptance update:** F0441–F0470 now pass individual Windows SQLite, native audio-zone and real MKVToolNix authoring checks. See [tested scope and remaining limits](LOCAL_BACKEND_ACCEPTANCE.md). The source-only statements below describe the earlier checkpoint; they do not override the current public feature catalog.

**New source work: F0381–F0480. None of these 100 additions was compiled, launched, exercised, visually reviewed, benchmarked or tested in this turn.** No application server connection, SSDP discovery, relay, scrobble/feedback submission, SQLite operation, zone player, MKVToolNix process, controller probe, media-arrival hook or power request was run during development. Documentation was consulted; that is not runtime verification. The existing executable predates this expansion.

This is separate from the **earlier** [hundred-capability acceptance checkpoint](VERIFIED_HUNDRED.md), covering F0201–F0260, F0291–F0320 and F0371–F0380. Its report/results are preserved unchanged and do not validate new code. The other 80 previously unverified capabilities also retain their status.

Current public catalog: **279 previously implemented · 180 implemented without testing · 21 pending · 480 total**. Original inventory: **573 previously verified · 45 implemented without testing · 1,072 pending or partial · 1,690 total**. These inventories count different things. Independently usable provider and workflow subfeatures can share an original parent; this is not 100 completed original requirements. Verified parent evidence is preserved, and broad requirements remain partial where this code implements a narrower scope.

The [hundred-item checklist](EXPANSION_HUNDRED_CHECKLIST.md) enumerates every capability and original reference. The public [feature checklist](../FEATURES.md) contains individual implementation references and separates untested work. All new verification boxes remain unchecked.

## Categorized access

| Settings category | Section | Panel |
|---|---|---|
| Library | Personal media servers | Jellyfin library |
| Library | Personal media servers | Emby library |
| Library | Personal media servers | Plex library |
| Playback | Network devices | UPnP media and renderers |
| Audio | Listening services | ListenBrainz listening |
| Audio | Listening services | Last.fm listening |
| Library | Large catalogs and Focus | SQL catalogs and Focus |
| Audio | Independent zones | Independent audio zones |
| Video | Chapters and editions | Advanced Matroska chapters |
| General | Desktop and HTPC | Desktop and HTPC integration |

Each entry activates its existing owned window on repeat clicks. Audio zones and Desktop/HTPC also use the existing curated context-menu registry. Specialized commands stay in grouped panel menus. New windows reuse the Fluent/Mica workbench, themed fields/checkboxes, expandable sections, virtualized tables/trees and explicit cancellation model. No second Settings button or Tools catch-all was added. These panels have not undergone visual, keyboard, screen-reader or DPI review.

## Jellyfin, Emby and Plex — thirty capabilities

The three clients share a bounded transport/workbench implementation with provider-specific routes, response parsing and authorization. These are client integrations, not three new playback engines or server installations. Enter an existing server token and explicit HTTPS API root; HTTP is accepted only on loopback, following the existing remote-client policy. For Emby, include the server's API path when necessary. Tokens remain in session headers and are cleared on disposal, never placed in saved queue URLs.

Jellyfin uses a MediaBrowser authorization header, user-aware Items/UserViews/Resume routes and explicit current/selected-user lookup. Emby uses its token header and user-scoped item routes. Each exposes bounded collection hierarchy, movie view, series/seasons/episodes, music albums/tracks, text search, read-only resumable items and server favorites. Views return at most 200 records per operation (search/resume up to 100); full result pagination, watched-state reporting and bidirectional favorite synchronization are not implemented.

Plex uses its session token header and XML responses for library sections, children, search, on-deck items, recently added items and server playlists/tracks. It selects the first exposed media Part. Provider/cloud-account discovery, OAuth/PIN acquisition, media-provider feature negotiation, multi-part stitching and adaptive transcoding sessions remain out of scope.

**Resume lists are catalog views.** These clients do not transfer provider resume positions into playback or report local watching progress to the server. Queue labels and original media are the implemented scope. The existing player handles the selected resource through a session-only loopback relay, carrying upstream credentials outside the queue. Keep the panel open for relayed playback; closing or reconnecting ends those URLs. HTTP-listener binding, ranges, permissions, codec availability and server-version compatibility are unverified. The application does not change URL ACLs, firewall rules or server security settings.

Original-media downloads are explicit new copies, with a 2 GiB/30-minute guard, response-length checking and no-overwrite publication. Missing exposed originals, unsupported server permissions, DRM and special transcode/header requirements fail rather than being bypassed. No requests were executed to confirm any provider version. Broad compatible-server requirements remain partial; the selected Plex client scope is source-implemented but unverified.

## UPnP media/control — ten capabilities

Discovery performs an explicit four-second IPv4 SSDP M-SEARCH, bounds response size and returns at most fifty description addresses whose literal host matches the response sender. Hostname/IPv6 descriptions can instead be entered manually. There is no background scan, device installation or discovery while the main player starts.

Description inspection reads bounded XML with DTDs prohibited and uses same-origin control endpoints. The panel separately selects a ContentDirectory server and an AVTransport/RenderingControl renderer. Server browsing supports ObjectID, page offset and 100 requested children; search passes bounded UPnP search criteria through escaped SOAP elements. Grammar and search capability depend on the device.

Selected DIDL resources can play locally or be loaded explicitly onto the selected renderer with metadata. Renderer controls bundle play/pause/stop, absolute relative-time seek, volume/mute and explicit position/transport snapshots. There is no permanent state poll, synchronized multiroom clock, codec negotiation matrix, local-file HTTP media server, multicast media broadcast or DLNA certification. A renderer must be able to reach and decode the provided resource independently. These additions do not complete local-file Chromecast/DLNA casting or a media-server-hosting requirement.

## ListenBrainz and Last.fm — twenty capabilities

All network writes require explicit actions in their panels. There is no automatic scrobbler or background retry service. Tokens/secrets/session keys remain in memory; local outbox rows contain owner, track metadata, times and submission state, not credentials.

ListenBrainz implements token validation/owner identification, playing-now submission, reviewed historical listens, eligible retained-history staging, account-bound persistent outbox, recent listens, current playing-now, top artists, top recordings and recording-MBID feedback. Feedback commands submit love/dislike/clear values explicitly. Read views are bounded to 100 records, not full-history scraping.

Last.fm requires the user's own application API key/secret. It obtains a desktop authorization token, copies its approval URL, then explicitly exchanges an approved token for a session. Other capabilities are playing-now, reviewed single-track scrobbles, retained-history staging, recent/loved tracks, similar artists/tracks and love/unlove. Application credentials are not embedded in source. Libre.fm compatibility is not claimed by implementing Last.fm's API.

Historical submissions need artist/title, a past start, duration above thirty seconds and conservative whole-second listened-time eligibility: half the declared track or four minutes. The history importer stages at most 100 retained audio sessions; Last.fm candidates are limited to fourteen days. Manual values are user-declared, not measured playback telemetry. Existing history remains untouched.

The local outbox holds at most 500 identities, deduplicated by provider/owner/artist/title/start. It binds submissions to the authorized account. A selected batch has at most 25 Pending rows, sent sequentially with spacing. Before a network request, the row is durably marked **Uncertain**; confirmed success becomes Submitted. Exceptions/cancellation leave Uncertain and do not automatically retry. This deliberately avoids treating unknown server outcomes as safe to resend. It is not a distributed idempotency guarantee. Removing a local submitted row also removes its local deduplication history; no remote listen is deleted. The UI reports ignored Last.fm submissions as unsuccessful.

API shapes, signatures, limits, timestamps, duplicate behavior, uncertain outcomes and credential/session disposal still require acceptance tests. No metadata was sent to either provider during development.

## Derived SQL catalogs and Focus — ten capabilities

`SqlMediaIndex` uses the Windows `winsqlite3.dll` ABI and requires SQLite 3.24 or later. It is opened only by explicit index actions; the main player's JSON library remains authoritative. No additional database package/runtime was installed. Missing exports or an older OS SQLite engine produce an error, not an automatic DLL replacement.

Create up to 32 named derived catalogs. Projection accepts up to 200,000 current library entries per operation, reuses a prepared UPSERT inside a transaction, skips unchanged indexed metadata/stamps and honors cancellation. Aborted projection rolls back. Stale records are retained, so projection is not a destructive mirror or file-deletion operation. There is no automatic watcher/metadata scan attached to this index.

Focus supports up to twenty allowlisted rules, combined with all/any semantics. Values are parameter-bound; column/operator/sort names come from fixed allowlists. Contains text escapes LIKE wildcards. Numeric fields remain numeric. Queries return 100-row pages with deterministic path ties and bounded offsets, plus explicit facets, artist/title duplicate candidates, saved views and selected CSV copies. Candidate duplicates are metadata groups, not hashes or acoustic equivalence. CSV export quotes fields and neutralizes formula-like starts.

Checkpoint/compaction and quick-check reporting are explicit derived-database actions. They do not remove media. The SQLite ABI, transaction semantics, corrupt/locked databases, cancellation, saved-view limits, query plans and large-library latency have not been exercised or benchmarked. Adding a derived SQL index does not establish measured support for 100,000-plus files in every main-player workflow; the broader large-library requirement remains partial.

## Independent local audio zones — ten capabilities

Up to four zones create separate MediaService/LibVLC instances on demand, with dummy video output and local-audio-only queues. They are independent players, not streams in the main queue. Each has output selection, gain/mute, transport, queue, speed/repeat and profile settings. Device enumeration occurs from its explicit command.

Queues hold up to 500 available absolute local audio sources. End events advance only that zone. Missing next sources report an error. Group actions prevalidate source availability and dispatch sequential start/play/pause/stop/seek calls. A ±5,000 ms per-zone timeline offset is attempted at startup/seek, with no negative position. Initial seeking is attempted on the Playing event; device/format-specific readiness remains unverified.

Group start is **best-effort software coordination**, not sample-accurate, hardware-clocked or cross-machine synchronization. Independent rates/devices can drift. A snapshot samples player times sequentially relative to the first playing zone and is not calibrated physical-output latency. There is no idle timer for position monitoring. Profiles/ordered queues can be saved and explicitly restored without auto-play. Closing the panel stops/releases the zone engines.

No RAAT, ASIO/DSD guarantee, network-zone protocol, exclusive multi-device route or acoustic delay calibration is introduced. Multi-device playback, startup seek, event races, profile corruption and shutdown/resource disposal are deferred checks. No zone engine or device probe ran during development.

## Advanced Matroska chapter production — ten capabilities

The advanced project supports up to 32 editions, bounded nested chapter atoms, explicit source intervals, linked SegmentUID/EditionUID references, multiple language displays, chapter hidden/enabled flags and edition default/hidden/ordered flags. Tree editing offers sibling order and local removal. Generated UIDs are nonzero; present UID values are checked for duplicates within their type. Start/end editing has millisecond input and nanosecond XML formatting.

Import/export uses DTD-prohibited XML, a 4 MiB bound, depth limits and up to 2,000 atoms. Untouched elements are retained. Language-display edits preserve other displays. Ordered editions require chapter end times, and only one edition may be flagged default. These are structural project checks, not full Matroska XSD, every ChapterProcess feature or playback verification.

Installed `mkvextract` can extract existing chapter XML. Installed `mkvpropedit` is invoked only on a newly copied MKV, with an owned chapter document. Original source stamps are checked and output publication refuses replacement. The app does not install/probe MKVToolNix at startup. The copy is cancellable; warnings/tool exit status and generated-container compatibility need future acceptance.

The previously verified flat chapter-editor parent R0438 remains verified **at its original scope**. These ten advanced additions are separately untested. Linked/ordered-edition playback and full compatibility in public F0104 remain pending; writing metadata does not prove the current engine can play every edition structure.

## Windows desktop/HTPC — ten capabilities

- **XInput controls:** explicit enable, controller 0–3, edge-triggered A/X/LB/RB/D-pad transport/seek/volume, with polling only while enabled and focus gating to the main player/panel. This is a fixed XInput mapping, not every remote/gamepad protocol.
- **Awake policy:** requested display/system activity while playing, released on pause/disable/close. Owner-window screensaver/monitor-power requests are handled while active; this does not rewrite global screen-saver policy or override explicit user sleep.
- **Chosen fullscreen monitor:** explicit available-monitor placement followed by the existing video fullscreen path. No resolution/refresh-rate change or HDR negotiation.
- **Per-mode monitor recall:** selected audio/video targets on mode changes, activated only for this open panel session. No automatic monitor-capability detection.
- **Window placements:** up to sixteen named finite geometry/topmost presets with work-area clamping. Full playback/theme/output presets are out of scope; mixed-DPI and disconnected-monitor behavior need review.
- **Native track notifications:** opt-in title/artist balloons through the existing player tray icon, respecting Windows notification behavior. The tray icon must already be enabled in General settings.
- **Sleep:** explicit confirmed OS sleep request, without forcing suspension.
- **Hibernate:** explicit confirmed OS hibernation request; OS policy/hardware must support it. No power configuration changes.
- **Shutdown after current item:** explicit confirmed arm, natural main-player end and a software 60-second countdown with cancel. Switching source while still armed cancels it; closing the panel cancels before launch. Only after the countdown does the app request `shutdown /s /t 0`, with no force-close flag. Once Windows receives it, the panel cannot guarantee cancellation; Windows may block on policy/unsaved applications. This is independent of an offline-job completion scheduler.
- **Removable-media arrival:** opt-in ready-drive snapshots after device signals, bounded top-level media enumeration and queue-only behavior. No device-message pointer is dereferenced, no program is autorun, no recursive collection is scanned and no disc menu is decoded. Empty/non-media discs and delayed mount behavior remain conditional.

All policies default off. Closing the panel detaches its hook/handlers, stops its timers and cancels its internal countdown/scans. No controller probe, native power call, drive-arrival experiment, notification preview, display move or shutdown command was executed during development.

## Deferred acceptance and references

Compile first when testing is authorized, then cover all provider/auth/protocol/error cases, response shapes, source/credential lifecycle, relay binding/ranges, outbox uncertain outcomes, SQL ABI/plans/transactions/cancellation, measured multi-output audio, native event shutdown, Matroska fixtures/tool versions, power-action cancellation, insertion events, focus/mixed-DPI and Fluent/accessibility review. No existing passing test result can be applied to this expansion.

Implementation references consulted: [Jellyfin Items controller](https://github.com/jellyfin/jellyfin/blob/master/Jellyfin.Api/Controllers/ItemsController.cs), [Jellyfin user views](https://github.com/jellyfin/jellyfin/blob/master/Jellyfin.Api/Controllers/UserViewsController.cs), [Emby REST API](https://dev.emby.media/doc/restapi/index.html), [Plex Media Server API](https://developer.plex.tv/pms/), [UPnP ContentDirectory](https://openconnectivity.org/wp-content/uploads/2015/11/UPnP-av-ContentDirectory-v4-Service-20101231.pdf), [ListenBrainz API](https://listenbrainz.readthedocs.io/en/latest/users/api/index.html), [Last.fm API](https://www.last.fm/api/intro), [SQLite C API](https://sqlite.org/c3ref/sqlite3.html), [Matroska chapters](https://www.matroska.org/technical/chapters.html), [mkvpropedit](https://mkvtoolnix.download/doc/mkvpropedit.html), [Windows execution-state requests](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate), [XInput](https://learn.microsoft.com/en-us/windows/win32/api/xinput/nf-xinput-xinputgetstate) and [Windows shutdown](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/shutdown).

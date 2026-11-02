# Remote libraries and device actions — one hundred untested capabilities

**Current acceptance update:** 26 Subsonic and 20 Jellyfin actions pass with real isolated Navidrome/Jellyfin servers and positive local media/state fixtures. The remaining 54 actions retain independent acceptance status. [Evidence and limits](LOCAL_SESSION_ACCEPTANCE.md). Statements below about source-only work describe the earlier checkpoint.

**F0581–F0680 have source implementation only and have not been compiled or tested.** The batch adds 30 Subsonic operations, 20 Jellyfin operations, 20 Emby operations, 20 Plex operations and ten UPnP operations. No application launch, authenticated provider request, device inspection/control, remote state change, benchmark or visual acceptance was performed. No executable was rebuilt and no GitHub publication was made.

[Individual checklist](NETWORK_HUNDRED_CHECKLIST.md) · [Operation/source manifest](network-hundred-manifest.json) · [Complete feature inventory](../FEATURES.md)

These are explicit, independently selectable subfeatures of the broad original integration/discovery/library requirements. Jellyfin and Emby intentionally share descriptors and UI while their adapters use different routes and payloads. Each integration remains independently unverified. The batch does not mean that 100 broad original requirements are complete. Automatic cross-device synchronization, account/cloud discovery and protocol certification remain outside this scope.

## Access and layout

| Settings category | Section | New panel | IDs |
|---|---|---|---|
| Library | Remote libraries | Subsonic discovery and library actions | F0581–F0610 |
| Library | Personal media servers | Jellyfin library actions | F0611–F0630 |
| Library | Personal media servers | Emby library actions | F0631–F0650 |
| Library | Personal media servers | Plex library actions | F0651–F0670 |
| Playback | Network devices | UPnP capabilities and queue settings | F0671–F0680 |

There are five categorized entry points, rather than 100 new menu items. The existing single Settings button and curated context menu are retained. Within each panel, choose a purpose such as Browse, Discovery, Metadata, Playlists, Favorites/state, Bookmarks or Renderer inspection/control. Only relevant parameter fields are created for the selected action. Read and change buttons are enabled according to the action type. Tables are virtualized; selected details have a separate readable pane. Session/selection commands use grouped actions and the existing Fluent/Mica workbench, cancellation and monitor fitting. Actual visual/DPI/theme behavior remains unreviewed.

The new panels extend existing provider playback/download windows. They do not add a second main playback engine or duplicate stream relays. Use the earlier library windows for original playback/download workflows; use these panels for account/device data operations.

## Session workflow

1. Enter the server root and session credentials, or a UPnP device description URL.
2. Choose **Connect / inspect**. Nothing connects automatically when opening the panel.
3. Choose a category and action, fill its labeled parameters, then choose **Read server data** for a view or **Apply remote change** for a write.
4. Use selected IDs/occurrences or copy them for a subsequent action. Playlist creation/append accepts ordered newline-separated IDs. The selection helper follows displayed table order.
5. Inspect the returned state and optionally export the displayed bounded report as a new JSON file.

Provider roots require HTTPS, with HTTP allowed only on loopback, following existing project policy. The URL must not contain embedded credentials, query parameters or fragments. UPnP descriptions/resources use HTTP(S) because that protocol commonly exposes device endpoints over the LAN. No SSDP discovery runs in the new panel; use the existing explicit discovery window or enter a description URL.

Subsonic uses a username/password session with salted token requests. Jellyfin uses the existing MediaBrowser authorization header; Emby uses X-Emby-Token; Plex uses X-Plex-Token. Secrets are not persisted in a profile, playlist or report. The password/token entry box is cleared after connection; the client retains the credential material necessary for the session until disconnect/close. Closing cancels pending work and disposes the owned client. Changing the editable server field does not redirect an already connected session: the active target is displayed separately and a new Connect action is required.

Jellyfin/Emby optionally identify a selected user; an empty user ID requests the token owner. Plex identity can initialize a session without proving all subsequent operation permissions. Server support and permissions are checked per request; “session ready” is not acceptance evidence or proof that every listed feature is supported.

## Subsonic (30)

Browse genres and server song/album counts, genre song pages, random songs, artist top songs, related tracks, artist biographies, album notes and selected song metadata. Search albums/artists separately from the existing song search. Discover highest-rated, frequently played, year-range and genre albums. Browse logical music-folder roots. Top/similar results depend on the server's own data and optional upstream integrations; FluxPlayer does not infer them or scrape a provider.

Explicit account changes star/unstar a song, set/clear its 0–5 rating, report playing-now, or submit one reviewed listen. A historical listen requires an actual Unix-millisecond start within the past fourteen days and a separate reviewed write. This is not an automatic scrobbler or proof that a provider forwarded a listen to Last.fm/ListenBrainz. Only declare a listen that actually occurred. No automatic retry is attempted after a canceled or failed response.

Playlist actions inspect song occurrences with zero-based indices, create a playlist, append ordered songs, rename it, change its public/private flag and remove selected occurrences. They do not delete the underlying media. Creation/append/removal allow 1–50 values per action. Duplicate song IDs are allowed intentionally; duplicate removal positions, including equivalent zero-padded numbers, are rejected.

Before positional removal, read that playlist's occurrences in this session. The client retains up to sixteen short-lived order signatures. Removal requires a review within five minutes, accepts only the displayed first 100 positions, and rereads/compares the ordered server entry IDs immediately before writing. Reviews cover playlists up to 10,000 entries; larger positional removal is refused in this panel. A review is consumed before an attempted change, including a rejected preflight. This is a best-effort stale-order check: Subsonic does not provide an atomic compare-and-update transaction here, so concurrent edits after the check remain a limitation. Read/review again if another client is editing the playlist.

Bookmark actions read saved positions, create/update a selected song/audiobook bookmark and remove one bookmark. Positions are explicit milliseconds within 24 hours. These actions do not automatically copy FluxPlayer audiobook progress to a device. Server-provided authored lyrics can be read by artist/title; there is no transcription or automatic lyric attachment.

## Jellyfin and Emby (20 each)

Each provider has genre, music-artist, album-artist, release-year, studio and tag facets; randomized media discovery; latest additions; next-up television episodes; seed-based similar items; local special features/trailers; and selected-user metadata inspection. Results are explicit bounded views, not continuous recommendations or automatic playback queues.

Per-user changes set/clear favorites, mark played/unplayed, set boolean Likes or clear personal ratings. Likes is not a numeric five-star scale. These changes affect the selected server user only and do not update FluxPlayer's local favorites/history/rating index. Played-state operations are deliberate account edits, not actual playback reporting.

Playlist creation accepts a name, 1–50 ordered item IDs and Audio/Video type. Jellyfin uses a JSON creation payload, requires UUID item IDs and requests a private playlist. Emby uses its documented query interface; visibility/ownership follow that server's authenticated-account behavior and policies. Existing playlists are not replaced. User permissions, exposed server version and media compatibility may reject a request.

Jellyfin uses current UserFavoriteItems/UserPlayedItems/UserItems rating routes with a selected-user query and current Items metadata endpoints. Emby uses user-scoped legacy endpoints. Reads share semantic definitions, not an assumption that both APIs have identical URLs. No login automation, saved credentials or administrator library refresh/deletion is added.

## Plex (20)

Inspect selected metadata, recursive leaf items, section collections and collection members. Read genre/country/decade/director/cast/label facets, an unwatched view and similar items. These routes target a conventional local Plex Media Server library; cloud/custom providers and arbitrary metadata key schemes are not discovered. Metadata, section and playlist occurrence identifiers are numeric in this panel.

Changes mark played/unplayed, set a 0–10 personal rating, rename a playlist, remove a playlist occurrence, move an occurrence after another, or create a playlist from one initial media item. Played/unplayed/rating use the documented PUT endpoints. Playlist creation resolves the connected server identity and builds a local `server://` library URI. It does not create a public shared playlist or import an arbitrary cloud URI.

Load **Plex playlist occurrence inspection** before selecting an entry for removal/reordering. `playlistItemID` is displayed separately from the media `ratingKey`: two occurrences of one track must not be confused. For Move, the After field is another occurrence ID; zero means first. The operations use stable server occurrence IDs rather than local row numbers. There is no rollback or atomic multi-edit transaction. Reread the playlist after concurrent changes or an uncertain response.

## UPnP (10)

ContentDirectory inspection adds BrowseMetadata for one object, SearchCaps, SortCaps and SystemUpdateID snapshots. Renderer inspection adds GetMediaInfo, GetDeviceCapabilities, GetTransportSettings and GetCurrentTransportActions for instance zero. No event subscription or idle polling is added.

Explicit renderer changes request a standardized NORMAL/REPEAT_ONE/REPEAT_ALL/RANDOM/SHUFFLE play mode or stage an HTTP(S) resource using SetNextAVTransportURI with empty metadata. Support is renderer-dependent and can depend on current transport state. Staging does not guarantee gapless transition, clock synchronization or acceptance of an authenticated resource. The panel does not host files or turn a local path into a network media URL.

SOAP fields are encoded as XML elements through the existing bounded, DTD-prohibited client. A SOAP Fault is not treated as success. Displayed absolute URI properties omit user info, query and fragment; raw current/next URI metadata is summarized by size to avoid copying embedded resource credentials into a report. Metadata fields are descriptive and are not executed or opened as links.

## Bounds, writes and failure handling

Each view displays at most 100 rows. Some APIs accept offsets or paging bounds; others return a larger server response and are capped locally. Reports are therefore partial views, not full exports or exhaustive server-library synchronization. Existing HTTP helpers bound response sizes (normally 8 MiB JSON/XML reads, 4 MiB UPnP, 2 MiB new mutation responses) and request deadlines. Exports are bounded new-copy JSON files, containing provider/action/scope/time and displayed rows, without connection credentials. Exporting a previous report preserves that report's provenance even after parameter edits.

Remote writes require an explicit action with a review of target and parameters. A confirmed HTTP/protocol success is reported as request acceptance, not a subsequent independent state verification. Canceled/disconnected/timed-out writes may have applied at the server even if no result returned. The UI states this uncertainty and does not automatically retry, replay a queue or claim a rollback. Read server state before deliberately retrying a write. Making a playlist public is an explicit visibility change, not a local setting.

There are no startup network requests, extra listeners, timers, provider polling, new package dependencies, FFmpeg jobs or playback-time DSP in this batch. Work runs through asynchronous requests; only a bounded result set is materialized for the UI. These design bounds do not constitute performance measurements or proof of zero overhead.

Implementation references consulted: [OpenSubsonic endpoint reference](https://opensubsonic.netlify.app/docs/), [Jellyfin API source](https://github.com/jellyfin/jellyfin/tree/master/Jellyfin.Api/Controllers), [Emby REST reference](https://dev.emby.media/reference/RestAPI.html), [Plex Media Server reference](https://developer.plex.tv/pms/), [UPnP AVTransport service specification](https://upnp.org/specs/av/UPnP-av-AVTransport-v1-Service.pdf). Reading these references did not exercise any connected user's server/device.

## Deferred acceptance

Later acceptance must compile Release x64, cover all 100 provider/operation mappings, check credentials/headers/root prefixes, inspect actual server versions and response shapes, and verify each supported read/write on disposable test accounts/libraries/devices. Include empty/oversized/malformed responses; unknown endpoints and permissions; canceled/ambiguous writes; stale playlist reviews and duplicate entries; row/parameter ordering; private/public behavior; boolean versus numeric ratings; bookmark milliseconds and listen timestamps; SOAP faults/optional actions; URI/report redaction; no overwrite on JSON export; disconnect/close cancellation; and Fluent/DPI/theme/keyboard behavior. These checks have not been run for F0581–F0680. Previous acceptance reports remain unchanged and do not validate this batch.

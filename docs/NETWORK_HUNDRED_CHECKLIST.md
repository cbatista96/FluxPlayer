# Remote library actions — individual acceptance tracked

F0581–F0680 have source implementation. The 26 checked Subsonic actions and all 20 Jellyfin actions now pass against real isolated servers; see [runtime scope](LOCAL_SESSION_ACCEPTANCE.md). The remaining 54 entries retain independent acceptance requirements: four external Subsonic enrichment actions, 20 Emby, 20 Plex and ten UPnP actions. Shared descriptors do not certify another provider. These are subfeatures of broader original requirements, not 100 completed original requirements.

[Workflow scope and limits](UNTESTED_NETWORK_HUNDRED.md) · [Machine-readable mapping](network-hundred-manifest.json)

## Subsonic (30)

- [x] **F0581** Subsonic — Genre catalog with server counts. `genres` · Read-only. Read the server genre catalog and its song/album counts. Original R0135 remains partial where broader work is pending.
- [x] **F0582** Subsonic — Tracks by remote genre. `genre-songs` · Read-only. Request one bounded genre-song page; no local genre inference. Original R0135 remains partial where broader work is pending.
- [x] **F0583** Subsonic — Random remote song selection. `random-songs` · Read-only. Request up to 100 server-selected random songs. Original R0134 remains partial where broader work is pending.
- [ ] **F0584** Subsonic — Artist top-song discovery. `top-songs` · Read-only. Read server/Last.fm-backed top songs for an explicitly supplied artist name. Original R0135 remains partial where broader work is pending.
- [ ] **F0585** Subsonic — Related tracks from a song seed. `similar-songs` · Read-only. Request getSimilarSongs2 for a selected song ID; results depend on server similarity support. Original R0523 remains partial where broader work is pending.
- [ ] **F0586** Subsonic — Remote artist biography and links. `artist-info` · Read-only. Read bounded artist biography and descriptive links without opening external links. Original R0127 remains partial where broader work is pending.
- [ ] **F0587** Subsonic — Remote album notes and links. `album-info` · Read-only. Read server album notes and MusicBrainz/Last.fm references. Original R0485 remains partial where broader work is pending.
- [x] **F0588** Subsonic — Detailed remote song metadata. `song-info` · Read-only. Read selected song metadata, duration, track/disc and rating fields. Original R0232 remains partial where broader work is pending.
- [x] **F0589** Subsonic — Remote album text search. `search-albums` · Read-only. Use search3 album results rather than the existing track-only search. Original R0135 remains partial where broader work is pending.
- [x] **F0590** Subsonic — Remote artist text search. `search-artists` · Read-only. Use search3 artist results rather than the existing track-only search. Original R0135 remains partial where broader work is pending.
- [x] **F0591** Subsonic — Highest-rated remote albums. `rated-albums` · Read-only. Request a highest-rated album page from the server. Original R0134 remains partial where broader work is pending.
- [x] **F0592** Subsonic — Frequently played remote albums. `frequent-albums` · Read-only. Request server most-played albums, independent of FluxPlayer local history. Original R0109 remains partial where broader work is pending.
- [x] **F0593** Subsonic — Remote playlist occurrence inspection. `playlist-entries` · Read-only. Read ordered song entries with zero-based occurrence positions for reviewed removal. Original R0181 remains partial where broader work is pending.
- [x] **F0594** Subsonic — Remote album year-range browsing. `year-albums` · Read-only. Request one page between explicit release-year bounds. Original R0135 remains partial where broader work is pending.
- [x] **F0595** Subsonic — Remote albums by genre. `genre-albums` · Read-only. Request one getAlbumList2 byGenre page. Original R0135 remains partial where broader work is pending.
- [x] **F0596** Subsonic — Add a remote song favorite. `star` · Explicit remote change. Explicitly star one remote song; does not synchronize the local favorites index. Original R0180 remains partial where broader work is pending.
- [x] **F0597** Subsonic — Remove a remote song favorite. `unstar` · Explicit remote change. Explicitly unstar one remote song without deleting media. Original R0180 remains partial where broader work is pending.
- [x] **F0598** Subsonic — Set or clear a remote song rating. `rating` · Explicit remote change. Set the server's 0–5 rating; zero clears it. No local-rating synchronization. Original R0180 remains partial where broader work is pending.
- [x] **F0599** Subsonic — Explicit server playing-now report. `now-playing` · Explicit remote change. Send a single scrobble submission=false. Not automatic background reporting. Original R0215 remains partial where broader work is pending.
- [x] **F0600** Subsonic — Reviewed remote listening submission. `scrobble` · Explicit remote change. Submit one explicitly confirmed listen using a supplied past Unix-millisecond time. No automatic retry after ambiguous failure. Original R0215 remains partial where broader work is pending.
- [x] **F0601** Subsonic — Create a remote song playlist. `playlist-create` · Explicit remote change. Create a named server playlist from 1–50 ordered song IDs. Original R0181 remains partial where broader work is pending.
- [x] **F0602** Subsonic — Append songs to a remote playlist. `playlist-add` · Explicit remote change. Append 1–50 explicitly ordered song IDs without replacing existing entries. Original R0181 remains partial where broader work is pending.
- [x] **F0603** Subsonic — Rename a remote playlist. `playlist-rename` · Explicit remote change. Update only a selected remote playlist's name. Original R0181 remains partial where broader work is pending.
- [x] **F0604** Subsonic — Set remote playlist visibility. `playlist-visibility` · Explicit remote change. Explicit public/private flag for the selected server playlist. Original R0181 remains partial where broader work is pending.
- [x] **F0605** Subsonic — Remove selected remote playlist positions. `playlist-remove` · Explicit remote change. Remove 1–50 displayed zero-based positions after a fresh occurrence review and order recheck; not media deletion. Server-side atomic concurrency is not guaranteed. Original R0181 remains partial where broader work is pending.
- [x] **F0606** Subsonic — Read remote audiobook bookmarks. `bookmarks` · Read-only. Read server bookmarks and positions, bounded to 100 displayed entries. Original R0221 remains partial where broader work is pending.
- [x] **F0607** Subsonic — Save a remote media bookmark. `bookmark-save` · Explicit remote change. Create/update the server bookmark for an item with a millisecond position. Original R0221 remains partial where broader work is pending.
- [x] **F0608** Subsonic — Remove a remote media bookmark. `bookmark-remove` · Explicit remote change. Remove only the selected server bookmark, leaving the media intact. Original R0221 remains partial where broader work is pending.
- [x] **F0609** Subsonic — Read server-provided song lyrics. `lyrics` · Read-only. Request authored lyrics by artist/title; not transcription or provider scraping. Original R0051 remains partial where broader work is pending.
- [x] **F0610** Subsonic — Browse remote music-folder roots. `music-folders` · Read-only. Read available logical music folders and their server IDs. Original R0068 remains partial where broader work is pending.

## Jellyfin (20)

- [x] **F0611** Jellyfin — Server genre navigation. `genres` · Read-only. Read a bounded genre catalog for the selected user. Original R1275 remains partial where broader work is pending.
- [x] **F0612** Jellyfin — Server music-artist navigation. `artists` · Read-only. Read music artist entities, distinct from album-root navigation. Original R1275 remains partial where broader work is pending.
- [x] **F0613** Jellyfin — Server album-artist navigation. `album-artists` · Read-only. Read server album-artist entities with stable IDs. Original R1275 remains partial where broader work is pending.
- [x] **F0614** Jellyfin — Server release-year navigation. `years` · Read-only. Read available production-year facets. Original R1275 remains partial where broader work is pending.
- [x] **F0615** Jellyfin — Server studio navigation. `studios` · Read-only. Read studio entities for movie and television browsing. Original R1275 remains partial where broader work is pending.
- [x] **F0616** Jellyfin — Server descriptive-tag facets. `tags` · Read-only. Read tag names from item-filter metadata; no tag editing. Original R1275 remains partial where broader work is pending.
- [x] **F0617** Jellyfin — Randomized server media discovery. `random` · Read-only. Request a bounded random-order recursive media view. Original R0134 remains partial where broader work is pending.
- [x] **F0618** Jellyfin — Latest additions for the selected user. `latest` · Read-only. Read selected-user latest media, bounded to 100 displayed rows. Original R1275 remains partial where broader work is pending.
- [x] **F0619** Jellyfin — Server next-up television episodes. `next-up` · Read-only. Read the server's next-up episode view; not automatic queue replacement. Original R1275 remains partial where broader work is pending.
- [x] **F0620** Jellyfin — Server related-item discovery. `similar` · Read-only. Read similar media for a selected seed item ID. Original R0523 remains partial where broader work is pending.
- [x] **F0621** Jellyfin — Selected-item special-feature browsing. `features` · Read-only. Read a selected item's local special-feature entries. Original R1275 remains partial where broader work is pending.
- [x] **F0622** Jellyfin — Selected-item local-trailer browsing. `trailers` · Read-only. Read server-local trailers; does not contact commercial trailer services. Original R1275 remains partial where broader work is pending.
- [x] **F0623** Jellyfin — Selected-user item metadata inspection. `details` · Read-only. Read descriptive item and user-state fields through the selected server account. Original R0127 remains partial where broader work is pending.
- [x] **F0624** Jellyfin — Mark a server item favorite. `favorite` · Explicit remote change. Set one server favorite explicitly, without automatic local synchronization. Original R0180 remains partial where broader work is pending.
- [x] **F0625** Jellyfin — Remove a server item favorite. `unfavorite` · Explicit remote change. Clear one server favorite without deleting media. Original R0180 remains partial where broader work is pending.
- [x] **F0626** Jellyfin — Mark a server item played. `played` · Explicit remote change. Explicitly mark watched/listened state for one server item. Original R0724 remains partial where broader work is pending.
- [x] **F0627** Jellyfin — Mark a server item unplayed. `unplayed` · Explicit remote change. Explicitly clear watched/listened state for one server item. Original R0724 remains partial where broader work is pending.
- [x] **F0628** Jellyfin — Set a server item like or dislike. `like` · Explicit remote change. Write the server's boolean Likes rating; not a numeric star score. Original R0180 remains partial where broader work is pending.
- [x] **F0629** Jellyfin — Clear a server personal rating. `rating-clear` · Explicit remote change. Remove the selected user's server rating for one item. Original R0180 remains partial where broader work is pending.
- [x] **F0630** Jellyfin — Create a user-owned server playlist. `playlist-create` · Explicit remote change. Create a playlist from 1–50 explicit ordered item IDs, without replacing an existing playlist. Original R0181 remains partial where broader work is pending.

## Emby (20)

- [ ] **F0631** Emby — Server genre navigation. `genres` · Read-only. Read a bounded genre catalog for the selected user. Original R1275 remains partial where broader work is pending.
- [ ] **F0632** Emby — Server music-artist navigation. `artists` · Read-only. Read music artist entities, distinct from album-root navigation. Original R1275 remains partial where broader work is pending.
- [ ] **F0633** Emby — Server album-artist navigation. `album-artists` · Read-only. Read server album-artist entities with stable IDs. Original R1275 remains partial where broader work is pending.
- [ ] **F0634** Emby — Server release-year navigation. `years` · Read-only. Read available production-year facets. Original R1275 remains partial where broader work is pending.
- [ ] **F0635** Emby — Server studio navigation. `studios` · Read-only. Read studio entities for movie and television browsing. Original R1275 remains partial where broader work is pending.
- [ ] **F0636** Emby — Server descriptive-tag facets. `tags` · Read-only. Read tag names from item-filter metadata; no tag editing. Original R1275 remains partial where broader work is pending.
- [ ] **F0637** Emby — Randomized server media discovery. `random` · Read-only. Request a bounded random-order recursive media view. Original R0134 remains partial where broader work is pending.
- [ ] **F0638** Emby — Latest additions for the selected user. `latest` · Read-only. Read selected-user latest media, bounded to 100 displayed rows. Original R1275 remains partial where broader work is pending.
- [ ] **F0639** Emby — Server next-up television episodes. `next-up` · Read-only. Read the server's next-up episode view; not automatic queue replacement. Original R1275 remains partial where broader work is pending.
- [ ] **F0640** Emby — Server related-item discovery. `similar` · Read-only. Read similar media for a selected seed item ID. Original R0523 remains partial where broader work is pending.
- [ ] **F0641** Emby — Selected-item special-feature browsing. `features` · Read-only. Read a selected item's local special-feature entries. Original R1275 remains partial where broader work is pending.
- [ ] **F0642** Emby — Selected-item local-trailer browsing. `trailers` · Read-only. Read server-local trailers; does not contact commercial trailer services. Original R1275 remains partial where broader work is pending.
- [ ] **F0643** Emby — Selected-user item metadata inspection. `details` · Read-only. Read descriptive item and user-state fields through the selected server account. Original R0127 remains partial where broader work is pending.
- [ ] **F0644** Emby — Mark a server item favorite. `favorite` · Explicit remote change. Set one server favorite explicitly, without automatic local synchronization. Original R0180 remains partial where broader work is pending.
- [ ] **F0645** Emby — Remove a server item favorite. `unfavorite` · Explicit remote change. Clear one server favorite without deleting media. Original R0180 remains partial where broader work is pending.
- [ ] **F0646** Emby — Mark a server item played. `played` · Explicit remote change. Explicitly mark watched/listened state for one server item. Original R0724 remains partial where broader work is pending.
- [ ] **F0647** Emby — Mark a server item unplayed. `unplayed` · Explicit remote change. Explicitly clear watched/listened state for one server item. Original R0724 remains partial where broader work is pending.
- [ ] **F0648** Emby — Set a server item like or dislike. `like` · Explicit remote change. Write the server's boolean Likes rating; not a numeric star score. Original R0180 remains partial where broader work is pending.
- [ ] **F0649** Emby — Clear a server personal rating. `rating-clear` · Explicit remote change. Remove the selected user's server rating for one item. Original R0180 remains partial where broader work is pending.
- [ ] **F0650** Emby — Create a user-owned server playlist. `playlist-create` · Explicit remote change. Create a playlist from 1–50 explicit ordered item IDs, without replacing an existing playlist. Original R0181 remains partial where broader work is pending.

## Plex (20)

- [ ] **F0651** Plex selected-item metadata inspection. `details` · Read-only. Inspect bounded descriptive and user-state attributes for a metadata ID. Original R0538 remains partial where broader work is pending.
- [ ] **F0652** Plex recursive episode and track leaves. `leaves` · Read-only. Read up to 100 leaf items beneath a show, season, artist or album. Original R0538 remains partial where broader work is pending.
- [ ] **F0653** Plex library collection browsing. `collections` · Read-only. List collections for a selected section. Original R0538 remains partial where broader work is pending.
- [ ] **F0654** Plex collection-member browsing. `collection-items` · Read-only. Read a collection's children rather than normal library-directory children. Original R0538 remains partial where broader work is pending.
- [ ] **F0655** Plex genre facets. `genres` · Read-only. Read section genre facets and filter keys. Original R0538 remains partial where broader work is pending.
- [ ] **F0656** Plex production-country facets. `countries` · Read-only. Read section country facets. Original R0538 remains partial where broader work is pending.
- [ ] **F0657** Plex release-decade facets. `decades` · Read-only. Read section decade facets. Original R0538 remains partial where broader work is pending.
- [ ] **F0658** Plex director facets. `directors` · Read-only. Read section director facets, subject to media type. Original R0538 remains partial where broader work is pending.
- [ ] **F0659** Plex cast-member facets. `actors` · Read-only. Read section actor facets, subject to media type. Original R0538 remains partial where broader work is pending.
- [ ] **F0660** Plex personal-label facets. `labels` · Read-only. Read existing section label facets; no global metadata writes. Original R0538 remains partial where broader work is pending.
- [ ] **F0661** Plex unwatched-item view. `unwatched` · Read-only. Request the section's unplayed-only media view. Original R0724 remains partial where broader work is pending.
- [ ] **F0662** Plex playlist occurrence inspection. `playlist-entries` · Read-only. Read playlist entries with playlistItemID occurrence identifiers for subsequent remove/move operations. Original R0181 remains partial where broader work is pending.
- [ ] **F0663** Plex related-item browsing. `similar` · Read-only. Read server similar items for a seed metadata ID. Original R0523 remains partial where broader work is pending.
- [ ] **F0664** Plex explicit played-state marking. `played` · Explicit remote change. Send one scrobble state operation using the library identifier. Original R0724 remains partial where broader work is pending.
- [ ] **F0665** Plex explicit unplayed-state marking. `unplayed` · Explicit remote change. Send one unscrobble operation; no media deletion. Original R0724 remains partial where broader work is pending.
- [ ] **F0666** Plex personal rating update. `rating` · Explicit remote change. Set one user rating from 0 to 10; does not update FluxPlayer's 0–5 ratings. Original R0180 remains partial where broader work is pending.
- [ ] **F0667** Plex playlist renaming. `playlist-rename` · Explicit remote change. Change only a selected playlist's title. Original R0181 remains partial where broader work is pending.
- [ ] **F0668** Plex playlist occurrence removal. `playlist-remove` · Explicit remote change. Remove one playlistItemID occurrence; the media rating key is not an occurrence ID. Original R0181 remains partial where broader work is pending.
- [ ] **F0669** Plex playlist occurrence reordering. `playlist-move` · Explicit remote change. Move one playlistItemID after another; zero means first position. Original R0181 remains partial where broader work is pending.
- [ ] **F0670** Plex playlist creation from media. `playlist-create` · Explicit remote change. Build one playlist from a selected local Plex metadata key and server identity. Original R0181 remains partial where broader work is pending.

## UPnP (10)

- [ ] **F0671** UPnP selected-object metadata. `metadata` · Read-only. BrowseMetadata for one selected object without browsing its children. Original R0059 remains partial where broader work is pending.
- [ ] **F0672** UPnP searchable-field capabilities. `search-capabilities` · Read-only. Read SearchCaps from the selected ContentDirectory service. Original R0059 remains partial where broader work is pending.
- [ ] **F0673** UPnP sortable-field capabilities. `sort-capabilities` · Read-only. Read SortCaps from the selected ContentDirectory service. Original R0059 remains partial where broader work is pending.
- [ ] **F0674** UPnP content-directory revision snapshot. `update-id` · Read-only. Read SystemUpdateID explicitly; no event subscription or polling. Original R0059 remains partial where broader work is pending.
- [ ] **F0675** UPnP renderer media information. `media-info` · Read-only. Read current/next URI and track-count metadata for instance zero. Original R0059 remains partial where broader work is pending.
- [ ] **F0676** UPnP renderer media capabilities. `device-capabilities` · Read-only. Read supported playback/recording media and quality modes. Original R0059 remains partial where broader work is pending.
- [ ] **F0677** UPnP renderer transport settings. `transport-settings` · Read-only. Read play mode and recording-quality mode. Original R0059 remains partial where broader work is pending.
- [ ] **F0678** UPnP currently available transport actions. `transport-actions` · Read-only. Read device-reported currently valid AVTransport commands. Original R0059 remains partial where broader work is pending.
- [ ] **F0679** UPnP renderer repeat and shuffle mode. `play-mode` · Explicit remote change. Request a standardized play mode; device support is conditional. Original R0059 remains partial where broader work is pending.
- [ ] **F0680** UPnP next-resource staging. `next-uri` · Explicit remote change. SetNextAVTransportURI for an explicit HTTP(S) resource with empty metadata; not synchronized or guaranteed gapless playback. Original R0059 remains partial where broader work is pending.

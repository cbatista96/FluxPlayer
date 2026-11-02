# Spoken media, personal ratings and playback workspaces

These ten capabilities are available in the local desktop build. All actions use the existing settings categories and window materials.

| Capability | Where to open it | What is saved |
| --- | --- | --- |
| Podcast subscriptions | Settings > Library > Podcasts and downloads | HTTP/HTTPS feed addresses, feed titles, episodes and listening state |
| Offline podcast copies | Podcast browser > Download episode | Completed audio/video enclosure copies inside the active profile |
| OPML interchange | Podcast browser > OPML | Subscription addresses, imported without retrieving the feeds |
| Audiobook shelf | Settings > Library > Audiobooks | Local files, position, individual speed, named bookmarks and completion |
| Listening statistics | Settings > Library > Listening statistics | Views are calculated from retained playback history; CSV export writes a new copy |
| Personal ratings | Media library > Rate | A local rating from 0 (unrated) to 5; optional Rating column and numeric queries |
| External audio | Settings > Audio > Attach external audio | Additional local tracks for the current source |
| Video crop | Settings > Video > Video crop | A temporary pixel rectangle for the current source |
| Picture presets | Settings > Video > Picture presets | Named brightness, contrast, saturation, hue and gamma combinations |
| Saved sessions | Settings > Playback > Saved playback sessions | Queue order and labels, selected occurrence, position, rate, repeat and shuffle |

External audio, crop, picture presets and saved sessions also have curated player context-menu shortcuts in Audio, Video and Play. The Library menu still opens the media library for ratings.

## Podcasts and downloads

Paste an RSS 2.0 or Atom feed address, then choose Subscribe. The subscription is stored immediately. Choose Refresh feed to retrieve its title and enclosures; there is no periodic polling or automatic download. Search and All episodes / Not listened / Downloaded filters apply to the selected feed. Refresh retains existing listening progress and downloaded copies.

Play / resume uses an available offline copy first, otherwise the enclosure URL. Download episode fetches one selected enclosure, and Cancel task interrupts it. A completed copy is published only after the response finishes. Repeating Download reuses an existing copy. Limits are 4 MiB per feed/OPML document, 100 subscriptions, 1,000 enclosures per refresh, 50,000 retained episodes, and 512 MiB / 30 minutes per download. Feed requests have a 30-second deadline; redirects are bounded. Embedded credentials and non-HTTP feed/enclosure URLs are rejected.

OPML import flattens nested outlines, deduplicates subscription addresses, and leaves retrieval explicit. Imported subscription names initially use the host and are updated from the feed on refresh. Export writes a new OPML file containing the saved subscription titles and addresses. Existing destinations are refused.

Stop an active episode before removing its downloaded copy. Remove download affects only the application-owned profile copy and retains the episode's online address and listening state. Unsubscribe refuses subscriptions with downloaded copies; remove those copies first. No paid-feed account support, podcast directory, unattended download scheduler or full protocol/codec matrix is included.

## Audiobooks and listening statistics

Add audio files creates shelf entries without moving the originals. Resume selected restores the book's position and rate; Restart starts it at zero. Speed sets an individual rate. Bookmarks adds a named position and provides navigation/removal for saved marks. Completion marks the selected book completed or in progress. Removing a shelf entry retains the original file.

Book and episode progress saves on pause, stop, source switch and application close. Native end-of-media marks the active item complete/listened. Progress is not written to disk on every time update. An interrupted process can lose progress since the last checkpoint. A book is currently one source file; grouping several chapter files into one book is outside this version.

Listening statistics uses only retained **audio** sessions from playback history. Recording remains controlled in Settings > Library. Group by Artists, Tracks or Days and choose all retained history or the last 7/30/90 days. Dates use the session's start time in UTC. Listening totals exclude pauses; completed counts are end-of-media events. Muted playback still contributes. These figures do not measure what someone heard. Export this view writes a new CSV with quoted text and spreadsheet formula protection.

## Ratings and saved sessions

Select one or more library files and choose Rate to assign 1–5 stars or Clear rating. Ratings are local library metadata, carried by library backup/import; source tags are unchanged. Enable the optional Rating column for numeric sorting. Queries such as `rating:>=4`, `rating:0` and `-rating:5` work in searches and saved smart-list rules. Automatic ratings are still pending.

Save current as creates a named session. Replace selected explicitly replaces that session with the current queue and transport state. Restore queue stops playback and replaces the queue, retaining its saved position until Play is requested. Restore and play starts explicitly. Missing local files are skipped and the saved occurrence is remapped; if that occurrence is missing, playback starts at the first available entry from zero. Duplicate sources keep their separate labels and order. Clearing the queue also discards a staged restored position.

Limits are 100 sessions, 10,000 entries in one session and 100,000 saved entries in total. Removing a saved session retains media files. Sessions store source references; they do not copy media or automatically find files moved elsewhere.

## External audio and video picture

Attach external audio chooses a local file and adds it to active media without restarting transport. Use Audio track in the transport bar or context menu to select between streams. Reattaching the same file does not duplicate it. Up to 16 distinct external files can be attached to one source; attachments clear when a new source opens. Supported content, alignment and available stream duration depend on the native decoder and files.

Video crop reads the decoded source size. Enter Left, Top, Width and Height in whole pixels, then Apply crop. The rectangle must be positive and contained in the source. Read current video size again if the source changed. Reset crop restores the full picture. This is a manual crop; automatic border detection is not included. Exported snapshots use the same rectangle. Cropping resets for the next source.

Picture presets contains up to 32 named combinations. Preview applies a temporary picture; closing the window restores the saved active picture. Save and use persists the values and activates the preset for subsequent media. Use selected activates an existing preset. Neutral picture disables adjustment processing and resets the legacy picture values. Remove preset deletes its saved definition. Non-neutral adjustments add native video processing; an unchanged neutral picture avoids restarting that filter.

## Profile data and performance

Subscriptions, books and episodes live in `spoken-media.json`; completed downloads live in `PodcastDownloads/`. Saved sessions live in `playback-sessions.json`. Both documents are lazy, bounded, versioned JSON stores with serialized atomic publication. A failed write preserves the in-memory and disk snapshot; malformed originals are retained and reported. Ratings belong to `library.json`, picture presets to `settings.json`, and listening records to `playback-history.json`. Library-index backup does not include the separate spoken-media/session documents or downloaded files.

New windows recycle table rows. Feed retrieval, downloads and document operations are asynchronous or use a worker. No background podcast timer or startup scan is added. The WPF crop maps a viewport of the existing shared-memory video section rather than copying a frame each time; snapshot cropping occurs only during an explicit capture. These choices bound unnecessary work, but do not guarantee zero overhead or support for every format/hardware combination.

> **Verification update — 2026-10-28:** All 30 capabilities in this document passed acceptance tests. The original deferral narrative below is historical and is superseded by [100 verified capabilities](VERIFIED_HUNDRED.md).

# Thirty implemented capabilities awaiting testing

This is the first untested thirty-capability batch. The [processing batch](UNTESTED_PROCESSING_THIRTY.md) and [external-control/capture/media batch](UNTESTED_EXTERNAL_MEDIA_THIRTY.md) extend the pending-verification queue to ninety. Each retains its own source references and unchecked acceptance list.

**Status: source implementation only. None of these thirty capabilities has been built, launched, visually reviewed, tested or benchmarked in this batch.** Testing was explicitly deferred by the user. The existing development executable still belongs to the preceding source checkpoint; it does not contain these changes.

The public catalog uses `implemented_unverified`. The original checklist has a separate **Funciones implementadas sin probar** section. Unchecked boxes below represent pending verification of existing source implementation. They must not be read as passing tests or production readiness.

## Implemented — untested

| Verification pending | Capability | Original entry | Source |
|---|---|---|---|
| [x] F0201 | Local movie library | R1108 | CinemaLibraryWindow / CinemaCatalogService |
| [x] F0202 | Series and television library | R1109 | CinemaSeriesWindow / CinemaLibraryWindow |
| [x] F0203 | Movie/episode posters | R1222 | CinemaItemEditor / detail view |
| [x] F0204 | Independent video fanart | R1223 | CinemaItemEditor / detail view |
| [x] F0205 | Actor information profiles | R1224 | CinemaPersonEditor / CinemaPeopleWindow |
| [x] F0206 | Editable video synopses | R1225 | CinemaItemEditor / search and details |
| [x] F0207 | Optional release dates | R1226 | CinemaItem.ReleaseDate / editor |
| [x] F0208 | Content classification labels | R1228 | CinemaItem.Classification / editor |
| [x] F0209 | Watched movies | R1230 | Viewing-status actions and filters |
| [x] F0210 | Movie watchlist/unwatched views | R1231 | Viewing-status actions and filters |
| [x] F0211 | Series/season navigation | R1246 | Dependent series/season selectors |
| [x] F0212 | Numbered episode records | R1238 | CinemaItemEditor / catalog membership |
| [x] F0213 | Season covers | R1239 | CinemaSeasonEditor / episode details |
| [x] F0214 | Series banners | R1240 | CinemaSeriesEditor / series and episode details |
| [x] F0215 | Cast members and character roles | R1242 | CinemaCredit / reverse people credits |
| [x] F0216 | Watched-episode tracking | R1244 | Explicit per-episode state actions |
| [x] F0217 | Continue an unfinished series | R1245 | CinemaCatalogService.Continue / PlayCinema |
| [x] F0218 | Paged poster wall | R1114 | CinemaLibraryWindow.RenderWall |
| [x] F0219 | Local trailers before movies | R1573 | Trailer preference / PlayCinema / Up next |
| [x] F0220 | TV library with large controls | R1313 | CinemaTheaterWindow |
| [x] F0221 | Artist biographies | R0128 | CinemaPersonEditor / CinemaPeopleWindow |
| [x] F0222 | Composer information | R0129 | Composer profiles and work associations |
| [x] F0223 | Production credits | R0130 | CinemaCredit jobs / reverse people credits |
| [x] F0224 | Artist/person relationships | R0131 | ArtistRelation / linked-profile navigation |
| [x] F0225 | Discographies and local release playback | R0132 | ArtistRelease / people details |
| [x] F0226 | Multiple values per metadata field | R0301 | CinemaText.Attributes / title and work editors |
| [x] F0227 | Classical collections and ordered movements | R0298 | ClassicalWorkEditor / ClassicalLibraryWindow |
| [x] F0228 | Classification/ordering by criteria | R1235 | Query ordering, rating and combined filters |
| [x] F0229 | Reduced-size artwork thumbnails | R1113 | CinemaArtwork / poster-wall cards |
| [x] F0230 | Viewing status and captured position | R1243 | Explicit position capture / progress store |

## Where the new code is exposed

- **Settings → Library → Movies and television**: Movie library; Series and episodes. Both open the same catalog design with a different initial media-type filter.
- **Settings → Library → People and credits**: Artists, composers and actors.
- **Settings → Audio → Classical music**: Classical collections.
- **Settings → Video → Presentation**: TV library view.
- Selected quick access: Movie library and TV library view are registered for the existing curated right-click menu. The app retains its single settings entry point.

The library footer groups commands into **Add and edit**, **Viewing status**, **Views**, and **Playback options**, alongside Play and Refresh. These reuse the existing Fluent workbench, title bar, native styled menus, buttons and checkboxes. This is source-level design reuse, not a claim that the result has passed visual review.

## Intended workflows

Create people profiles before assigning cast/production credits. Profiles support Actor, Artist and Composer types. Credits use an existing person, a job and an optional character/role. The person's detail panel lists their linked catalog credits. Relationships are labeled, directional links to another existing profile. Discographies group rows by year and album title; optional local tracks can be explicitly queued.

For a movie, choose a local source, then edit title, synopsis, optional release date, content classification, personal 0–10 rating, poster, fanart, trailer, credits and attributes. A series has its own synopsis and banner. Create its named seasons and season covers before adding numbered episodes. Season 0 is available for specials. Episode identity is unique within series/season/episode number.

The viewing-status menu explicitly marks watched, unwatched or watchlist entries. **Capture current position** requires the selected entry to be the exact current local source and stores an In progress position. This does not automatically monitor playback or infer completion. **Continue series** chooses the lowest season/episode not marked Watched and hands the stored position to the existing explicit-resume path. Mark finished episodes to advance the continuation target; specials sort before season 1.

The poster wall is paged at forty cards and can switch back to the virtualized table. Text searches wait briefly for typing to pause before filtering. Artwork is loaded only on request, from local image paths. The intended cache is per-window, up to 48 entries and 32 MiB of estimated cached pixels, with serialized decode and source-stamp keys. These bounds do not certify image-codec peak memory. JPEG/PNG/BMP and the first GIF frame/TIFF page are intended; artwork source files are not modified and no thumbnail files are written beside them.

The TV view uses large text and standard directional keyboard input. Up/down navigates the list, Enter plays, right/left changes focus, F toggles fullscreen and Escape returns/closes. It does not implement HDMI-CEC, a mobile remote or a hardware controller driver.

The local-trailer option stores an explicit preference. If an assigned trailer is available, playback adds it before the feature and places the feature first in Up next. Existing manual stop, stop-after-current and repeat-one policies can still prevent automatic transition. There is no online trailer download or commercial streaming integration.

Classical records group a work's ordered movements under an existing Composer profile and a named collection. Work playback queues available local movements in saved order; a movement can be played separately. Multiple-value attributes can hold instrumentation, performers, periods, identifiers or genres on both classical works and videos.

## Persistence and scope

`cinema-catalog.json` is a lazy profile document, separate from the existing music index. No automatic migration, file scan, online metadata scraper or network provider was added. All metadata is manually maintained; original media and embedded tags are preserved. Artwork paths remain external associations, so moving the images can make them unavailable.

Source-level guards include bounded document/record sizes, reference checks, unique episode identities, finite numeric fields, stale-revision checks for titles/series/people/works, and atomic profile publication. Season edits replace a matching series/season record and currently do not have revision-conflict detection. These guard paths themselves have not been tested.

## Deferred verification

The following remains deliberately unchecked:

- [ ] Compile the new C# files and XAML action registry.
- [ ] Exercise all thirty workflows and their error paths.
- [ ] Check JSON round trips, profile corruption, conflicting editors and failed writes.
- [ ] Verify trailer-to-feature transitions, repeat/stop interactions and explicit resume.
- [ ] Review actual Fluent presentation, grouped menus, focus, keyboard navigation, DPI and minimum window sizes.
- [ ] Verify thumbnail cancellation, cache bounds and codec peak memory.
- [ ] Run the existing regression suite against this new source checkpoint.

The previous 92 passing test groups are historical evidence for the preceding batch. They provide no test coverage claim for this batch. Nothing was published to GitHub and no executable, installer or release was rebuilt.

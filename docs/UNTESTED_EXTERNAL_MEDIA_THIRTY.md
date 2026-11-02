# Thirty external-control, capture and media capabilities — untested

**Code implementation only. This batch was not compiled, launched, visually reviewed, exercised or benchmarked. No API listener, capture device, stream, scheduled recorder or buffer was started during development.** The user explicitly deferred testing. The command-line client was written but not executed. The existing executable still predates all three untested batches.

These additions follow [the catalog batch](UNTESTED_THIRTY.md) and [the processing batch](UNTESTED_PROCESSING_THIRTY.md). There are now **90 implemented-but-untested capabilities**. Existing historical test results do not cover these changes.

## Implemented — verification pending

| Verification pending | Capability | Original |
|---|---|---|
| [ ] F0261 | Current-user named-pipe JSON IPC | R1006 |
| [ ] F0262 | External transport and queue command dispatcher | R1007 |
| [ ] F0263 | Read-only player property snapshots | R1008 |
| [ ] F0264 | Player/control events and WebSocket subscription | R1009 |
| [ ] F0265 | JSON-RPC 2.0 control subset | R1278 |
| [ ] F0266 | Authenticated loopback WebSocket transport | R1279 |
| [ ] F0267 | Loopback HTTP control/state endpoints | R1280 |
| [ ] F0268 | Loopback TCP/IP JSON lines | R1548 |
| [ ] F0269 | Registered-window-message transport integration | R1549 |
| [ ] F0270 | CLI client for the active player's control pipe | R1551 |
| [ ] F0271 | Bounded control journal and new-copy event-log export | R1018 |
| [ ] F0272 | Session-activated automatic extension profiles | R1003 |
| [ ] F0273 | Finite microphone recording | R1093 |
| [ ] F0274 | Explicit five-second microphone level check | R1094 |
| [ ] F0275 | Selected-camera preview | R1369 |
| [ ] F0276 | DirectShow device recording | R1370 |
| [ ] F0277 | Desktop video capture | R1435 |
| [ ] F0278 | Manual recorded-audio/video alignment | R1095 |
| [ ] F0279 | Provided IPTV-channel live playback | R1283 |
| [ ] F0280 | Local XMLTV programme guide | R1284 |
| [ ] F0281 | Channel catalog, groups and guide IDs | R1285 |
| [ ] F0282 | Completed-recording catalog and replay | R1286 |
| [ ] F0283 | Session-armed scheduled television recording | R1287 |
| [ ] F0284 | Segmented timeshift buffer and buffered playback | R1288 |
| [ ] F0285 | Finite compatible-stream copy recording | R1431 |
| [ ] F0286 | Bounded animated GIF photo playback | R1120 |
| [ ] F0287 | Conditional HEIF/HEIC decoding through Windows codecs | R1121 |
| [ ] F0288 | Saved custom Fluent accent resources | R1560 |
| [ ] F0289 | Saved idle-video background artwork | R1123 |
| [ ] F0290 | Left/right equalizer-window docking | R1567 |

## Categorized access

- **Settings → Playback → External control**: Local external control.
- **Settings → Playback → Media profiles**: Automatic media profiles.
- **Settings → General → Appearance**: Appearance and equalizer docking.
- **Settings → Video → Capture**: Capture and recorded-audio synchronization.
- **Settings → Video → Television**: Television, guide and recordings; also registered for the existing curated right-click menu.
- **Settings → Library → Photos**: existing photo library/viewer, extended for GIF playback and conditional HEIF/HEIC.

New windows reuse the existing Fluent/Mica workbench, standard styled fields/checkboxes, categorized action registry and grouped menus. This is code reuse, not evidence of a completed visual review. No second settings button was added.

## Local external control

Listeners are off by default and must be started in the control panel. Named pipes use the current Windows user and a profile-derived name. TCP and HTTP bind only to loopback. Each start generates a new 256-bit session token. HTTP requires `Authorization: Bearer <token>`; pipe/TCP requests carry `token` in the JSON object. Browser Origin requests are rejected. This is local application control, not LAN/mobile access or a browser remote-control page.

The implementation uses four shared connection slots, bounded requests, idle deadlines and per-connection throttling. A waiting pipe listener can occupy a slot. HTTP binding can fail because of URL permissions or an occupied port; no ACL, firewall or Windows security setting is changed automatically. Listener availability, authorization and shutdown behavior still need testing.

Intended HTTP endpoints are `GET /state`, `POST /rpc` and WebSocket `/ws`. JSON-RPC support is a bounded object-request subset: no batch/full-spec/notification compliance claim. The allowlisted methods are `ping`, `state.get`, `play`, `pause`, `stop`, `next`, `previous`, `seek`, `volume`, `rate`, `mute`, `open`, `queue.add`, `queue.clear`, `events.get`, and `events.subscribe` (streaming subscription on WebSocket).

Properties include current title/source, playing status, time/duration, volume/mute, speed and queue count. Queue mutations accept at most 100 explicit supported media sources; no shell commands or arbitrary code are evaluated. The journal retains at most 256 events, recording selected player-property changes and control commands. It is inspectable in the panel and explicitly exportable as a new JSONL file; tokens are not included in those journal records. Events are bounded/best-effort, not a durable lossless bus.

`tools/FluxPlayer-Control.ps1` is an unexecuted PowerShell client. It requires the pipe name and active token (normally supplied through `FLUXPLAYER_CONTROL_TOKEN`), an allowlisted method and optional JSON parameters. Examples of intended parameters are `{"value":25}` for `volume`, `{"positionMs":10000}` for `seek`, and `{"sources":["C:\\Media\\clip.mp4"]}` for `open`. Do not infer successful communication from these examples.

The optional `FluxPlayer.Transport.v1` registered window message is a separate local desktop channel with only fixed numeric actions: wParam 1 toggle, 2 stop, 3 next, 4 previous; lParam must be zero. It does not use bearer authentication and does not accept pointers, file paths or arbitrary payloads. Its hook exists only while explicitly enabled in the control panel and is removed when that panel closes. It has not been security-tested.

Closing the control panel leaves explicitly started pipe/TCP/HTTP listeners running until Stop or application shutdown; window-message hooks close with the panel. None of these lifecycles has been exercised yet.

## Automatic media profiles

Up to 32 local rules match exact extensions. Rules can keep or set playback mode, volume and speed. The first matching rule in the saved order wins. Saving a rule does not silently activate it; **Activate / reload rules** copies the saved snapshot into memory for this session. **Disable rules** removes that active snapshot. Later media opens use the cached rules without reading the rule file each time.

These are FluxPlayer extension rules, not Lua, JavaScript, mpv.conf, input.conf or external plugin execution. Their interactions with per-file memory, manual settings and native playback startup remain untested.

## Capture and synchronization

Device enumeration, microphone measurement, camera preview and capture begin only from explicit actions. FFmpeg DirectShow support and functioning Windows drivers are required. No device probe was executed while implementing them.

Microphone recording creates a new 48 kHz stereo 24-bit PCM WAV. Device video capture creates a new H.264/AAC Matroska file with an optional selected microphone. Desktop capture uses `gdigrab` at 15 FPS without audio. Recordings use an explicit finite duration, 1–14,400 seconds. Canceling discards the unpublished partial output; it is not a graceful stop-and-save command.

The microphone check records only five seconds of bounded mono PCM for sample RMS/peak calculation. It does not play the microphone audibly or certify hardware calibration. Camera preview uses a separate muted LibVLC/renderer instance and releases it when closed. Some drivers may allow only one camera client; the UI advises closing preview before recording.

Recorded-audio synchronization is manual: an offset of ±30,000 ms delays or trims the chosen recording, copies the first video stream and replaces its audio with FLAC in a new Matroska file. Output ends with the shortest stream. It does not estimate alignment, perform live overdubbing or mix original video audio automatically.

## Television, guide and recording

The local channel catalog accepts explicit HTTP(S), RTSP, UDP or RTP sources without URL user-info credentials. It supports extended-M3U import, name/group/guide-ID editing and source-URL merging. XMLTV import is local, bounded, prohibits DTDs and maps programme channel IDs to the chosen guide IDs. Missing XMLTV numeric offsets are treated as UTC. Sources with encrypted/DRM/provider-specific access are not implemented by adding a URL.

Channel playback uses the existing engine and queue. Completed stream recordings are stored in the local recording catalog with file, time and duration, and can be explicitly replayed. Stream-copy recording copies first available video/audio streams into a new Matroska file without promising every codec/container combination. Network protocol timeouts and a duration-based deadline are coded, but their behavior has not been exercised.

Schedules can use a future explicit timestamp or selected guide programme. The scheduler must be **armed for this session** and the application must remain running. It processes one recording at a time, persists a claim before capture, marks starts over two minutes late Missed, and records completion/failure/cancellation state. It does not wake a sleeping computer or run as a Windows service. Closing the television panel leaves an armed scheduler running; disarming or application shutdown cancels its active capture. Abandoned Claimed tasks after a crash are not automatically retried.

Timeshift is an explicit FFmpeg stream-copy session producing nominal ten-second MPEG-TS segments in a newly owned profile folder. Actual boundaries depend on stream/keyframes. The UI lists completed segments and can play an earlier one while capture continues. It does not implement seamless live-edge switching, a clock-synchronized DVR timeline or Tvheadend/NextPVR integration.

The buffer attempts to keep thirty segments, excluding the active segment from playback, and checks a 512 MiB session disk guard every two seconds. Files held open may resist deletion. This is not a hard instantaneous disk quota. Closed/stopped session folders remain on disk; no global multi-session quota or automatic archive purge is claimed. Closing the television panel stops its buffer. Playback continuity, retention and cleanup need testing before relying on this feature.

## Photos, accent, background and docking

The GIF decoder/viewer implements frame composition, disposal, frame-delay and loop metadata, and an explicit pause/play button. Limits are 16 MiB input, 300 frames, four-megapixel logical canvas, 960-pixel displayed long edge and 128 MiB estimated retained frame pixels. Decoder/canvas working memory is additional. Animation stops on minimize/source change/close; minimized animation must be resumed explicitly. GIF variants, transparency, disposal, timing and loop behavior remain unverified.

HEIF/HEIC extensions are accepted by the existing bounded WIC photo-decoding path. This depends on an appropriate installed Windows codec. No codec or Store extension is installed automatically, and failure reports through the viewer. Compatibility with an actual HEIF/HEIC file has not been checked.

Appearance preferences save an optional `#RRGGBB` accent and restore theme defaults when cleared. The code updates selected Fluent accent/selection/focus resources while retaining existing materials and branded gradients. Actual contrast, theme changes and control coverage are unreviewed. A local background image can be assigned to the idle video canvas with 0.05–0.5 opacity; it collapses when media is open. A missing associated image does not supply an embedded fallback.

Equalizer docking is opt-in, left or right, and follows player move/resize events without a polling timer. Placement is clamped to the owner's monitor work area. On a small monitor it may overlap the owner. This is docking for the owned equalizer, not a general docking framework. Cross-monitor/DPI/maximized-window behavior remains untested.

## Deferred verification

- [ ] Compile all new C# files and XAML action registrations.
- [ ] Exercise all listeners, authentication failures, limits, malformed input and stop/restart races.
- [ ] Verify JSON-RPC subset behavior, WebSocket subscriptions and event ordering/retention.
- [ ] Run the CLI against an explicitly started test-profile pipe.
- [ ] Check window-message hook enable/disable and permitted transport actions.
- [ ] Verify automatic-profile activation, precedence and native application timing.
- [ ] Test capture enumeration, measurement, preview, recording, cancellation and driver failures.
- [ ] Measure recorded-audio alignment and output duration/stream correctness.
- [ ] Check M3U/XMLTV parsing, timestamps, channel edits and corrupted/oversized data.
- [ ] Exercise recording claims, overlap, missed starts, failure, cancellation and restart states.
- [ ] Verify stream-copy compatibility and buffering disk/retention/seek behavior.
- [ ] Check GIF frames/disposal/transparency/loops and actual HEIF/HEIC codec support.
- [ ] Review Fluent layout, focus, contrast, backgrounds, docking and multiple DPI/monitor configurations.
- [ ] Run the existing regression suite against all ninety untested additions.

No new binaries, installers, releases or test results were produced. Nothing was published to GitHub.

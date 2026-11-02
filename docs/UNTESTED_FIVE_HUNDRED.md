# Five hundred interoperability and processing operations — untested source

**Current acceptance update:** the 100 processing algorithms F1081–F1180 pass actual exports/decode checks, and the 100 mpv controls F0681–F0780 pass real named-pipe IPC acceptance. All 184 Kodi and 116 MPD methods pass local protocol-contract fixtures; another 100 Kodi malformed-response guards and 100 MPD invalid-input guards pass as well. The full local regression with portable mpv and MKVToolNix passed **1,451 groups with zero failures**, including 273 server-action input and HTTP/XML/SOAP loopback checks. Simulated providers do not prove real servers accept the operations or change state. The 300 Kodi/MPD operations remain `implemented_unverified` pending compatible backend acceptance. See [processing scope](PROCESSING_ACCEPTANCE.md) and [backend scope](LOCAL_BACKEND_ACCEPTANCE.md).

The source-only statements below describe the earlier checkpoint and are superseded for F0681–F0780 and F1081–F1180. The remaining F0781–F1080 Kodi/MPD methods require real backend acceptance. This does not certify alternate GPU/audio hardware, provider compatibility, performance or visual/DPI behavior. No public release was created.

[Individual checklist: all 500 entries](FIVE_HUNDRED_CHECKLIST.md) · [Machine-readable mapping](five-hundred-manifest.json) · [Reference attribution](THIRD_PARTY_INTEROP.md)

The count is **500 explicit operations/subfeatures**, not 500 fully completed original requirements: 100 mpv controls, 184 Kodi RPC methods, 116 MPD protocol commands and 100 distinct processing algorithms. The earlier broad native/hardware/licensing/synchronization requirements retain their existing status. Readback calls, parameter values, alternative presets and inspection helpers are not additional IDs. Kodi/MPD methods are individually counted API operations; this distinction is intentional and visible in the checklist.

## Entry points and organization

| Settings category | Section / panel | New capabilities |
|---|---|---|
| Playback | Alternative engines → Alternate mpv playback session | F0681–F0780: 100 controls |
| Library | Home theater integration → Kodi media-center integration | F0781–F0964: 184 methods |
| Audio | Network players → MPD network-player integration | F0965–F1080: 116 commands |
| Audio | Offline effect recipes → Audio effect recipes | F1081–F1105: 25 algorithms |
| Video | Offline filter recipes → Video filter recipes | F1106–F1180: 75 algorithms |

Three new panel entries extend categorized Settings; processing reuses the earlier recipe editors. mpv also declares curated context-menu access. No second Settings button or catch-all Tools category is introduced. Controls are further grouped by purpose: mpv transport/audio/subtitles/picture/scaling/HDR/output, Kodi namespace, MPD playback/queues/library/tags/outputs/capabilities, and processing algorithm families. Native WPF controls inherit the existing Fluent styles and Mica workbench; actual layout, keyboard, light/dark, screen fitting and scaling await acceptance.

## Alternate mpv session

Choose a trusted separately installed **mpv.exe**, then explicitly start an idle, paused owned session. A file or URL is opened only through its selected command. mpv renders in its own native window; it does not replace FluxPlayer's main LibVLC player or share its queue, position, effects or device settings. This is process/IPC integration, not embedded libmpv rendering or a second implementation of the main workspace.

The 100 controls cover source loading/append, queue operations, transport/exact seeking/frame steps/A–B/loops/chapters/editions/speed, volume/mute/tracks/external audio, primary/secondary subtitle tracks and typography, window/aspect/zoom/pan/rotation, picture adjustments, decoding, output backends, scaling/dither/debanding, display timing and color/tone-mapping assumptions. Each property is one capability even though both set and readback are available. Queue indices are zero-based; audio/video/subtitle IDs come from the explicit track/queue/device snapshot. Partial snapshot failures are reported per property.

Boolean, enum, numeric, color, file and URL controls validate their input before building a structured IPC command. The client does not accept an arbitrary mpv command string. Pair input is reserved for the two queue indices of a move operation. Local sources must exist; supported explicit media URL schemes are HTTP(S), RTSP and RTMP without embedded user credentials.

Startup-only controls include output backend, graphics API/context, font provider and requested audio layout/format/rate/exclusive mode. **Stage startup option** stores a validated choice in this panel. Close the current owned session and explicitly start it again to apply; staged choices survive that close/restart and are discarded when the panel itself is closed. Staging does not alter a running output device. Exclusive mode is a request, not proof of exclusive access, bit-perfect PCM or native DSD. Renderer/HDR/scaling options depend on mpv version, build, selected output and physical hardware; unsupported options must fail rather than be labeled working.

mpv is started without user configuration or automatic external-script loading. Arguments use `ProcessStartInfo.ArgumentList`; IPC uses a random Windows pipe name, request IDs and bounded JSON responses. Each command opens its own client pipe connection and ignores unrelated events within a cap; it is not a continuously polled observer. Starting while a session already runs refuses without stopping that session. Closing first requests quit and then attempts to terminate only the owned process tree, with bounded cleanup waits. Cleanup failures are reported/logged and are not a guarantee that every OS process termination succeeds.

An IPC success is command acceptance, not independent measurement of image/audio quality or hardware behavior. Canceled requests can have applied before their reply was lost. Read state before retrying a change; there is no automatic replay or rollback. No mpv executable was searched for, installed, launched or exercised during this development batch.

## Kodi integration

The embedded reference contains **184 concrete methods**, individually listed in the checklist. They cover Player, Playlist, AudioLibrary, VideoLibrary, PVR, Files, GUI, Input, Profiles, Settings, Favourites, Textures, Addons, Application, System, Database, JSONRPC and XBMC namespaces. Examples include remote track selection and seeking, library queries/detail updates, television guides/timers/recordings, playlist editing, navigation, profile/settings inspection and supported server maintenance. These are conditional actions on an existing Kodi instance, not built-in Kodi playback, tuner support or a local PVR engine in FluxPlayer.

Connect to a Kodi HTTPS root or loopback HTTP endpoint using optional session Basic credentials. The client uses the fixed `jsonrpc` endpoint and first reads its version. No request starts in a window constructor. Choose a namespace and method, then fill **Named parameters** as a JSON object. This is an advanced API data editor with required/optional parameter names and a generated starting template; it is not a shell or script editor. Complex filters, item unions, sort/limits, nested media objects and optional fields are supplied as named JSON data. Templates assist entry but do not automatically select meaningful library IDs or guarantee that a complex example is ready to send.

The client resolves captured type references/inheritance and checks parameter names, required fields, basic types, union alternatives, enums, numeric bounds, required nested fields and collection lengths. General request limits apply in addition to the reference schema. It is a bounded schema validator, not a general-purpose complete JSON Schema implementation; the installed Kodi server remains authoritative for its grammar, extra constraints, permissions and available API version. The captured upstream schema may expose methods not supported by an older target. Unknown methods are not added dynamically from an arbitrary URL or response.

ReadData methods use the read action; methods with other permissions use a reviewed change action. Confirmation identifies the selected target, method and parameters, with consequences specific to removal, power or addon execution where applicable. Remote data deletion/cleanup, addon execution, recording operations, profile changes and power controls require explicit requests; they never run as part of connecting or rendering Settings. No Kodi account or library was modified in development.

Responses must match the JSON-RPC request ID. RPC errors are failures even when HTTP succeeds. Timed-out/canceled writes can already have applied: the panel reports an uncertain outcome and never automatically retries or claims rollback. Session credentials are not written to profile data or included as connection fields in reports. Server-returned data may itself be private and is exported only through the user's new-copy report action.

## MPD integration

The 116 individually listed commands implement an additional Music Player Daemon text-session client under the original broad music-server/network/API requirements. **MPD is not RAAT, Roon Ready, NAA, AirPlay or Sonos compatibility.** The main local player is unaffected. MPD partition/output/volume commands control the remote daemon's devices; they do not certify physical multizone synchronization.

Enter a host, port and optional session password, then Connect. Plain TCP is restricted to an actually connected loopback address. For remote connections, select certificate-validating TLS and provide an MPD TLS proxy endpoint; there is no insecure certificate override or automatic fallback to plaintext. Conventional remote plaintext-only MPD installations therefore require a suitable proxy/tunnel before use with this panel.

The persistent, serialized connection retains client partition, protocol and tag-mask state between explicit commands. Choose an operation family and command, read its displayed signature, then supply **Ordered arguments** as a JSON array of strings. Each argument is independently quoted/escaped; embedded control characters/newlines are refused. Filter expressions, ranges, option names and option values are separate arguments according to MPD grammar. Server-side parsing and permissions remain authoritative; the panel is an advanced protocol interface, not a full query-language visual builder.

The catalog includes playback, queue/stored-playlist editing, library searches/counts/scans, storage mounts, stickers, output configuration, partitions and capability inspection. Protocol-version/build-dependent commands can be unavailable. Binary artwork commands, idle waits, raw password command execution, connection termination/server kill, server config dumping and channel message transmission/subscription operations are excluded. Authentication is handled by the session connection, outside the operation catalog. The retained text commands do not provide binary download or event-subscription support.

The buffered reader checks the MPD greeting, UTF-8 text, OK/ACK termination and line/response bounds. An unexpected binary response or malformed/error/canceled exchange resets the connection; it is not reused with an unread partial response. No request is automatically retried after reconnect. Writes may have applied before a disconnect; inspect server state before deliberately repeating them. Persistent-session state must be deliberately reselected after reconnecting. No MPD connection was opened during development.

## One hundred new processing algorithms

The existing ordered recipe editors now load 25 additional audio and 75 additional video algorithms from a bounded embedded catalog. Each new entry is a **distinct algorithm with a fixed graph**, not a separate count for every knob, preset or inspection call. Existing parameterized effects remain available alongside them. Source graphs are application-owned; importing a recipe references allowed IDs rather than supplying executable filter text.

Audio families include lo-fi/sample transforms, spectral dynamics, wavelet/non-local denoising, finite phrase loops/tail padding, psychoacoustic clipping, subsonic/ultrasonic and higher-order Butterworth filters, spectral tilt, optional Bauer crossfeed, acoustic distance delay, HDCD decoding, optional Rubber Band pitch processing, leading-silence trim, stereo phase processing, eighteen-band presence processing and virtual-bass synthesis folded explicitly into stereo. Mathematical derivative/integral and nonlinear processors deliberately alter the signal and may not be suitable for normal listening. Optional filters require corresponding FFmpeg build support; no component is installed automatically.

HDCD must occur first, before resampling/DSP, and requires appropriate decoded 16-bit source material with valid markers. Its output is then resampled to the recipe working format. This does not add native DSD, SACD extraction, bit-perfect output or a certified HDCD compatibility matrix. Virtual bass produces an intermediate LFE channel and explicitly mixes a bounded contribution back into the stereo output rather than silently dropping LFE.

Video families add spatial/temporal/morphological noise processing, color constancy/matrix/curves/exposure/vibrance, picture scopes and edge operators, decoder motion-vector visualization, field/cadence transforms, finite repetition/order changes, fixed equirectangular panorama reprojection, reference overlays, pixel-art scaling and creative picture transforms. Motion vectors require decoder support. The panorama view assumes an equirectangular source rather than detecting its projection. BT.601/BT.709 conversion assumes the stated source matrix. Field transforms are offline image operations, not certified interlaced-container/hardware output. Flash-reduction processing is heuristic and is not a safety certification.

Rendering keeps the existing explicit new-copy jobs, source-version checks, staging, cancellation, bounded diagnostics, below-normal process priority and worker limits. Video is capped before processing and again after processing to at most 1920×1080/even dimensions; at most one pixel-art enlargement, two expensive denoisers and a combined declared 40-frame temporal window are accepted. A selected recipe can still be computationally heavy. The size/window rules are design bounds, not CPU/GPU/RAM measurements.

Both input and output duration are capped, including finite loop/padding effects: audio at most 600 seconds, video at most 120 seconds. Tail padding can be seen when the natural source ends before the requested output cap; it cannot extend past that cap. Results are new 48 kHz/24-bit stereo WAV/FLAC or H.264/AAC MP4 excerpts; earlier gradfun lossless-encoding policy is retained. Original files are preserved and output names cannot overwrite existing files. No rendering or filter availability probe was run for these algorithms.

## Resource and execution bounds

| Operation | Bound |
|---|---|
| Advanced request data | 64 KiB, nesting 16, at most 100 object fields / 200 array items |
| Kodi call | 45-second deadline, 8 MiB response, bounded parse depth |
| MPD call | 15-second deadline, 64 KiB line, 4 MiB / 20,000-line response |
| mpv start / command | 8-second pipe-start deadline / 5-second request deadline |
| mpv response | 1 MiB per response line; up to 256 unrelated events |
| Result display / export | 128 KiB display cap / 16 MiB new-copy JSON report |
| Recipe render | Existing two-hour cancellation deadline, bounded excerpt/output |

Schemas are embedded resources loaded when their panels/recipes are used. No new startup connections, automatic device scans, background polling, playback DSP hooks or downloaded runtime packages were added. MPD needs an existing daemon/proxy, Kodi an existing server, mpv a trusted separately installed executable and processing a compatible installed FFmpeg. These are distinct dependency paths; they do not change the authoritative LibVLC main-player session.

## Deferred acceptance

Later acceptance must compile Release x64; exercise every manifest operation against appropriate disposable backend fixtures; cover request/response shapes, references/unions/required fields, permission/version errors, lifecycle and cancellation, partial writes, reconnect state, credential handling, pipe event interleaving and process cleanup; verify mpv startup versus live controls, tracks/queues/output IDs and physical hardware/renderer behavior; independently decode and examine all 100 processing outputs, original source hashes, stale-source/collision cases, temporal/size/duration limits and optional-filter failures; benchmark startup and explicit jobs; and review Windows 11 light/dark, DPI, keyboard, screen fitting, categories and context access. None of these checks has been performed for F0681–F1180.

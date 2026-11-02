# Audio, video and scheduled playback

The thirty-capability batch is tracked in [THIRTY_FEATURES.md](THIRTY_FEATURES.md). Open the player settings to find each action in Audio, Video or Playback. Selected spatial, picture and fade actions are also available by right-clicking the player. Settings retain the existing Fluent controls, window chrome, theme resources and Mica surfaces.

## Native audio processing

Open **Settings > Audio > Equalization and tone > Parametric equalizer**. The same processing window offers five groups: Parametric EQ, Tone and stereo, Dynamics, Time effects and Channel controls. Enable the master switch and the individual effects, then choose Apply. Apply saves validated preferences before changing playback. Bypass all saves the master switch off; Reset controls changes the draft until Apply.

| Group | Processing |
|---|---|
| Parametric EQ | Ten independent peaking filters: 20–20,000 Hz, ±18 dB, Q 0.1–12. A zero-gain or above-Nyquist band is bypassed. |
| Tone and stereo | Bass shelf, low-pass headphone crossfeed, balance, mid/side width and stereo center reduction. |
| Dynamics | Linked peak-envelope compressor with threshold, ratio, attack, release and makeup; linked instantaneous sample-peak limiter. |
| Time effects | Echo, short multi-tap reverb, flanger and chorus. |
| Channel controls | Gain −24…+12 dB and delay 0…200 ms for logical channels 1–8. |

These effects use the native DSP module and are disabled by default. Supported effect bounds are up to eight decoded channels and 192 kHz; actual decoder/output support varies. Stereo operations use the first two logical channels and leave mono unaffected. Physical speaker assignment follows the output. The existing graphic equalizer remains independent and can run alongside these effects. Its gain compensation covers its own graphic response: leave headroom when combining boosts. The limiter controls sample peaks; it does not guarantee intersample or acoustic peak containment. Center reduction subtracts common stereo content and can remove instruments along with vocals.

The native processing order is graphic EQ, parametric EQ/bass shelf and preamp, channel gain/delay, crossfeed, center reduction, width, balance, time effects, compressor and limiter. The first explicit effect activation may reopen the audio-filter chain to allocate bounded time buffers; it retains playback position and paused state. Configuration updates use the existing shared-memory protocol. Processing does not introduce a managed audio callback or allocate on each audio block.

**Audio > Signal and measurements > Signal path and levels** shows configured stages and actual native acknowledgement, rate, logical channels, input/output block peaks and RMS. Meters run only while that window is open and describe the native DSP stage, before the output/mixer volume. They do not certify exclusive or bit-perfect device output.

## Analyze source audio

Open **Audio > Signal and measurements > Audio measurements**. Choose up to 100 local files per task to measure source RMS (dBFS), estimated true peak (dBTP), calculated track ReplayGain and estimated BPM. Analysis runs installed FFmpeg only on request; media tags remain unchanged. Export creates a new versioned JSON copy and refuses overwrite.

RMS/true peak/ReplayGain analyze the source audio; BPM uses at most the first 120 seconds of downmixed 11,025 Hz audio. Tempo is a 45–210 BPM onset/correlation estimate, can be uncertain or have rhythmic aliases, and returns no tempo for silence. The confidence number is a heuristic, not a calibrated probability. These measurements precede playback effects, volume and output-device processing. ReplayGain calculation does not automatically apply normalization or write tags.

## Media copies and conversion

The existing **Audio > Conversion > Batch audio converter** now offers 1–4 parallel jobs (default two). Each job retains the existing encoder options. Names are planned uniquely, including repeated source filenames. Cancel stops active jobs and retains completed copies.

**Audio > Conversion > Join audio** joins 2–32 local sources in the listed order. Move up/down controls the order; output is new 48 kHz stereo 24-bit WAV or lossless FLAC. Sources are decoded and resampled, so this is not bitstream concatenation or gapless album mastering.

**Video > Conversion > Video converter** provides H.264/AAC MP4 and VP9/Opus WebM. Mobile profiles create H.264/AAC MP4 bounded to 1280×720 or 1920×1080, preserve aspect ratio, avoid upscaling, use even dimensions and stereo 48 kHz audio. Only the first video/audio streams are used; extra tracks and subtitles are omitted. Jobs run at reduced process priority with bounded encoder threads. Outputs publish as new copies after completion; cancellation removes incomplete temporary copies. Codec availability depends on the installed FFmpeg build.

## Picture viewport and stereo sources

Open **Video > Picture and stereo > Zoom, pan and stereo view**. Choose full picture, either side-by-side eye or either top/bottom eye, zoom 1–8× and pan within the selected region. Half-resolution compensation restores the selected eye's aspect ratio; clear it for full-resolution stereo sources. Apply affects the current source. Reset restores the full picture; opening a source clears crop, zoom and eye selection.

These modes display one selected stereo eye on the regular video surface. They do not implement shutter glasses, stereoscopic hardware output or depth conversion. Vertical flip is available in Video orientation and the context menu. Rotation, mirroring, vertical flip, crop and eye aspect compensation are also reflected in exported snapshots. WPF presents a viewport of the shared frame memory without an extra per-frame crop copy.

## Scheduled playback and volume fades

Open **Playback > Scheduling > Playback schedule**. Enter a name, future local date/time and one of three actions: play a saved playlist, stop playback or fade volume. Playlist tasks replace the active queue and explicitly start the first available entry. Choose the saved playlist before scheduling. Cancel disarms a task; Remove deletes the task record after confirmation. Media and saved playlists are preserved.

Tasks run once while FluxPlayer remains open. They persist in the current profile's `playback-schedule.json`. Tasks are disarmed before execution to prevent duplicate runs after restart; a crash during execution therefore does not retry automatically. Tasks missed by more than five minutes are skipped. Ambiguous/skipped daylight-saving times are rejected. This does not wake the computer, run an OS service, shut down Windows or provide recurring alarms.

**Audio > Level and dynamics > Volume fade** changes player volume gradually over 1–300 seconds and preserves mute. A manual volume change or Stop cancels the active fade. Starting another fade replaces it. Closing the fade window lets an active fade continue; Cancel fade stops it explicitly. A timer is created only for armed schedule tasks; there is no new idle scheduler timer for an empty schedule.

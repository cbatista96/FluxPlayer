# Known limits and reporting / Límites y fallos

| Area | Current limit |
|---|---|
| Windows | The downloadable build is x64. Windows 11 materials and hardware acceleration depend on OS and driver support. |
| Playback | Compatibility depends on the input, LibVLC plugins and drivers. DRM playback and universal codec compatibility are outside the release claims. |
| Optional tools | FFmpeg, yt-dlp, Whisper, mpv and MKVToolNix are separate installations for the features that use them. |
| Connected features | Provider accounts, external APIs, physical receivers, ASIO, DSD, surround and HDR/calibration need independent validation on the actual setup. |
| Updates | GitHub downloads are updated manually. |
| mpv | The optional mpv acceptance pass stopped during the Windows desktop regression on the release machine. The local playback engine is LibVLC; mpv is a separately installed alternative. |

Search [existing issues](https://github.com/cbatista96/FluxPlayer/issues?q=is%3Aissue) before opening a report. Include FluxPlayer version, Windows build, distribution type, steps and expected behavior. The default crash log is `%LOCALAPPDATA%\FluxPlayer\logs\crash.log`. For a custom data directory use `Cache\logs\crash.log` under that directory. Review the log and redact private file paths, URLs or tokens before sharing it.

Si encuentras un fallo, indica la versión de FluxPlayer, la compilación de Windows, el tipo de instalación y los pasos para reproducirlo. Revisa el registro antes de adjuntarlo.

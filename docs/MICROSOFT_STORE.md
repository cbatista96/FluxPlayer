# Microsoft Store submission

FluxPlayer uses the **MSIX** path for its x64 WPF desktop application. A preview package has a development identity. Do not submit that package. Microsoft signs the public Store package after certification. The GitHub installer has its own independent signing status.

1. In [Partner Center](https://partner.microsoft.com/dashboard), create a Windows MSIX app and reserve the product name. In **Product identity**, copy the Package/Identity/Name, Package/Identity/Publisher and Package/Properties/PublisherDisplayName values exactly.
2. Build the desktop payload and package it with those values. Use a clean output directory. The resulting unsigned MSIX is for Partner Center.

   ```powershell
   pwsh -File tools/Build-Release.ps1 -OutputDirectory artifacts/store-candidate
   pwsh -File tools/Build-Store.ps1 -ReleaseDirectory artifacts/store-candidate `
     -IdentityName $storePackageName -Publisher $storePublisher -PublisherDisplayName $storePublisherDisplayName
   ```

3. Run [Windows App Certification Kit](https://learn.microsoft.com/windows/uwp/debug-test-perf/windows-app-certification-kit) on the final MSIX and test install, update, launch, file activation, playback, settings persistence and uninstall in a clean Windows VM. The development package returned overall PASS locally after adding PerMonitorV2 DPI awareness. The optional blocked executables test still flagged process APIs in WPF, .NET, VLC and FluxPlayer. The app invokes optional local tools for user-requested operations; explain this to reviewers.
4. Upload the MSIX and prepare English and Spanish listings using [the copy below](#listing-copy--texto-para-la-ficha) and the [screenshots](screenshots/). Enter the [privacy policy URL](https://github.com/cbatista96/FluxPlayer/blob/main/docs/PRIVACY.md), support URL, age rating questionnaire, and actual distribution settings. Do not claim universal format support, DRM support or that any hardware backend is certified.
5. Explain `runFullTrust` in the submission: FluxPlayer is a standard user WPF desktop player with native LibVLC and optional integrations; it plays user-selected files and URLs, uses local settings and can invoke configured local media tools. File associations let the user choose the app through Windows. Launch and core local playback do not require an account or external tools.
6. Submit for certification in Partner Center and track its outcome there.

[Microsoft Store MSIX requirements](https://learn.microsoft.com/windows/apps/publish/publish-your-app/app-package-requirements?pivots=store-installer-msix) · [Listing image requirements](https://learn.microsoft.com/windows/apps/publish/publish-your-app/screenshots-and-images)

## Listing copy / Texto para la ficha

| Field | English | Español |
|---|---|---|
| Name | FluxPlayer | FluxPlayer |
| Short description | Play music and video on Windows with playlists, subtitles, lyrics and audio controls. | Reproduce música y video en Windows con listas, subtítulos, letras y controles de audio. |
| Category suggestion | Music or multimedia; confirm the closest available option | Música o multimedia; confirmar la categoría disponible |
| Search terms | media player, music, video, subtitles, playlists, equalizer | reproductor multimedia, música, video, subtítulos, listas, ecualizador |

**English:** FluxPlayer is an open source Windows player with dedicated workspaces for music and video. Listen with album art, synchronized LRC lyrics, playlists and a graphic equalizer. Watch with external subtitles, timing controls, bookmarks and A–B repeat. Browse a local library and choose light or dark appearance. No FluxPlayer account is needed for local playback. Some conversion, transcription and connected features require separate tools or accounts.

**Español:** FluxPlayer es un reproductor de código abierto para Windows con espacios para música y video. Escucha con carátulas, letras LRC sincronizadas, listas y ecualizador gráfico. Mira videos con subtítulos externos, ajuste de tiempo, marcadores y repetición A–B. Explora la biblioteca local y elige un tema claro u oscuro. La reproducción local no requiere una cuenta. Algunas funciones de conversión, transcripción y conexión requieren herramientas o cuentas adicionales.

**Reviewer test:** Open FluxPlayer, use Ctrl+O to select a local MP4 or MP3, pause, seek and resume. Open Settings and switch between English and Spanish. Open the playlist and equalizer. Optional network services and external tools are unnecessary for this test.

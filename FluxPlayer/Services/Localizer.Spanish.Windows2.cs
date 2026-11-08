namespace FluxPlayer.Services;

// Spanish text for windows that are built in code (tools, converters, schedules, TV library...). Titles, labels and status messages.
public static partial class Localizer
{
    private static IReadOnlyDictionary<string, string> SpanishWindows2 => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["All genres"] = "Todos los géneros", ["Unknown genre"] = "Género desconocido", ["All decades"] = "Todas las décadas", ["Unknown year"] = "Año desconocido", ["All artists"] = "Todos los artistas", ["Unknown artist"] = "Artista desconocido",
        ["All albums"] = "Todos los álbumes", ["Unknown album"] = "Álbum desconocido", ["All labels"] = "Todas las etiquetas", ["No personal labels"] = "Sin etiquetas personales", ["All collections"] = "Todas las colecciones",

        ["{0} of {1} files · {2} favorites"] = "{0} de {1} archivos · {2} favoritos",

        // Dialog titles and common actions
        ["Will clear when saved"] = "Se borrará al guardar", ["Choose folder"] = "Elegir carpeta", ["Choose snapshot folder"] = "Elegir la carpeta de capturas",
        ["Open advanced CUE document"] = "Abrir un documento CUE avanzado", ["Loaded"] = "Cargado", ["Save a separate CUE copy"] = "Guardar una copia CUE aparte", ["Saved UTF-8 copy:"] = "Copia UTF-8 guardada:",
        ["Choose an output folder"] = "Elegir una carpeta de salida", ["Save a new audio mix"] = "Guardar una mezcla de audio nueva", ["Completed:"] = "Completado:", ["Zone:"] = "Zona:",
        ["Browse media folder"] = "Explorar carpeta de medios",

        // Audio measurements and processing
        ["Audio measurements · FluxPlayer"] = "Mediciones de audio · FluxPlayer", ["Audio measurements"] = "Mediciones de audio",
        ["Analyze local files on demand: RMS, estimated true peak, ReplayGain and estimated BPM. Tempo uses the first 120 seconds and can have half/double-time ambiguity. Media tags remain unchanged."] = "Analice archivos locales a petición: RMS, pico real estimado, ReplayGain y BPM estimado. El tempo usa los primeros 120 segundos y puede confundirse con la mitad o el doble. Las etiquetas del medio no se modifican.",
        ["FFmpeg runs only for an explicit analysis. Results describe source audio, before playback effects or volume."] = "FFmpeg se ejecuta solo en un análisis explícito. Los resultados describen el audio de origen, antes de los efectos o el volumen de reproducción.",
        ["Analyze local audio"] = "Analizar audio local", ["Choose up to 100 files per analysis batch."] = "Elija hasta 100 archivos por lote de análisis.",
        ["Analysis complete. ReplayGain is a calculated track value; BPM remains an estimate."] = "Análisis completado. ReplayGain es un valor de pista calculado; el BPM sigue siendo una estimación.",
        ["Analyze at least one file."] = "Analice al menos un archivo.", ["New measurement copy exported."] = "Copia de las mediciones exportada.", ["Analyze files…"] = "Analizar archivos…", ["Export new JSON copy…"] = "Exportar una copia JSON nueva…",
        ["Enable audio processing"] = "Activar el procesamiento de audio", ["Audio processing · FluxPlayer"] = "Procesamiento de audio · FluxPlayer", ["Audio processing"] = "Procesamiento de audio",
        ["Native effects. Disabled by default; Apply saves and updates processing. Leave headroom when combining boosts. Logical channel order follows the decoder/output."] = "Efectos nativos. Desactivados de forma predeterminada; Aplicar guarda y actualiza el procesamiento. Deje margen al combinar realces. El orden lógico de canales sigue al decodificador o la salida.",
        ["Bypass all"] = "Omitir todo", ["Reset controls"] = "Restablecer los controles", ["Current output:"] = "Salida actual:",
        ["Saved profile removed. Current audio settings are retained."] = "Perfil guardado quitado. Se conservan los ajustes de audio actuales.", ["The change could not be saved:"] = "No se pudo guardar el cambio:",
        ["Audio signal path · FluxPlayer"] = "Ruta de la señal de audio · FluxPlayer", ["Audio signal path"] = "Ruta de la señal de audio",
        ["Meters describe the most recent native DSP block before device volume/mixing. They remain at the last block while paused. Metering is requested only while this window is open."] = "Los medidores describen el último bloque DSP nativo antes del volumen y la mezcla del dispositivo. Se quedan en el último bloque mientras está en pausa. La medición solo se solicita mientras esta ventana está abierta.",
        ["Build the native DSP module to enable this layout."] = "Compile el módulo DSP nativo para activar esta distribución.",

        // Batch tags
        ["This job supports up to 10,000 files."] = "Esta tarea admite hasta 10 000 archivos.", ["Choose files for batch tag editing"] = "Elegir archivos para editar etiquetas por lotes", ["Add at least one file to this job."] = "Añada al menos un archivo a esta tarea.",
        ["Applying checked fields. The current write finishes safely before cancellation."] = "Aplicando los campos marcados. La escritura actual termina con seguridad antes de cancelar.",
        ["Save tag field template"] = "Guardar la plantilla de campos de etiquetas", ["Template saved. No media files were changed."] = "Plantilla guardada. No se modificó ningún archivo multimedia.",
        ["Load tag field template"] = "Cargar una plantilla de campos de etiquetas", ["Template loaded. Review the checked fields, then apply explicitly. No media files were changed."] = "Plantilla cargada. Revise los campos marcados y aplique de forma explícita. No se modificó ningún archivo multimedia.",
        ["Template operation canceled."] = "Operación de plantilla cancelada.", ["Select one file to read its tags. Reading does not mark fields for saving."] = "Seleccione un archivo para leer sus etiquetas. Leer no marca campos para guardar.",
        ["Tags could not be read from this file."] = "No se pudieron leer las etiquetas de este archivo.", ["Read"] = "Leer", ["Tag reading canceled."] = "Lectura de etiquetas cancelada.",
        ["Title, artist, album and genre are saved to the file when its format supports them."] = "El título, el artista, el álbum y el género se guardan en el archivo cuando su formato lo admite.", ["Save tags"] = "Guardar etiquetas",
        ["Open another file or remove this one from the playlist before editing it to release the file."] = "Abra otro archivo o quítelo de la lista antes de editarlo para liberar el archivo.", ["Advanced and selective tag editor…"] = "Editor de etiquetas avanzado y selectivo…",

        // Display patterns and capture
        ["Display patterns · FluxPlayer"] = "Patrones de pantalla · FluxPlayer", ["Fullscreen pattern (F)"] = "Patrón a pantalla completa (F)",
        ["Static SDR code-value patterns. F: fullscreen · Esc: return · Left/Right: pattern. White Wash is a solid white field; it does not repair panel damage. No monitor settings, HDR or ICC calibration are changed."] = "Patrones estáticos de valores de código SDR. F: pantalla completa · Esc: volver · Izquierda/Derecha: patrón. White Wash es un campo blanco sólido; no repara daños del panel. No se cambia ningún ajuste del monitor, HDR ni calibración ICC.",
        ["Saved microphone recording:"] = "Grabación del micrófono guardada:", ["Saved video-device recording:"] = "Grabación del dispositivo de vídeo guardada:", ["Saved desktop recording:"] = "Grabación del escritorio guardada:",
        ["Camera preview · FluxPlayer"] = "Vista previa de la cámara · FluxPlayer", ["Untested DirectShow preview ·"] = "Vista previa DirectShow sin probar ·",
        ["The camera could not be opened. Driver and device support remain unverified."] = "No se pudo abrir la cámara. La compatibilidad del controlador y del dispositivo no está verificada.",

        // Chapters and CUE
        ["The chapter limit has been reached."] = "Se alcanzó el límite de capítulos.", ["Open audio or video in the player first."] = "Abra primero un audio o vídeo en el reproductor.", ["Open audio or video first."] = "Abra primero un audio o vídeo.",
        ["Player navigation restored to the source's native chapters. The editor draft is retained."] = "La navegación del reproductor volvió a los capítulos nativos del origen. Se conserva el borrador del editor.",
        ["Open external chapters"] = "Abrir capítulos externos", ["Save a separate chapter copy"] = "Guardar una copia de capítulos aparte",
        ["Chapter copy saved. The imported file and media container are unchanged."] = "Copia de capítulos guardada. El archivo importado y el contenedor multimedia no se modificaron.",
        ["A CUE sheet supports up to 99 tracks."] = "Una hoja CUE admite hasta 99 pistas.", ["Choose an available local media file."] = "Elija un archivo multimedia local disponible.", ["Add CUE source files"] = "Añadir archivos de origen del CUE",
        ["Only the first 99 tracks can be added to a CUE sheet."] = "Solo se pueden añadir las 99 primeras pistas a una hoja CUE.", ["Open a local media file before adding its current position."] = "Abra un archivo multimedia local antes de añadir su posición actual.",
        ["Edit CUE sheet"] = "Editar hoja CUE", ["Opened the preserving editor for extended directives, comments or album metadata. The basic-table draft is unchanged."] = "Se abrió el editor conservador para directivas ampliadas, comentarios o metadatos del álbum. El borrador de la tabla básica no cambió.",
        ["Select a track with a valid time and an available source file."] = "Seleccione una pista con un tiempo válido y un archivo de origen disponible.", ["Correct the highlighted fields before saving."] = "Corrija los campos resaltados antes de guardar.",
        ["Add at least one track before saving."] = "Añada al menos una pista antes de guardar.", ["Save CUE sheet"] = "Guardar hoja CUE", ["Open CUE sheet"] = "Abrir hoja CUE", ["Double-click to play from the start of a track"] = "Doble clic para reproducir desde el inicio de una pista",

        // Catalog, TV library and credits
        ["Create a series to begin."] = "Cree una serie para empezar.", ["Banner unavailable:"] = "Banner no disponible:", ["Related people"] = "Personas relacionadas", ["Open related profile"] = "Abrir el perfil relacionado",
        ["Discography"] = "Discografía", ["Cast and production credits"] = "Reparto y créditos de producción", ["Select a profile."] = "Seleccione un perfil.", ["Choose"] = "Elegir",
        ["Select an entry to view its synopsis, artwork and credits."] = "Seleccione una entrada para ver su sinopsis, su carátula y sus créditos.", ["Artwork unavailable:"] = "Carátula no disponible:",
        ["TV library · FluxPlayer"] = "Biblioteca de TV · FluxPlayer", ["Fullscreen (F)"] = "Pantalla completa (F)", ["↑/↓ Browse · Enter Play · → Play button · ← Titles · F Fullscreen · Esc Return/close."] = "↑/↓ Explorar · Intro Reproducir · → Botón de reproducir · ← Títulos · F Pantalla completa · Esc Volver/cerrar.",
        ["No titles in this view"] = "No hay títulos en esta vista", ["Add local entries in Movies and series, or change the library filters."] = "Añada entradas locales en Películas y series, o cambie los filtros de la biblioteca.",
        ["Choose a title first."] = "Elija primero un título.", ["Playback started in the player:"] = "Reproducción iniciada en el reproductor:", ["Add titles in the movie/series library."] = "Añada títulos en la biblioteca de películas y series.",

        // Converter and production
        ["Audio converter · FluxPlayer"] = "Conversor de audio · FluxPlayer", ["Convert your music"] = "Convierta su música",
        ["Explicit values override the selected profile defaults. Raising sample rate or bit depth cannot recover information missing from the source. Opus uses a 48 kHz playback clock."] = "Los valores explícitos sustituyen los del perfil elegido. Subir la frecuencia de muestreo o la profundidad de bits no recupera información que falta en el origen. Opus usa un reloj de reproducción de 48 kHz.",
        ["Normalize to −16 LUFS (changes the signal and increases processing)"] = "Normalizar a −16 LUFS (modifica la señal y aumenta el procesamiento)", ["Parallel jobs"] = "Tareas en paralelo", ["Convert"] = "Convertir",
        ["Folder for converted files"] = "Carpeta de los archivos convertidos", ["Choose files and an output folder."] = "Elija archivos y una carpeta de salida.", ["Task canceled. Completed conversions have been retained."] = "Tarea cancelada. Se conservaron las conversiones completadas.",
        ["Saved a new multichannel master:"] = "Se guardó un nuevo máster multicanal:", ["Choose 3D LUT…"] = "Elegir LUT 3D…", ["Saved a new finished copy:"] = "Se guardó una nueva copia terminada:", ["Saved a new"] = "Se guardó un nuevo",
        ["Created:"] = "Creado:", ["Saved"] = "Guardado", ["Add files to saved playlist"] = "Añadir archivos a la lista guardada", ["Export a new playlist copy"] = "Exportar una copia nueva de la lista",
        ["Uses installed FFmpeg only when a task starts. Choose a new output filename."] = "Usa el FFmpeg instalado solo cuando empieza una tarea. Elija un nombre de archivo de salida nuevo.", ["Use up to 32 sources."] = "Use hasta 32 orígenes.", ["Save a new media copy"] = "Guardar una copia multimedia nueva",
        ["Processing a new copy…"] = "Procesando una copia nueva…", ["Saved a new processed copy:"] = "Se guardó una nueva copia procesada:", ["Saved a new processed video:"] = "Se guardó un nuevo vídeo procesado:",
        ["Selected effect parameters"] = "Parámetros del efecto seleccionado", ["Selected action parameters"] = "Parámetros de la acción seleccionada", ["No additional parameters."] = "Sin parámetros adicionales.",
        ["Gamepad: A play/pause · X stop · LB/RB previous/next · D-pad left/right seek 10 seconds · up/down volume. Input is read only while the player or this panel is active. Analog input and arbitrary controller mapping are not included."] = "Mando: A reproducir/pausar · X detener · LB/RB anterior/siguiente · cruceta izquierda/derecha avanza o retrocede 10 segundos · arriba/abajo volumen. La entrada solo se lee mientras el reproductor o este panel está activo. No incluye entrada analógica ni asignación arbitraria de mandos.",

        // Duplicates and organizing
        ["Ready to scan the current library."] = "Listo para analizar la biblioteca actual.", ["Scan library"] = "Analizar la biblioteca", ["Cancel scan"] = "Cancelar el análisis",
        ["Exact duplicate files · FluxPlayer"] = "Archivos duplicados exactos · FluxPlayer", ["Exact duplicate files"] = "Archivos duplicados exactos",
        ["Only equal-size candidates are hashed. This scan runs on demand and never deletes files."] = "Solo se calcula el hash de los candidatos del mismo tamaño. El análisis se ejecuta a petición y nunca elimina archivos.",
        ["Checking sizes before hashing candidates…"] = "Comprobando tamaños antes de calcular los hashes…", ["Scan canceled. No files were changed."] = "Análisis cancelado. No se modificó ningún archivo.",
        ["Organize media files · FluxPlayer"] = "Organizar archivos multimedia · FluxPlayer", ["Organize media files"] = "Organizar archivos multimedia",
        ["Preview metadata folders before moving files. Same drive/share only; loaded files are skipped and existing destinations are never replaced. Completed moves remain after cancellation; there is no undo."] = "Previsualice las carpetas de metadatos antes de mover archivos. Solo dentro de la misma unidad o recurso compartido; los archivos cargados se omiten y los destinos existentes nunca se reemplazan. Los movimientos completados permanecen tras cancelar; no se puede deshacer.",
        ["Move previewed files"] = "Mover los archivos previsualizados", ["Choose organization destination"] = "Elegir el destino de la organización", ["Working…"] = "Trabajando…",

        // Library window messages
        ["Widget"] = "Widget", ["Visible"] = "Visible", ["Connected to"] = "Conectado a", ["Fingerprinting"] = "Calculando huellas",
        ["Enter at least one label, one per line."] = "Escriba al menos una etiqueta, una por línea.", ["Saving personal labels… Close this window to cancel before the change is committed."] = "Guardando las etiquetas personales… Cierre esta ventana para cancelar antes de confirmar el cambio.",
        ["Label edit canceled before commit."] = "Edición de etiquetas cancelada antes de confirmarse.", ["Choose a specific library collection before adding or removing members."] = "Elija una colección concreta de la biblioteca antes de añadir o quitar miembros.",
        ["Select files to rate."] = "Seleccione archivos para valorar.", ["Add folder to library"] = "Añadir carpeta a la biblioteca", ["Scan canceled. Files already found have been retained."] = "Análisis cancelado. Se conservaron los archivos ya encontrados.",
        ["Select library files to edit their personal labels."] = "Seleccione archivos de la biblioteca para editar sus etiquetas personales.", ["Library backup saved."] = "Copia de seguridad de la biblioteca guardada.", ["Columns"] = "Columnas",
        ["Restore column view"] = "Restaurar la vista de columnas", ["Remove saved column view"] = "Quitar la vista de columnas guardada", ["Select files to preview organization into metadata folders."] = "Seleccione archivos para previsualizar su organización en carpetas de metadatos.",
        ["Restore library backup"] = "Restaurar copia de seguridad de la biblioteca", ["Merge files and smart playlists into this library. Current favorites and playback counts are preserved."] = "Combine archivos y listas inteligentes en esta biblioteca. Se conservan los favoritos y los contadores de reproducción actuales.",
        ["Also restore monitoring for available folders"] = "Restaurar también la vigilancia de las carpetas disponibles", ["Merge backup"] = "Combinar la copia de seguridad", ["Validating and merging backup…"] = "Validando y combinando la copia de seguridad…",
        ["Backup restore canceled."] = "Restauración de la copia cancelada.", ["Folder monitoring removed; its files remain in the library."] = "Vigilancia de la carpeta quitada; sus archivos siguen en la biblioteca.", ["No watched folders"] = "No hay carpetas vigiladas",
        ["Playlist saved. Opening it applies its rules to the current library."] = "Lista guardada. Al abrirla se aplican sus reglas a la biblioteca actual.", ["Save column view…"] = "Guardar la vista de columnas…", ["Reset columns and sorting"] = "Restablecer columnas y ordenación",
        ["Add selected to current collection"] = "Añadir la selección a la colección actual", ["Remove selected from current collection"] = "Quitar la selección de la colección actual", ["Export library backup…"] = "Exportar copia de seguridad de la biblioteca…",
        ["Organize selected files…"] = "Organizar los archivos seleccionados…", ["Restore backup (merge)…"] = "Restaurar copia de seguridad (combinar)…", ["Find exact duplicates…"] = "Buscar duplicados exactos…", ["Remove selected from library"] = "Quitar la selección de la biblioteca",

        // Listening services and statistics
        ["Reviewed local outbox"] = "Bandeja de salida local revisada", ["Remote listening data"] = "Datos de escucha remotos", ["Session authorized for"] = "Sesión autorizada para", ["Session identified as"] = "Sesión identificada como",
        ["Export a new statistics copy"] = "Exportar una copia nueva de las estadísticas", ["Open LRC lyrics"] = "Abrir letras LRC", ["Save a new LRC copy"] = "Guardar una copia LRC nueva",
        ["Export a new history copy"] = "Exportar una copia nueva del historial", ["History view exported:"] = "Vista del historial exportada:",

        // Native GPU, photos and slideshow
        ["Native GPU video and HDR display state · FluxPlayer"] = "Vídeo GPU nativo y estado de pantalla HDR · FluxPlayer",
        ["Separate native Direct3D11 session. Video bypasses the 8-bit WPF memory presenter; native subtitles remain in the GPU output. HDR transfer depends on source metadata, VLC/driver and display state. This is not a calibrated HDR or ICC acceptance result. Main playback stops when this session starts. Closing stops this session."] = "Sesión nativa Direct3D11 independiente. El vídeo evita el presentador de memoria WPF de 8 bits; los subtítulos nativos permanecen en la salida de la GPU. La transferencia HDR depende de los metadatos del origen, de VLC/controlador y del estado de la pantalla. No es un resultado de aceptación HDR o ICC calibrado. La reproducción principal se detiene al iniciar esta sesión. Al cerrar se detiene esta sesión.",
        ["Native session volume"] = "Volumen de la sesión nativa", ["Native subtitle track"] = "Pista de subtítulos nativa", ["Choose local file…"] = "Elegir archivo local…", ["Play source"] = "Reproducir el origen", ["Inspect display color state…"] = "Inspeccionar el estado de color de la pantalla…",
        ["Play GIF"] = "Reproducir GIF", ["Pause GIF"] = "Pausar GIF", ["Unable to display this photo:"] = "No se puede mostrar esta foto:", ["Pause slideshow"] = "Pausar la presentación", ["Play slideshow"] = "Iniciar la presentación",
        ["Zoom −"] = "Reducir −", ["Fit"] = "Ajustar", ["Zoom +"] = "Ampliar +", ["Rotate 90°"] = "Girar 90°",

        // Schedule and volume fade
        ["Playback schedule · FluxPlayer"] = "Programación de reproducción · FluxPlayer", ["Playback schedule"] = "Programación de reproducción",
        ["One-time or weekly playlist, stop, volume and alarm tasks. FluxPlayer must remain open. Missed occurrences older than five minutes are skipped. Higher priorities run last when times match; this does not wake the computer."] = "Tareas únicas o semanales de lista, detención, volumen y alarma. FluxPlayer debe permanecer abierto. Las ocurrencias perdidas de hace más de cinco minutos se omiten. Si coinciden las horas, las prioridades más altas se ejecutan al final; esto no reactiva el equipo.",
        ["Local date and time (HH:mm:ss)"] = "Fecha y hora locales (HH:mm:ss)", ["Repeat on weekdays (none = once)"] = "Repetir en estos días (ninguno = una vez)",
        ["Playlist tasks replace the current queue and start explicitly at their scheduled time. Native playback remains source-dependent."] = "Las tareas de lista reemplazan la cola actual y empiezan explícitamente a la hora programada. La reproducción nativa sigue dependiendo del origen.",
        ["Volume fade · FluxPlayer"] = "Fundido de volumen · FluxPlayer", ["Volume fade"] = "Fundido de volumen", ["Change playback volume gradually. Mute is preserved. Manual volume changes or Stop cancel the active fade."] = "Cambia el volumen de reproducción de forma gradual. El silencio se conserva. Los cambios manuales de volumen o Detener cancelan el fundido activo.",
        ["Duration (1–300 seconds)"] = "Duración (1–300 segundos)", ["Start fade"] = "Iniciar el fundido", ["Cancel fade"] = "Cancelar el fundido", ["Fading…"] = "Atenuando…", ["Fade finished or canceled."] = "Fundido terminado o cancelado.", ["Fade canceled."] = "Fundido cancelado.",
        ["Add scheduled task"] = "Añadir tarea programada", ["Cancel selected task"] = "Cancelar la tarea seleccionada", ["Remove selected task…"] = "Quitar la tarea seleccionada…", ["Show on toolbar"] = "Mostrar en la barra de herramientas",

        // Remote control, snapshots, spoken media, sonic discovery
        ["Keep this panel open while using the remote. Stopping it invalidates the pairing code. No arbitrary file access, source opening or media download is exposed."] = "Mantenga este panel abierto mientras usa el mando a distancia. Al detenerlo se invalida el código de vinculación. No se expone acceso arbitrario a archivos, apertura de orígenes ni descarga de medios.",
        ["No active connection."] = "No hay ninguna conexión activa.", ["Connecting…"] = "Conectando…", ["Save scheduled snapshots"] = "Guardar las capturas programadas",
        ["Use an interval of 1–3600 seconds and a limit of 1–10,000 images."] = "Use un intervalo de 1–3600 segundos y un límite de 1–10 000 imágenes.", ["Waiting for video playback and the first interval…"] = "Esperando la reproducción del vídeo y el primer intervalo…",
        ["Capture job stopped. Completed images have been retained."] = "Captura detenida. Se conservaron las imágenes completadas.", ["Capture stopped:"] = "Captura detenida:",
        ["Indexed audio"] = "Audio indexado", ["Similar results"] = "Resultados similares", ["Add audiobooks"] = "Añadir audiolibros", ["Retrieving"] = "Recuperando", ["Downloading"] = "Descargando",
        ["Import podcast subscriptions"] = "Importar suscripciones de podcasts", ["Export subscription copy"] = "Exportar una copia de las suscripciones", ["Results"] = "Resultados", ["Focus rules"] = "Reglas de enfoque", ["Facets / candidates"] = "Facetas / candidatos",

        // Subtitles
        ["Subtitle file to translate"] = "Archivo de subtítulos por traducir", ["Save the translated subtitles"] = "Guardar los subtítulos traducidos", ["Save the generated subtitles"] = "Guardar los subtítulos generados",
        ["Downloaded a validated SRT. The media changed or could not attach it:"] = "Se descargó un SRT validado. El medio cambió o no se pudo adjuntar:", ["Saved a new validated SRT copy:"] = "Se guardó una copia SRT validada nueva:",
        ["Profile loaded into the form. Choose Use this style, then restart FluxPlayer to apply it."] = "Perfil cargado en el formulario. Elija «Usar este estilo» y reinicie FluxPlayer para aplicarlo.", ["The style could not be saved:"] = "No se pudo guardar el estilo:",
        ["Choose a saved profile to remove."] = "Elija un perfil guardado para quitarlo.", ["Saved profile removed. The current default style is retained."] = "Perfil guardado quitado. Se conserva el estilo predeterminado actual.", ["The profile could not be removed:"] = "No se pudo quitar el perfil:",
        ["Open subtitles for timing correction"] = "Abrir subtítulos para corregir su sincronización", ["Live delay must be within 24 hours."] = "El retraso en directo debe estar dentro de 24 horas.", ["Enter a whole millisecond delay."] = "Escriba un retraso en milisegundos enteros.",
        ["Save corrected SRT copy"] = "Guardar la copia SRT corregida", ["Choose a different path to preserve the original subtitle file."] = "Elija otra ruta para conservar el archivo de subtítulos original.", ["Start video playback before loading the saved copy."] = "Inicie la reproducción del vídeo antes de cargar la copia guardada.",
        ["Corrected copy loaded; live delay reset to zero to avoid applying it twice."] = "Copia corregida cargada; el retraso en directo se restableció a cero para no aplicarlo dos veces.", ["Captions"] = "Subtítulos", ["Findings"] = "Hallazgos", ["Writing"] = "Escribiendo",

        // Television
        ["Programme guide"] = "Guía de programación", ["Recordings"] = "Grabaciones", ["Scheduled recordings"] = "Grabaciones programadas", ["Timeshift segments"] = "Segmentos de timeshift",

        // Video view
        ["Crop applied:"] = "Recorte aplicado:", ["Active preset:"] = "Ajuste activo:", ["Video view · FluxPlayer"] = "Vista de vídeo · FluxPlayer", ["Video view"] = "Vista de vídeo",
        ["Zoom and pan inside the current picture, or view one eye of a side-by-side / top-and-bottom source. This does not provide stereoscopic display hardware output."] = "Amplíe y desplace dentro de la imagen actual, o vea un ojo de un origen en paralelo o de arriba abajo. No ofrece salida estereoscópica por hardware.",
        ["Stereo source / eye"] = "Origen estéreo / ojo", ["Half-resolution stereo source (restore eye aspect ratio)"] = "Origen estéreo de media resolución (restaurar la relación de aspecto del ojo)", ["Apply view"] = "Aplicar la vista", ["Reset view"] = "Restablecer la vista",
        ["View applied. Zoom and stereo-eye selection reset when another source opens."] = "Vista aplicada. El zoom y la selección del ojo estéreo se restablecen al abrir otro origen.", ["Full picture restored."] = "Imagen completa restaurada."
    };
}

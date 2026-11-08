namespace FluxPlayer.Services;

// Spanish text for the secondary windows (library, editors, history, equalizer...). Same convention as the main catalog:
// the key is the English text exactly as written in the interface; "..." and "…" are interchangeable.
public static partial class Localizer
{
    private static IReadOnlyDictionary<string, string> SpanishWindows => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Now playing, main window, shared
        ["NOW PLAYING"] = "REPRODUCIENDO", ["Favorite"] = "Favorito", ["Equalizer (E)"] = "Ecualizador (E)", ["Close (Esc)"] = "Cerrar (Esc)",
        ["Remove shortcut"] = "Quitar el atajo", ["Discard changes (Esc)"] = "Descartar los cambios (Esc)", ["Made with"] = "Hecho con", ["in Miami, FL"] = "en Miami, FL",
        ["Engine:"] = "Motor:", ["Framework:"] = "Plataforma:",
        ["Italic is defined by the subtitle file. A global override is pending."] = "La cursiva la define el archivo de subtítulos. La sustitución global está pendiente.",
        ["Placement is defined by the subtitle file. A global override is pending."] = "La posición la define el archivo de subtítulos. La sustitución global está pendiente.",
        ["Assign shortcut"] = "Asignar atajo", ["Press a key combination…"] = "Pulse una combinación de teclas…", ["Esc to cancel"] = "Esc para cancelar",
        ["Settings — FluxPlayer"] = "Ajustes — FluxPlayer",

        // URL window
        ["Enter a stream or network file URL"] = "Escriba la URL de una transmisión o de un archivo de red",
        ["HTTP(S), HLS (.m3u8), RTSP, RTMP, MMS, UDP/RTP, FTP or SMB"] = "HTTP(S), HLS (.m3u8), RTSP, RTMP, MMS, UDP/RTP, FTP o SMB",
        ["Paste from clipboard (Ctrl+V)"] = "Pegar del portapapeles (Ctrl+V)",

        // Mini player
        ["FluxPlayer Mini"] = "FluxPlayer Mini", ["Return to the player (double-click)"] = "Volver al reproductor (doble clic)", ["Close mini player"] = "Cerrar el minirreproductor", ["Play / pause (Space)"] = "Reproducir / pausar (Espacio)",

        // Equalizer
        ["Equalizer — FluxPlayer"] = "Ecualizador — FluxPlayer", ["Layout"] = "Distribución", ["Independent native filters"] = "Filtros nativos independientes", ["Preset"] = "Ajuste preestablecido",
        ["Previous preset"] = "Ajuste anterior", ["Next preset"] = "Ajuste siguiente", ["Compensate gain"] = "Compensar la ganancia",
        ["Leave headroom for boosted bands. Extended layouts estimate the combined filter response. This is not a peak limiter."] = "Deja margen para las bandas realzadas. Las distribuciones ampliadas estiman la respuesta combinada de los filtros. No es un limitador de picos.",
        ["Reset to flat"] = "Restablecer a plano",
        ["Requested preamp (double-click: 0 dB). The ten-band engine caps effective gain at +7.96 dB. Gain compensation can reduce it further."] = "Preamplificación solicitada (doble clic: 0 dB). El motor de diez bandas limita la ganancia efectiva a +7,96 dB. La compensación de ganancia puede reducirla aún más.",
        ["PRE"] = "PRE", ["Double-click a band: 0 dB  ·  Arrow keys: ±0.5 dB"] = "Doble clic en una banda: 0 dB  ·  Teclas de flecha: ±0,5 dB",

        // Library
        ["Library · FluxPlayer"] = "Biblioteca · FluxPlayer", ["Your library"] = "Su biblioteca", ["Music, videos and favorites in one place"] = "Música, vídeos y favoritos en un solo lugar",
        ["Add folder…"] = "Añadir carpeta…", ["Watched folders"] = "Carpetas vigiladas",
        ["Search text or rules: artist:\"First Last\" label:\"Road trip\" format:flac duration:>3:00 plays:>2 added:>=2026-01-01. Use - to exclude."] = "Busque texto o reglas: artist:\"Nombre Apellido\" label:\"Viaje\" format:flac duration:>3:00 plays:>2 added:>=2026-01-01. Use - para excluir.",
        ["Entire library"] = "Toda la biblioteca", ["Recently played"] = "Reproducido recientemente", ["Most played"] = "Más reproducido", ["Not played"] = "Sin reproducir", ["Tags not analyzed"] = "Etiquetas sin analizar",
        ["Watch when adding"] = "Vigilar al añadir", ["Include child collections"] = "Incluir las colecciones secundarias", ["Manage collections…"] = "Administrar colecciones…",
        ["Save view as playlist…"] = "Guardar la vista como lista…", ["Remove saved playlist"] = "Quitar la lista guardada", ["Personal label"] = "Etiqueta personal", ["Clear browsing"] = "Borrar la exploración",
        ["Decade"] = "Década", ["Plays"] = "Reproducciones", ["Labels"] = "Etiquetas", ["Folder"] = "Carpeta", ["Size (bytes)"] = "Tamaño (bytes)", ["Added (UTC)"] = "Añadido (UTC)",
        ["Last played (UTC)"] = "Última reproducción (UTC)", ["Rating"] = "Valoración", ["Remaining"] = "Restante", ["Modified (UTC)"] = "Modificado (UTC)",
        ["Add a folder to get started. Files you play will also appear here."] = "Añada una carpeta para empezar. Los archivos que reproduzca también aparecerán aquí.",
        ["Rate…"] = "Valorar…", ["Read tags"] = "Leer etiquetas", ["Edit tags"] = "Editar etiquetas", ["Personal labels…"] = "Etiquetas personales…", ["Export playlist"] = "Exportar la lista",
        ["Library tools"] = "Herramientas de la biblioteca", ["Cancel task"] = "Cancelar la tarea", ["Loading library…"] = "Cargando la biblioteca…",
        ["Library collections · FluxPlayer"] = "Colecciones de la biblioteca · FluxPlayer", ["Library collections"] = "Colecciones de la biblioteca",
        ["Group indexed files without moving them. Collections can contain child collections; a file can belong to more than one."] = "Agrupe archivos indexados sin moverlos. Las colecciones pueden contener colecciones secundarias; un archivo puede pertenecer a varias.",
        ["Collection hierarchy"] = "Jerarquía de colecciones", ["Direct files"] = "Archivos directos", ["New collection"] = "Nueva colección", ["Save name and parent"] = "Guardar nombre y superior",
        ["Remove collection tree…"] = "Quitar el árbol de colecciones…", ["Choose a collection to rename or move, or start a new collection."] = "Elija una colección para cambiarle el nombre o moverla, o cree una nueva.",
        ["Personal labels · FluxPlayer"] = "Etiquetas personales · FluxPlayer", ["Personal labels"] = "Etiquetas personales", ["Current labels"] = "Etiquetas actuales",
        ["Labels to add or remove · one per line"] = "Etiquetas por añadir o quitar · una por línea",
        ["Up to 32 labels per file, 64 characters each. Labels are stored in your library and its backups. They do not edit the media file's metadata."] = "Hasta 32 etiquetas por archivo, de 64 caracteres cada una. Se guardan en su biblioteca y en sus copias de seguridad. No modifican los metadatos del archivo multimedia.",
        ["Add labels to selected files"] = "Añadir etiquetas a los archivos seleccionados", ["Remove these labels"] = "Quitar estas etiquetas",
        ["Existing labels are preserved when adding. Search with label:\"Road trip\" or save that rule as a smart playlist."] = "Al añadir se conservan las etiquetas existentes. Busque con label:\"Viaje\" o guarde esa regla como lista inteligente.",

        // Statistics and history
        ["Listening statistics · FluxPlayer"] = "Estadísticas de escucha · FluxPlayer", ["Listening statistics"] = "Estadísticas de escucha",
        ["Based on retained audio playback history. Enable recording in Settings > Library."] = "Basadas en el historial de reproducción de audio conservado. Active el registro en Ajustes > Biblioteca.",
        ["Group by"] = "Agrupar por", ["Days"] = "Días", ["Period (session start, UTC)"] = "Período (inicio de sesión, UTC)", ["All retained history"] = "Todo el historial conservado",
        ["Last 7 days"] = "Últimos 7 días", ["Last 30 days"] = "Últimos 30 días", ["Last 90 days"] = "Últimos 90 días", ["Sessions"] = "Sesiones", ["Listening time"] = "Tiempo de escucha",
        ["Export this view to a new CSV…"] = "Exportar esta vista a un CSV nuevo…", ["Loading…"] = "Cargando…",
        ["Playback history · FluxPlayer"] = "Historial de reproducción · FluxPlayer",
        ["Recording is controlled in Settings > Library. Records describe sessions, not complete listens."] = "El registro se controla en Ajustes > Biblioteca. Los registros describen sesiones, no escuchas completas.",
        ["Search title, artist or recorded path"] = "Buscar por título, artista o ruta registrada", ["Search history"] = "Buscar en el historial", ["Started (UTC)"] = "Inicio (UTC)", ["Listened"] = "Escuchado", ["State"] = "Estado",
        ["Play from start"] = "Reproducir desde el principio", ["Resume selected"] = "Reanudar la selección", ["Export view copy…"] = "Exportar una copia de la vista…", ["Clear all history…"] = "Borrar todo el historial…",
        ["Loading recorded sessions…"] = "Cargando las sesiones registradas…",

        // Saved playback sessions
        ["Saved playback sessions · FluxPlayer"] = "Sesiones de reproducción guardadas · FluxPlayer", ["Saved playback sessions"] = "Sesiones de reproducción guardadas",
        ["Save the queue, selected item, position, speed, repeat and shuffle. Restoring replaces the current queue; unavailable local files are skipped."] = "Guarde la cola, el elemento seleccionado, la posición, la velocidad, la repetición y el modo aleatorio. Al restaurar se reemplaza la cola actual; los archivos locales no disponibles se omiten.",
        ["Search session names"] = "Buscar nombres de sesión", ["Search saved session names"] = "Buscar nombres de sesiones guardadas", ["Session"] = "Sesión", ["Entries"] = "Elementos",
        ["Save current as…"] = "Guardar la actual como…", ["Replace selected…"] = "Reemplazar la seleccionada…", ["Restore queue"] = "Restaurar la cola", ["Restore and play"] = "Restaurar y reproducir", ["Remove session…"] = "Quitar la sesión…",

        // Scheduled snapshots
        ["Scheduled snapshots · FluxPlayer"] = "Capturas programadas · FluxPlayer", ["Scheduled snapshots"] = "Capturas programadas",
        ["Capture native video frames while video is playing. The job stops at the selected limit or when you close this window."] = "Capture fotogramas nativos mientras se reproduce el vídeo. La tarea se detiene al llegar al límite elegido o al cerrar esta ventana.",
        ["Interval (seconds)"] = "Intervalo (segundos)", ["Maximum images"] = "Máximo de imágenes", ["Save folder"] = "Carpeta de destino", ["Start capture job"] = "Iniciar la captura",
        ["Ready. No capture job is running."] = "Listo. No hay ninguna captura en curso.",

        // Spoken media
        ["Spoken media · FluxPlayer"] = "Medios hablados · FluxPlayer", ["Feed address (HTTP or HTTPS)"] = "Dirección de la fuente (HTTP o HTTPS)", ["Subscribe"] = "Suscribirse", ["OPML…"] = "OPML…",
        ["Refresh feed"] = "Actualizar la fuente", ["Unsubscribe…"] = "Cancelar la suscripción…", ["Search titles"] = "Buscar títulos", ["Search spoken media"] = "Buscar medios hablados",
        ["All episodes"] = "Todos los episodios", ["Not listened"] = "Sin escuchar", ["Downloaded"] = "Descargados", ["Refresh view"] = "Actualizar la vista", ["Episode"] = "Episodio",
        ["Published (UTC)"] = "Publicado (UTC)", ["Offline"] = "Sin conexión", ["Add audio files…"] = "Añadir archivos de audio…", ["Restart"] = "Reiniciar", ["Speed…"] = "Velocidad…",
        ["Bookmarks…"] = "Marcadores…", ["Completion…"] = "Finalización…", ["Remove from shelf…"] = "Quitar de la estantería…", ["Play / resume"] = "Reproducir / reanudar",
        ["Download episode"] = "Descargar el episodio", ["Listening state…"] = "Estado de escucha…", ["Remove download…"] = "Quitar la descarga…",

        // Saved media (catalog)
        ["Saved media · FluxPlayer"] = "Medios guardados · FluxPlayer", ["New…"] = "Nuevo…", ["Import playlist…"] = "Importar lista…", ["Favorites only"] = "Solo favoritos", ["Save name"] = "Guardar el nombre",
        ["Lock playlist"] = "Bloquear la lista", ["Stream address (HTTP, HTTPS, RTSP or MMS)"] = "Dirección de la transmisión (HTTP, HTTPS, RTSP o MMS)", ["Country or region"] = "País o región",
        ["Append current queue"] = "Añadir la cola actual", ["Remove selected entries"] = "Quitar los elementos seleccionados", ["Sort by title"] = "Ordenar por título", ["Remove exact duplicates"] = "Quitar duplicados exactos",
        ["Path or stream"] = "Ruta o transmisión", ["Add to playback queue"] = "Añadir a la cola de reproducción", ["Export new playlist copy…"] = "Exportar una copia nueva de la lista…", ["Remove saved item…"] = "Quitar el elemento guardado…",

        // Folder browser
        ["Browse media folder · FluxPlayer"] = "Explorar carpeta de medios · FluxPlayer", ["Browse media folders"] = "Explorar carpetas de medios",
        ["Open a folder to browse supported media. Double-click a folder to enter it or a file to play it."] = "Abra una carpeta para explorar los medios compatibles. Haga doble clic en una carpeta para entrar o en un archivo para reproducirlo.",
        ["Choose folder…"] = "Elegir carpeta…", ["Up one folder"] = "Subir una carpeta", ["All media"] = "Todos los medios", ["Filter media filenames; folders remain visible"] = "Filtrar nombres de archivos de medios; las carpetas siguen visibles",
        ["Open / Play selected"] = "Abrir / reproducir la selección", ["Add selected to playlist"] = "Añadir la selección a la lista", ["Add all visible media"] = "Añadir todos los medios visibles", ["Choose a media folder."] = "Elija una carpeta de medios.",

        // Subtitles
        ["Subtitle styles · FluxPlayer"] = "Estilos de subtítulos · FluxPlayer", ["Subtitle style profiles"] = "Perfiles de estilo de subtítulos",
        ["Save default text-subtitle styles for different viewing conditions. Restart FluxPlayer to apply a changed style. Embedded styling and bitmap subtitles can override these defaults."] = "Guarde estilos predeterminados de subtítulos de texto para distintas condiciones de visionado. Reinicie FluxPlayer para aplicar un estilo modificado. El estilo incrustado y los subtítulos de mapa de bits pueden sustituir estos valores.",
        ["Profile name"] = "Nombre del perfil", ["Remove profile"] = "Quitar el perfil", ["Font family"] = "Familia de fuente", ["Font size in video pixels (0 = automatic)"] = "Tamaño de fuente en píxeles de vídeo (0 = automático)",
        ["Text color (#RRGGBB)"] = "Color del texto (#RRGGBB)", ["Outline color (#RRGGBB)"] = "Color del contorno (#RRGGBB)", ["Outline thickness (0–10% of font size)"] = "Grosor del contorno (0–10 % del tamaño de fuente)",
        ["Bottom margin in video pixels (0–500)"] = "Margen inferior en píxeles de vídeo (0–500)", ["Prefer bold text"] = "Preferir texto en negrita", ["Text shadow"] = "Sombra del texto", ["Use this style"] = "Usar este estilo",
        ["Choose a saved profile or enter a name to save a new one. Up to 32 profiles."] = "Elija un perfil guardado o escriba un nombre para guardar uno nuevo. Hasta 32 perfiles.",
        ["Subtitle timing · FluxPlayer"] = "Sincronización de subtítulos · FluxPlayer", ["Subtitle timing"] = "Sincronización de subtítulos",
        ["Adjust the current subtitle delay live, or correct an external file and save a separate SRT copy. Exported text does not retain ASS/VTT styling."] = "Ajuste el retraso actual de los subtítulos en directo, o corrija un archivo externo y guarde una copia SRT aparte. El texto exportado no conserva el estilo ASS/VTT.",
        ["Load original in player"] = "Cargar el original en el reproductor", ["SRT · VTT · ASS · SSA"] = "SRT · VTT · ASS · SSA", ["Live delay"] = "Retraso en directo", ["Apply delay"] = "Aplicar el retraso",
        ["Video playhead"] = "Posición del vídeo", ["Correction for the exported copy"] = "Corrección de la copia exportada", ["Offset (ms)"] = "Desfase (ms)", ["Time scale (%)"] = "Escala de tiempo (%)",
        ["Update table"] = "Actualizar la tabla", ["Reset correction"] = "Restablecer la corrección", ["Set first anchor at playhead"] = "Fijar el primer ancla en la posición actual",
        ["Set second anchor at playhead"] = "Fijar el segundo ancla en la posición actual", ["Seek selected original cue"] = "Ir al subtítulo original seleccionado",
        ["Select a cue and capture its matching video time. Two anchors calculate drift and offset."] = "Seleccione un subtítulo y capture su tiempo de vídeo correspondiente. Dos anclas calculan la deriva y el desfase.",
        ["Original start"] = "Inicio original", ["Adjusted start"] = "Inicio ajustado", ["Adjusted end"] = "Fin ajustado", ["Text"] = "Texto", ["Save corrected SRT copy…"] = "Guardar copia SRT corregida…",
        ["Load saved copy in player"] = "Cargar la copia guardada en el reproductor", ["Open a subtitle file to begin. Positive delay shows subtitles later."] = "Abra un archivo de subtítulos para empezar. Un retraso positivo muestra los subtítulos más tarde.",

        // Lyrics
        ["Lyrics editor · FluxPlayer"] = "Editor de letras · FluxPlayer", ["Lyrics editor"] = "Editor de letras",
        ["Edit synchronized LRC text. Apply a session preview or save a new copy; existing files are preserved."] = "Edite el texto LRC sincronizado. Aplique una vista previa de la sesión o guarde una copia nueva; los archivos existentes se conservan.",
        ["Open LRC…"] = "Abrir LRC…", ["Load current sidecar"] = "Cargar el archivo asociado actual", ["Insert current time"] = "Insertar el tiempo actual", ["Save LRC copy…"] = "Guardar copia LRC…",
        ["Use [00:01.250] before each lyric. Offset metadata and repeated timestamps are supported."] = "Use [00:01.250] antes de cada verso. Se admiten metadatos de desfase y marcas de tiempo repetidas.",

        // Video picture
        ["Video picture · FluxPlayer"] = "Imagen de vídeo · FluxPlayer", ["Left (pixels)"] = "Izquierda (píxeles)", ["Top (pixels)"] = "Arriba (píxeles)", ["Width (pixels)"] = "Ancho (píxeles)", ["Height (pixels)"] = "Alto (píxeles)",
        ["Apply crop"] = "Aplicar el recorte", ["Reset crop"] = "Restablecer el recorte", ["Read current video size"] = "Leer el tamaño actual del vídeo",
        ["The crop applies to the current source only and resets when another source opens. Coordinates refer to the decoded source picture."] = "El recorte se aplica solo al origen actual y se restablece al abrir otro. Las coordenadas se refieren a la imagen decodificada del origen.",
        ["Saved preset"] = "Ajuste guardado", ["Use selected"] = "Usar el seleccionado", ["Preset name"] = "Nombre del ajuste", ["Save and use"] = "Guardar y usar", ["Neutral picture"] = "Imagen neutra", ["Remove preset…"] = "Quitar el ajuste…",

        // Tag, chapter, CUE and rename tools
        ["Advanced CUE editor · FluxPlayer"] = "Editor CUE avanzado · FluxPlayer", ["Advanced CUE editor"] = "Editor CUE avanzado",
        ["Preserve CD-TEXT, gaps, flags, extra indexes, comments and unknown directives. Copies keep references pointing to their original files."] = "Conserva CD-TEXT, pausas, indicadores, índices adicionales, comentarios y directivas desconocidas. Las copias mantienen las referencias a sus archivos originales.",
        ["Open CUE…"] = "Abrir CUE…", ["UTF-8 / Unicode BOM"] = "UTF-8 / BOM Unicode", ["Validate and list tracks"] = "Validar y listar las pistas", ["Play selected INDEX 01"] = "Reproducir el INDEX 01 seleccionado",
        ["Save UTF-8 copy…"] = "Guardar copia UTF-8…", ["Mode"] = "Modo", ["INDEX 01"] = "INDEX 01",
        ["Navigation uses AUDIO INDEX 01 only. Gaps/flags and extra indexes are retained for editing; they do not enable CD burning, silence synthesis or gapless playback."] = "La navegación usa solo el INDEX 01 de AUDIO. Las pausas, los indicadores y los índices adicionales se conservan para editar; no habilitan la grabación de CD, la síntesis de silencio ni la reproducción sin pausas.",
        ["Open a CUE document to begin. Saves always create a separate copy."] = "Abra un documento CUE para empezar. Al guardar siempre se crea una copia aparte.",
        ["Output profiles · FluxPlayer"] = "Perfiles de salida · FluxPlayer", ["Audio output profiles"] = "Perfiles de salida de audio",
        ["Keep a volume, 10/15/20/31-band equalizer, preamp, gain compensation and audio delay for each output. Adjust them in the player first, then save here."] = "Conserve un volumen, un ecualizador de 10/15/20/31 bandas, la preamplificación, la compensación de ganancia y el retraso de audio para cada salida. Ajústelos primero en el reproductor y guárdelos aquí.",
        ["Apply saved profiles when choosing an output and at startup"] = "Aplicar los perfiles guardados al elegir una salida y al iniciar", ["Refresh devices"] = "Actualizar los dispositivos", ["Saved settings"] = "Ajustes guardados",
        ["Profiles are saved explicitly. Later adjustments do not overwrite them. System default follows Windows' current default output; physical-device detection is refreshed on demand. ReplayGain and other effects retain their global settings."] = "Los perfiles se guardan de forma explícita. Los ajustes posteriores no los sobrescriben. «Predeterminado del sistema» sigue la salida predeterminada actual de Windows; la detección de dispositivos físicos se actualiza a petición. ReplayGain y los demás efectos conservan sus ajustes globales.",
        ["Use output and saved profile"] = "Usar la salida y el perfil guardado", ["Save current settings"] = "Guardar los ajustes actuales", ["Remove saved profile"] = "Quitar el perfil guardado",
        ["Choose an output to inspect its profile. Up to 64 profiles."] = "Elija una salida para ver su perfil. Hasta 64 perfiles.",
        ["Batch tag editor · FluxPlayer"] = "Editor de etiquetas por lotes · FluxPlayer", ["Edit tags in batches"] = "Editar etiquetas por lotes",
        ["Check the fields to change. Unchecked fields are kept; a checked empty field clears its tag. Changes are written to the original files when their format supports them."] = "Marque los campos que desea cambiar. Los desmarcados se conservan; un campo marcado y vacío borra su etiqueta. Los cambios se escriben en los archivos originales cuando su formato lo admite.",
        ["Change title for every file"] = "Cambiar el título de todos los archivos", ["Change artist"] = "Cambiar el artista", ["Change album"] = "Cambiar el álbum", ["Change genre"] = "Cambiar el género",
        ["Advanced metadata fields"] = "Campos de metadatos avanzados", ["Custom embedded fields (MP3 / FLAC / Ogg / Opus)"] = "Campos personalizados incrustados (MP3 / FLAC / Ogg / Opus)",
        ["Apply these custom fields to every file"] = "Aplicar estos campos personalizados a todos los archivos",
        ["One NAME=value per line. NAME= clears only that field. Names use uppercase letters, digits and underscores. Stored with a FLUXPLAYER_ prefix; other fields are preserved."] = "Un NOMBRE=valor por línea. NOMBRE= borra solo ese campo. Los nombres usan mayúsculas, dígitos y guiones bajos. Se guardan con el prefijo FLUXPLAYER_; los demás campos se conservan.",
        ["Add files…"] = "Añadir archivos…", ["Remove selected from this job"] = "Quitar la selección de esta tarea", ["Read selected file tags"] = "Leer las etiquetas del archivo seleccionado",
        ["Save field template…"] = "Guardar plantilla de campos…", ["Load field template…"] = "Cargar plantilla de campos…", ["File"] = "Archivo", ["Result"] = "Resultado",
        ["Apply checked fields"] = "Aplicar los campos marcados", ["Cancel after current file"] = "Cancelar tras el archivo actual", ["Add local files and choose the fields to change."] = "Añada archivos locales y elija los campos que desea cambiar.",
        ["Chapter editor · FluxPlayer"] = "Editor de capítulos · FluxPlayer", ["Chapter editor"] = "Editor de capítulos",
        ["Load or create navigation points for one audio or video timeline. Saved chapter files leave the media container unchanged."] = "Cargue o cree puntos de navegación para una línea de tiempo de audio o vídeo. Los archivos de capítulos guardados no modifican el contenedor multimedia.",
        ["Open chapter file…"] = "Abrir archivo de capítulos…", ["Copy player chapters"] = "Copiar los capítulos del reproductor", ["Add current position"] = "Añadir la posición actual", ["Add row"] = "Añadir fila", ["Remove row"] = "Quitar fila",
        ["Find"] = "Buscar", ["Find a chapter by title or timestamp"] = "Buscar un capítulo por título o marca de tiempo", ["Search chapters"] = "Buscar capítulos", ["Start (hh:mm:ss.mmm)"] = "Inicio (hh:mm:ss.mmm)", ["Sort by time"] = "Ordenar por tiempo",
        ["Seek selected in player"] = "Ir a la selección en el reproductor", ["Apply to current media"] = "Aplicar al medio actual", ["Restore native chapters"] = "Restaurar los capítulos nativos", ["Save chapter copy…"] = "Guardar copia de capítulos…",
        ["OGM simple text or Matroska XML. Flat visible chapters from one edition; nested/ordered/linked chapter playback and container metadata editing remain separate. Applying is for this playback session."] = "Texto simple OGM o XML de Matroska. Capítulos visibles planos de una edición; la reproducción de capítulos anidados, ordenados o enlazados y la edición de metadatos del contenedor son aparte. Al aplicar, solo vale para esta sesión de reproducción.",
        ["Edit start times and titles, then apply or save a separate chapter file. Up to 10,000 chapters."] = "Edite los tiempos de inicio y los títulos; después aplique o guarde un archivo de capítulos aparte. Hasta 10 000 capítulos.",
        ["CUE sheet editor · FluxPlayer"] = "Editor de hojas CUE · FluxPlayer", ["CUE sheet editor"] = "Editor de hojas CUE",
        ["Create and edit audio tracks. Times use minutes:seconds:frames at 75 frames per second."] = "Cree y edite pistas de audio. Los tiempos usan minutos:segundos:fotogramas a 75 fotogramas por segundo.",
        ["Start (mm:ss:ff)"] = "Inicio (mm:ss:ff)", ["Load CUE…"] = "Cargar CUE…", ["Advanced editor…"] = "Editor avanzado…", ["Play selected track"] = "Reproducir la pista seleccionada", ["Save as…"] = "Guardar como…",
        ["The basic table writes audio FILE/TRACK/TITLE/PERFORMER/INDEX 01. Loading extended directives opens the advanced editor, which preserves them and exports a separate copy."] = "La tabla básica escribe FILE/TRACK/TITLE/PERFORMER/INDEX 01 de audio. Al cargar directivas ampliadas se abre el editor avanzado, que las conserva y exporta una copia aparte.",
        ["Add local files or load a CUE sheet. Up to 99 tracks."] = "Añada archivos locales o cargue una hoja CUE. Hasta 99 pistas.",
        ["Batch file rename · FluxPlayer"] = "Cambio de nombre de archivos por lotes · FluxPlayer", ["Batch file rename"] = "Cambio de nombre de archivos por lotes",
        ["Preview names before renaming. Files stay in their current folders and keep their extensions. Loaded files are skipped; existing destinations are never replaced."] = "Vea los nombres antes de renombrar. Los archivos permanecen en sus carpetas y conservan su extensión. Los archivos cargados se omiten; los destinos existentes nunca se reemplazan.",
        ["Sidecars, embedded tags and external playlists are not renamed or rewritten."] = "Los archivos asociados, las etiquetas incrustadas y las listas externas no se renombran ni se reescriben.",
        ["Choose destination…"] = "Elegir destino…", ["Folder template (below the chosen destination)"] = "Plantilla de carpeta (dentro del destino elegido)", ["Template"] = "Plantilla",
        ["Tokens: {filename}, {title}, {artist}, {album}, {albumartist}, {genre}, {year}, {track}, {disc}, {index}. Tags are read only when previewing."] = "Variables: {filename}, {title}, {artist}, {album}, {albumartist}, {genre}, {year}, {track}, {disc}, {index}. Las etiquetas solo se leen al previsualizar.",
        ["Preview names"] = "Previsualizar nombres", ["Rename previewed files"] = "Renombrar los archivos previsualizados", ["Cancel job"] = "Cancelar la tarea", ["Preview destination"] = "Destino de la vista previa",
        ["Add files or use the current playlist, then preview the names."] = "Añada archivos o use la lista actual y después previsualice los nombres."
    };
}

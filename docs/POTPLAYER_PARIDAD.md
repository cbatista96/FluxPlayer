# FluxPlayer frente a PotPlayer — paridad de menús y funciones

Base: el vídeo de grabación de pantalla (7 min 30 s, 74 cambios de escena extraídos) y el código de FluxPlayer en `main` (commit 5a9aeb7).
Leyenda: ✅ lo tenemos · 🟡 parcial · ❌ falta · ➖ no aplicable a este marco de interfaz.

## 0. Qué es fiable y qué no (leer primero)

- **Visto en el vídeo y confirmado:** menú principal; Subtítulos (Mostrar/ocultar, Sincronizar, Guardar, 3D, Buscando, Traducir, Crear desde audio, tiempo real); Vídeo (todos los submenús que se abren); Audio (menú principal, Procesador, Segundo dispositivo, Altavoces); Temas; Varios (Al finalizar, Al abrir archivos, Otros); Tamaño cuadro; Tamaño ventana; Preferencias (árbol completo y sólo las páginas **General** y **Subtítulos**).
- **No aparece en el vídeo:** Abrir ›, Álbum/favoritos ›, Filtros ›, Capítulo/marcador, Omitir, Lista reproducción, Reproducción continua, y los submenús de Audio distintos de los tres citados (Paso por, Formato/Muestreo, Modo salida, Volumen, Predefinido, Sincronizar, Efectos, Grabación, Tono). De esos sólo sé que existen por su nombre en el menú.
- **Sin confirmar:** las capturas del menú Reproducir (Ir a, Velocidad, Repetir AB) y Estilo subtítulo no se me mostraron, así que sus entradas en la sección 2 vienen de mi conocimiento de PotPlayer. Hay que contrastarlas con el programa real antes de implementar. Lo mismo vale para Emisión y las pestañas de Preferencias que no se abrieron.
- **No he ejecutado FluxPlayer** ni consultado fuentes web: el estado ✅/🟡/❌ sale de leer el código y los catálogos (`FEATURES.md`, `FUNCIONALIDADES.md`). Una búsqueda sin resultados puede ser un falso ❌; los marcados «(verificar)» son los dudosos.

## 1. Composición del menú

PotPlayer (vídeo): `Abrir archivo F3 · Abrir › · Emisión › · Álbum/favoritos › · Cerrar F4 | Reproducir › · Subtítulos › · Vídeo › · Audio › · Filtros › · Temas › · Varios › | Tamaño cuadro › · Proporción › · Tamaño ventana › · Pantalla completa Enter · Pantalla completa (Extendida) Ctrl+Enter | Preferencias F5 · ✓Lista reproducir F6 · Panel de control F7 · Info reproducción/sistema Ctrl+F1 · Acerca de F1 | Salir Alt+F4`.

FluxPlayer (`MainWindow.xaml:8`, `MainContextMenu`): Abrir archivo · URL · Recientes · Cerrar | Reproducir · Marcadores · Temporizador · Subtítulos · Vídeo · Audio · Biblioteca | Tamaño de ventana · Pantalla completa · Mini reproductor · Bloquear · Lista · Información · Preferencias · Acerca de · Salir.

Qué falta en la estructura: `Abrir ›` agrupado, `Emisión ›`, `Álbum/favoritos ›`, `Filtros ›`, `Temas ›`, `Varios ›`, `Tamaño cuadro ›`, `Proporción ›` como menú propio, `Pantalla completa (Extendida)`, `Panel de control` e `Info de la reproducción/sistema`.

**Propuesta:** reordenar `MainContextMenu` con los mismos 8 grupos y el mismo orden. Los grupos sin equivalente real (Filtros DirectShow) se sustituyen por los equivalentes nuestros (Procesamiento / Extensiones), no se dejan vacíos. Cada ítem nuevo lleva `SettingsAction.Category/Section` para aparecer también en Ajustes, como los actuales.

## 2. Reproducir

| PotPlayer | Estado | Dónde / cómo |
|---|---|---|
| Reproducir/pausa, anterior, siguiente | ✅ | — |
| Ir a: ±5 s, ±30 s, ±1 min, ±5 min | 🟡 (sólo paso pequeño y grande) | Añadir 4 pasos al submenú `Reproducir › Ir a`; acciones nuevas en `BuildHotkeysList` (`SettingsPanel.xaml.cs:371`) y en el `switch` de `MainWindow.xaml.cs:2016` |
| Cuadro anterior (D) | ❌ (sólo existe «Next frame») | LibVLC no retrocede cuadros: aproximar con seek −1/fps en pausa, o usar `MpvSession` |
| Cuadro clave anterior/siguiente | ❌ | Mismo límite; requiere índice de keyframes vía FFmpeg |
| Punto inicial, medio, 30 s antes del final | ❌ | Acciones nuevas, triviales |
| Subtítulo anterior/siguiente/actual | 🟡 (hay búsqueda con salto, no atajos) | Usar `SubtitleDocument` para saltar a cue vecino |
| **Ir al tiempo/cuadro (G)** | ❌ (verificar) | Diálogo pequeño nuevo; `Reproducir › Ir a` |
| Explorador de escenas (Mayús+Z) | 🟡 (sólo hoja de contactos exportable) | Ventana nueva con `ThumbnailService`; reutilizar `MediaArtifactsWindow` |
| Explorador de capítulos/marcadores (Alt+Z) | 🟡 | Hay `ChapterEditorWindow` y marcadores; unificar en una ventana |
| Velocidad ± y normal (Z/X/C) | ✅ | — |
| Modos de estiramiento de audio (calidad, FFmpeg, tono) | ❌ (verificar) | `Ajustes › Playback › Velocidad` |
| Repetir AB básico | ✅ | — |
| Mover puntos A/B 0,1 s; nº de repeticiones; recordar secciones | ❌ | `Ajustes › Playback › Chapters and navigation` |
| Repetir sección de subtítulo/marcador anterior-actual-siguiente | ❌ | Sobre `SubtitleDocument` y marcadores |
| Repetir (desactivado/lista/elemento) y aleatoria | ✅ | — |
| Recordar posición, ocultar ratón, cerrar al finalizar | ✅ | — |
| Impedir salvapantallas durante vídeo | 🟡 (la llamada existe en `DesktopMediaSession`) | Comprobar que se aplica también a vídeo local |
| Reproducir y enviar a la papelera | ❌ | `Microsoft.VisualBasic.FileIO` con papelera; con confirmación |
| Info de tiempo y miniatura al buscar; marcas en barra | ✅ / 🟡 | Marcas de capítulo en la barra: verificar |

## 3. Subtítulos

| PotPlayer | Estado | Dónde / cómo |
|---|---|---|
| Añadir/seleccionar idioma, mostrar/ocultar | ✅ | — |
| Buscar/editar/sincronizar | ✅ | `SubtitleWorkshopWindow`, `SubtitleTimingWindow` |
| Entrada de subtítulo manual (Alt+P) | ❌ | Diálogo para escribir una línea en el tiempo actual |
| Estilo: fuente, tamaño, color, contorno, sombra | ✅ | `SubtitleStyleWindow` (32 perfiles) |
| Tamaño ±, alinear izq/centro/der, mover en pantalla, espaciado (atajos Alt+…) | 🟡 | Sólo hay margen inferior. Añadir alineación, desplazamiento X/Y y espaciado a `SubtitleStyle` |
| Sincronización ±0,5/5/50 s | 🟡 | Hay retardo y corrección con dos anclas; faltan pasos y «restaurar desde la posición» |
| Convertir CPS entre fotogramas (29→23,976…) | ❌ | Opción en `SubtitleTimingWindow` |
| Guardar (mismo nombre, como…) | ✅ (SRT) | Añadir formatos SMI/ASS |
| Subtítulo 3D (SBS/TAB, posición) | ❌ | Sólo existe conversión de vídeo 3D, no subtítulo 3D |
| Buscar: OpenSubtitles | ✅ | `SubtitleDownloadsWindow` |
| Buscar: podnapisi, subir, buscar con Google | ❌ | `Ajustes › Subtitles › Online providers` |
| **Traducir (Bing, DeepL, Google, LibreTranslate, LM Studio, Papago, Yandex), idioma origen/destino, sólo traducción/arriba/abajo** | ❌ (hay doble idioma pero no traducción) | Servicio nuevo con claves del usuario; reutilizar `BilingualSubtitles` para mostrar arriba/abajo. Aviso de privacidad: el texto sale a un tercero |
| **Crear subtítulos desde audio (Whisper): modelo, idioma, auto al iniciar, en tiempo real** | ❌ | whisper.cpp o Whisper.net; `Ajustes › Subtitles › Recognition and extraction` (ya existe OCR de Windows) |
| Copiar subtítulo actual al portapapeles | ❌ | `Clipboard.SetText` |
| Bitmap fuera del vídeo, texto dentro, parte inferior, animaciones ASS, caja opaca, desvanecer | 🟡 (verificar cuáles) | `Ajustes › Subtitles › Subtitle appearance` |
| Página «General» de subtítulos: ignorar etiquetas HTML, forzar izquierda, cargar de carpeta, solo nombre exacto, eliminar sordos `[ ]`, recargar al cambiar archivo, etc. | ❌ (la mayoría) | Casillas en `Ajustes › Subtitles`; son baratas de implementar |

## 4. Vídeo

| PotPlayer | Estado | Dónde / cómo |
|---|---|---|
| Procesador de vídeo (VMR/EVR/madVR/D3D11/OpenGL) | ➖ | Usamos LibVLC → memoria. Ya hay `GpuRenderPolicy`; no copiar la lista |
| **Sombreador de píxel pre/post redimensionar (afilar, desenfoque, niveles, noche, etc.)** | ❌ (0 coincidencias) | LibVLC no admite shaders. Camino viable: la sesión **mpv** existente (`MpvSession`) con `--glsl-shaders`; `Ajustes › Video › Renderer` |
| Corrección de color ICC/YCgCo/HDR (ST 2084/2086) | 🟡 | `DisplayColorInspector` sólo lee; validación HDR pendiente (F0085) |
| Vídeo 3D: entrada SBS/TAB y vista de un ojo | ✅ | `VideoViewport` |
| 3D: salida anaglifo (≈15 variantes), entrelazado, gafas de obturación, NVIDIA 3D, intercambiar ojos, voltear, desplazar imagen | 🟡 (sólo exportación de archivo, F-VideoFinishing) | Presentación en vivo: shader/renderer propio; baja prioridad |
| MVC, detección automática de formato 3D | ❌ | Heurística por nombre (`3D-SBS`, `HSBS`) es barata |
| Redimensionar (algoritmo), desentrelazado por modo, extender/recortar, margen inferior | 🟡 | Desentrelazado es un único interruptor (`DeinterlaceEnabled`); añadir modos (yadif, bwdif…). Recorte manual ✅ |
| Efectos de vídeo | 🟡 | `VideoPictureWindow`: brillo/contraste; añadir saturación/tono con paso 1 % y atajos Q/W/E/R/T/Y/U/I/O |
| Rotación y volteo | ✅ | — |
| Capturar: PNG/JPG/BMP | ✅ | — |
| Capturar: **WEBP**, copiar cuadro al portapapeles (Ctrl+C), guardar como…, captura de pantalla, incluir mensaje y subtítulos, fecha/tiempo en el nombre | ❌ / 🟡 | `MediaService.Snapshots`; `Clipboard.SetImage` |
| Captura consecutiva (Ctrl+G) | 🟡 | Existe `SnapshotScheduleWindow` (por intervalo); falta el inicio/parada rápidos |
| Hoja de miniaturas (Alt+N) | ✅ | `MediaArtifactsWindow` |
| **Grabar vídeo en reproducción (Alt+C)** | 🟡 | Exportar A-B ✅ y grabación de cámara ✅; falta grabar el flujo actual con inicio/parada |
| AviSynth, VapourSynth | ❌ | Requiere el runtime externo; opcional, vía FFmpeg `-vf` |
| **Vídeo 360° (equirectangular, dual fisheye, cubemap, 4×90°)** | ❌ (0 coincidencias) | Sólo viable con mpv/shader; prioridad baja |
| Controles de color con paso 1 % | 🟡 | Ver «Efectos de vídeo» |

## 5. Audio

| PotPlayer | Estado | Dónde / cómo |
|---|---|---|
| Seleccionar pista | ✅ | — |
| Dispositivo de salida | ✅ | `SelectOutputDevice` |
| **Paso por / S/PDIF** (AC3, DTS, TrueHD) | ❌ | LibVLC `--spdif`; `Ajustes › Audio › Output and processing`. Sólo probable con hardware |
| Segundo dispositivo | 🟡 | `AudioZones` ya reproduce por zonas; unificar en este menú |
| Altavoces de salida (1.0…7.1, Pro Logic) | 🟡 | Hay 2.1/5.1/7.1 sobre WASAPI; falta Dolby Pro Logic y «igual que la entrada» |
| Formato/muestreo, modo exclusivo/WASAPI | ✅ | `AudioEndpointFormats`, `RealtimeAudioSession` |
| Auriculares virtuales / HRTF | ✅ | `Audio › Spatial processing` |
| Ecualizador, predefinidos | ✅ | — |
| Sincronización de audio | ✅ | — |
| Efectos de audio, tono, filtro integrado (normalizar, compresor) | ✅ | — |
| Grabación de audio | ✅ | — |
| Volumen del sistema separado del del reproductor | 🟡 | Verificar |

## 6. Tamaño de cuadro, proporción y ventana

| PotPlayer | Estado | Dónde / cómo |
|---|---|---|
| Proporciones | 🟡 (6 opciones: Auto/16:9/4:3/21:9/1:1/Estirar) | Añadir 16:10, 5:4, 2.35 y personalizada (ya hay `_customAspectChoice`) |
| Aumentar/disminuir, estirar H/V (Num 1–9) | 🟡 | El zoom y el desplazamiento existen en `VideoViewportWindow`; faltan atajos |
| Mover imagen (Ctrl+Num), márgenes por lado | 🟡 / ❌ | Los márgenes de pantalla son nuevos |
| Predefinidos de zoom (Zoom 1–3, aumentar ancho/alto 1–3) | ❌ | Lista editable con «Añadir/Editar…» |
| Recordar cambios de tamaño/posición del vídeo | ❌ | `AppSettings` |
| Tamaño de ventana: 0,5×/1×/1,5×/2× del vídeo | ✅ | 50–200 % |
| Tamaño de ventana: 30/45/60/75 % del escritorio | ❌ | Añadir al menú |
| Maximizar al escritorio / pantalla | ❌ | — |
| Pantalla completa (extendida / mantener relación / en otra pantalla) | 🟡 | Hay pantalla completa y varios monitores; faltan las 3 variantes |
| Aumentar/disminuir ventana (Ctrl+Alt+Num ±), mover ventana | ❌ | — |

## 7. Varios, Temas, Preferencias y Panel de control

| PotPlayer | Estado | Dónde / cómo |
|---|---|---|
| Predefinidos (perfiles de ajustes), administrar | ✅ | `--profile`, `ProfileDocument`; falta menú para cambiar de perfil sin reiniciar |
| **Idioma de interfaz (Mayús+L)** | ❌ (sólo inglés, F0006) | **Mayor brecha de paridad.** Pasar textos a recursos `.resx` y añadir español primero. Trabajo grande (≈ todo el XAML) |
| Siempre visible (nunca/al reproducir/siempre) | ✅ | — |
| Al finalizar la reproducción (apagar/suspender/hibernar/salir/permanecer) | 🟡 | Hay `OnEndCombo`; comprobar las 6 acciones y recordar la última |
| Al abrir archivos (seleccionado/similares/todos de la carpeta) | 🟡 | Verificar en `Ajustes › Playback` |
| Apagar a hora fija, tras 30 min de inactividad, tareas | ✅ | `PlaybackSchedule`, temporizador |
| Función táctil, recientes, desactivar atajos | ✅ / 🟡 | `TouchGestures` |
| Evitar arrastrar la ventana fuera de pantalla | ❌ | Limitar en `OnLocationChanged` |
| **OSD de información: Tab (funciones/etiquetas), Bloq Despl (archivo actual), Mayús+Tab (mínima)** | ❌ | Overlay sobre `MainWindow.Hud.cs`; datos de `MediaDiagnostics` |
| Copiar al portapapeles: nombre, ruta, ruta+nombre, tiempo transcurrido/total, formato | 🟡 (sólo ruta) | `Varios › Otros` |
| Vaciar historial de la barra de tareas | ❌ | Jump list |
| Ir al sitio web / comprobar actualización | ❌ | Publicación diferida a petición tuya (F0091/92) |
| **Temas** (skins `.dsf`, logo, color, opacidad, control en pantalla, diseño/estilo de menú) | ➖ parcial | Otro marco de interfaz: no se copia el formato `.dsf`. Equivalentes: temas Mica/negro/acrílico ✅, color de acento ✅ (F0288), imagen de fondo ✅ (F0289). Faltan: opacidad de ventana, logo propio al reproducir, estilo de menú |
| Panel de control (F7) | ❌ | Ventana-resumen de pistas, filtros y estado; reutilizar `MediaDiagnosticsWindow` |
| Info de reproducción/sistema (Ctrl+F1) | 🟡 | `MediaDiagnosticsWindow` y `RendererStatisticsWindow` |
| Lista con pestañas (Predeterminado / Este equipo / +) | 🟡 | Listas guardadas ✅; pestañas dentro de la lista: verificar |
| Panel Explorador junto a la lista | 🟡 | `FolderBrowserWindow` es ventana aparte, no panel acoplado |
| Atajos de teclado | 🟡 | **34 acciones** frente a cientos en PotPlayer. Cada función nueva de arriba debe añadir su acción |

**Árbol de Preferencias de PotPlayer** (visto en el vídeo) y dónde iría en FluxPlayer (`SettingsPanel`, categorías actuales: General, Playback, Audio, Video, Subtitles, Library, Files, Mouse, Keyboard):
General (Al iniciar, Teclado, Ratón, Táctil, XInput, Temas, Tema avanzado, Logo, Explorar, Mensajes OSD, Cuadro de diálogo) → `General` + `Keyboard` + `Mouse`; Reproducir → `Playback`; Subtítulos (Estilo, Desvanecer/3D/ubicación, Buscando, Buscando palabras, Explorador, Idioma/sincro) → `Subtitles`; Dispositivo → `Audio › Output`; Filtros → no aplicable; Vídeo → `Video`; Audio → `Audio`; Extensiones → `General › Extensions` ✅; Accesibilidad, Ubicación, Asociación, Configurar, Salva pantallas → `General` (nuevas: **asociaciones de archivo** ❌ 0 coincidencias, **instancia única** ❌ sólo existe «varias instancias» ✅).

## 8. Funciones de PotPlayer que FluxPlayer no tiene y requieren más que un menú

| Función | Estado | Observación |
|---|---|---|
| Emisión: captura de pantalla/PIP, captura de cámara y TV | 🟡 | Cámara ✅ (`CaptureWindows`); captura de pantalla y PIP ❌ |
| DVD / Blu-ray | ❌ | LibVLC lo soporta (`dvd://`); falta UI de menús de disco |
| TV digital (BDA/EPG) | 🟡 | `TelevisionWindow`/`TelevisionRecorder` existen (verificar alcance) |
| Filtros DirectShow externos, selección de decodificador por códec | ❌ | R1674 pendiente en el catálogo; imposible con LibVLC |
| XInput (mando) | 🟡 | 4 ficheros mencionan mando; verificar |

## 9. Dónde tenemos más que PotPlayer

Según el código y mi conocimiento de PotPlayer (no lo he contrastado punto por punto): biblioteca con puntuaciones, listas inteligentes, estadísticas de escucha y scrobbling; edición de etiquetas por lotes, renombrado y detección de duplicados; clientes de Plex/Emby/Jellyfin/Subsonic/WebDAV/UPnP/Kodi/MPD y control remoto móvil; producción y restauración de audio, masterización multicanal y recetas offline; conversión por lotes; editor de capítulos y de CUE, ediciones virtuales y exportación de clips; análisis sónico; podcasts y radio por internet; biblioteca de fotos; API de extensiones; DSP nativo de 15/20/31 bandas. PotPlayer es claramente superior en renderizado (shaders, madVR), 360°, 3D en vivo, atajos y personalización de interfaz.

## 10. Orden de implementación propuesto

1. **Menú y atajos** (bajo riesgo): reestructurar `MainContextMenu`; añadir ir-a por tiempo y pasos, puntos inicial/medio/final, copiar al portapapeles, tamaños de ventana por escritorio, proporciones extra, atajos de color. Cada ítem con su `SettingsAction`.
2. **Subtítulos «baratos»**: casillas de la página General, alineación/posición/espaciado, pasos de sincronía, CPS, copiar al portapapeles, podnapisi.
3. **Captura y salida**: WEBP, copiar cuadro, captura consecutiva, grabación en curso, OSD de información (Tab/Bloq Despl/Mayús+Tab).
4. **Sistema**: asociaciones de archivo, instancia única, enviar a la papelera, límites de ventana, perfiles desde el menú, panel de control.
5. **Traducción de subtítulos** (HTTP + claves del usuario) y **Whisper** local.
6. **Idioma de interfaz** (español primero): mayor esfuerzo, merece su propia planificación.
7. **Pesados y opcionales**: shaders y 360° vía mpv, 3D en vivo, DVD/Blu-ray, captura de pantalla/PIP, AviSynth/VapourSynth.

Cada fase debe actualizar `docs/feature-catalog.json` y regenerar `FEATURES.md` con `tools/Sync-PublicFeatures.ps1`, que es el procedimiento del proyecto.

## 11. Estado de implementación (2026-11-02)

**Segundo vídeo (2 min 29 s):** revisé unos 35 de sus 57 cambios de escena. Confirmó con detalle Subtítulos (Sincronizar, Guardar, Traducir con 7 motores, Whisper: modelos tiny→large-v3-turbo), Vídeo (Sombreador de píxel con ~40 filtros, Efectos de color con paso 1 %), Audio (Procesador de audio, Salida formato/muestreo 16/24/32 bit y 8–192 kHz) y Varios (Idioma interfaz, Siempre visible, Al finalizar, Al abrir archivos, Otros). El submenú de Temas no se ve abierto en los fotogramas revisados, y Abrir, Emisión, Álbum/favoritos y Filtros siguen sin verse por dentro. Los idiomas largos del menú no se replican: sólo inglés y español.

**Hecho:**
- Menú contextual reordenado en el orden de PotPlayer (`MainWindow.xaml`): Abrir archivo, Abrir ›, Emisión ›, Biblioteca y favoritos ›, Cerrar | Reproducir ›, Subtítulos ›, Vídeo ›, Audio ›, Filtros ›, Temas ›, Varios › | Tamaño de cuadro ›, Proporción ›, Tamaño de ventana ›, Pantalla completa | Preferencias, Lista, Información, Acerca de | Salir.
- Cada área termina en «Configurar …», que abre Ajustes directamente en su página y, cuando existe, en su sección (`SettingsPanel.ShowPage`). Las herramientas existentes (`QuickAccess`) se insertan justo antes de ese enlace; las de procesamiento de audio/vídeo y extensiones aparecen también en Filtros.
- Entradas marcables enlazadas a Ajustes mediante `Tag="set:Propiedad[=Valor]"`: se guardan en `settings.json` y se aplican al instante, así que menú y Ajustes siempre coinciden (reproducción automática, recordar posición, historial, subtítulos automáticos/negrita/cursiva/sombra, desentrelazado, decodificación por hardware, V-Sync, normalizar, amplificar, siempre visible, al finalizar, al abrir archivos, pausar al minimizar/perder foco/bloquear, bandeja, instancias, bordes, consejos).
- Nuevas acciones: pasos de ±5 s/30 s/1 min/5 min, punto inicial/medio/final, **Ir a un tiempo concreto**, velocidad (normal/más lenta/más rápida), abrir desde el portapapeles, reabrir el último archivo, copiar nombre/ruta/tiempos al portapapeles, tamaño de ventana al 30/45/60/75 % del escritorio, proporciones 16:10 y 5:4.
- **Idioma de interfaz** (`Services/Localizer.cs`, `Localizer.Spanish.cs`): inglés y español, seleccionable en Varios › Idioma o en Ajustes › General; traduce el menú, la ventana principal y Ajustes sin reiniciar. Añadir un idioma = registrarlo en `Available` y `Catalogs` y crear su catálogo.
- Tests: `PlayerMenuChecks` (orden del menú, un «Configurar» por área, propiedades enlazadas válidas, traducción completa del menú, analizador de tiempo). Suite gestionada: 573 correctas, 0 fallos.

**Pendiente (no implementado):** todo lo marcado ❌ en las secciones 2–8, incluidas traducción de subtítulos, Whisper, sombreadores, 360°/3D en vivo, asociaciones de archivo, instancia única, OSD de información, papelera, captura WEBP/portapapeles, y la mayoría de las opciones de las páginas de Preferencias de PotPlayer. Las ventanas de herramientas creadas por código (convertidor, ecualizador, etc.) y los mensajes en pantalla aún no se traducen. Además, Filtros muestra sólo los procesos que ya existían; los filtros DirectShow no son aplicables.

## 12. Segunda entrega (2026-11-02)

**Hecho y probado** (suite gestionada: 575 correctas, 0 fallos; además probado en la app real con un clip de prueba):
- **Instancia única** (`SingleInstance`): con «Permitir varias instancias» desactivado, un segundo arranque entrega sus archivos al primero y termina. Antes esa casilla no hacía nada. Test con dos instancias reales y nombres Unicode.
- **Asociaciones de archivo** (`FileAssociations`): registra FluxPlayer para el usuario actual (sin administrador) y abre Aplicaciones predeterminadas de Windows; Windows no deja que una app se fije como predeterminada sin que el usuario elija, así que el último paso es suyo. Probado sólo en una clave de registro de pruebas, **no** contra el registro real.
- **Atajos configurables** para 35 acciones nuevas (`PotActionCatalog`), sin tecla por defecto para no cambiar las existentes: ir al tiempo, pasos de 5 s a 5 min, retardo de subtítulos ±0,5/5/50 s, color con paso del 1 %, copiar cuadro y subtítulo, siguiente proporción, siempre visible, Papelera y copiar ruta. Aparecen en Ajustes › Atajos y en el menú.
- **Copiar el cuadro al portapapeles** (comprobado: imagen 640×360) y **copiar el subtítulo actual** (comprobado).
- **Mover a la Papelera** con confirmación, tras liberar el archivo.
- **Traducción de subtítulos** (`SubtitleTranslation`): LibreTranslate, DeepL y Google Cloud, en lotes de 40, con original arriba/abajo o sólo traducción; guarda un SRT nuevo sin sobrescribir. La clave nunca se guarda y sólo se aceptan direcciones https (o http en este equipo). Probado contra servidores locales que imitan cada servicio, **no contra los servicios reales** (sin claves).
- **Subtítulos desde el audio con Whisper** (`WhisperSubtitles`): usa un whisper.cpp y un modelo que instala el usuario; extrae el audio con FFmpeg. Probados los argumentos y las validaciones; **no se ha ejecutado una transcripción real** porque no hay whisper.cpp instalado aquí.
- Ventanas de herramientas y títulos se traducen al español al abrirse.

**Sigue sin hacerse** (y por qué):
- **Sombreadores de píxel, vídeo 360°, 3D en vivo, AviSynth/VapourSynth:** LibVLC no los admite; requieren pasar la reproducción por mpv u otro renderizador. Es una decisión de arquitectura, no un menú.
- **DVD/Blu-ray, captura de pantalla/PIP, paso por S/PDIF, WEBP, MVC:** necesitan hardware, medios o códecs que no tengo para comprobarlos.
- **Cuadro/fotograma clave anterior:** LibVLC no retrocede cuadros.
- **Los skins `.dsf` y el diseño de menú de PotPlayer:** otro marco de interfaz, no aplicable.
- **Traducción de las ventanas de herramientas:** los textos que construye cada ventana en código aún tienen muchas cadenas sin catálogo; se traducen las palabras comunes y los títulos, no todas.

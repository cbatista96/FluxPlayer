namespace FluxPlayer.Services;

// Messages that start with fixed words and continue with a value (a name, a code, a path): the words are translated, the value is kept.
public static partial class Localizer
{
    private static readonly (string English, string Spanish)[] SpanishPrefixes =
    [
        ("Connected to ", "Conectado a "), ("Downloading ", "Descargando "), ("Writing ", "Escribiendo "), ("Fingerprinting ", "Calculando huellas de "), ("Session authorized for ", "Sesión autorizada para "), ("Session identified as ", "Sesión identificada como "),
        ("Unknown field ", "Campo desconocido "), ("Missing linked edition UID ", "Falta el UID de la edición enlazada "), ("Map linked SegmentUID ", "Asigne el SegmentUID enlazado "), ("No supported value type matches ", "Ningún tipo de valor admitido coincide con "),
        ("FFmpeg could not complete the job (exit ", "FFmpeg no pudo completar el trabajo (código de salida "), ("MKVToolNix rejected the chapter operation (exit ", "MKVToolNix rechazó la operación de capítulos (código de salida "), ("Last.fm rejected the request (API code ", "Last.fm rechazó la petición (código de API "),
        ("Unable to measure input. ", "No se pudo medir la entrada. "), ("OCR failed. Check installed language data. ", "Falló el OCR. Compruebe los datos de idioma instalados. "), ("Conversion did not finish. ", "La conversión no terminó. "), ("Install MKVToolNix separately and add ", "Instale MKVToolNix por separado y añada "),
        ("The installed application lacks ", "La aplicación instalada no tiene "), ("This device does not expose ", "Este dispositivo no expone "), ("Enter 1–50 valid values for ", "Escriba de 1 a 50 valores válidos para "), ("Expected ", "Se esperaba "), ("Missing ", "Falta "),
        ("A CUE position is outside the supported range.", "Una posición CUE está fuera del rango admitido."), ("Working…", "Trabajando…")
    ];
}

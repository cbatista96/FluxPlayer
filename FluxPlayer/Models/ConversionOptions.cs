namespace FluxPlayer.Models;

public sealed record ConversionOptions(int SampleRate = 0, int Channels = 0, int BitrateKbps = 0, int PcmBits = 0)
{
    public static IReadOnlyList<int> Rates { get; } = new[] { 8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000, 64000, 88200, 96000, 176400, 192000 };
    public static int[] SampleRates(string encoder) => Rates.Where(rate => encoder switch
    { "libmp3lame" => rate <= 48000, "libopus" => rate == 48000, "aac" => rate <= 96000, _ => true }).Prepend(0).ToArray();
    public static int[] Bitrates(string encoder) => encoder switch
    {
        "libmp3lame" => new[] { 0, 64, 96, 128, 160, 192, 256, 320 },
        "libopus" or "aac" => new[] { 0, 32, 64, 96, 128, 160, 192, 256, 320, 512 },
        "libvorbis" => new[] { 0, 64, 96, 128, 160, 192, 256, 320 },
        _ => new[] { 0 }
    };
    public static int[] Depths(string encoder) => encoder switch { "pcm_s24le" => new[] { 0, 16, 24, 32 }, "flac" => new[] { 0, 16, 24 }, _ => new[] { 0 } };
    public void Validate(string encoder)
    {
        if (!SampleRates(encoder).Contains(SampleRate) || Channels is < 0 or > 2 || !Bitrates(encoder).Contains(BitrateKbps) || !Depths(encoder).Contains(PcmBits))
            throw new ArgumentException("Choose sample rate, channels, bitrate and PCM depth supported by the selected profile. Zero keeps the preset/source default.");
        if (encoder == "libmp3lame" && SampleRate is > 0 and < 32000 && (BitrateKbps == 0 ? 320 : BitrateKbps) > 160)
            throw new ArgumentException("MP3 below 32 kHz supports at most 160 kb/s. Choose a lower bitrate or a higher sample rate.");
    }
}

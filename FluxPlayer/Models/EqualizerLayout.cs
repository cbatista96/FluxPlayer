namespace FluxPlayer.Models;

public static class EqualizerLayout
{
    public static bool IsSupported(int count) => count is 10 or 20 or 31;
    public static double Bandwidth(int count) => count switch { 31 => 1.0 / 3, 20 => Math.Log2(1000) / 19, _ => 1 };
    public static double[] Frequencies(int count) => count switch
    {
        31 => new double[] { 20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630,
            800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000, 10000, 12500, 16000, 20000 },
        20 => Enumerable.Range(0, 20).Select(index => 20 * Math.Pow(1000, index / 19.0)).ToArray(),
        10 => Enumerable.Range(0, 10).Select(index => 31.25 * Math.Pow(2, index)).ToArray(),
        _ => throw new ArgumentException("Choose 10, 20 or 31 equalizer bands.")
    };
    public static string Label(double frequency) => frequency < 1000 ? frequency.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
        : (frequency / 1000).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "k";
}

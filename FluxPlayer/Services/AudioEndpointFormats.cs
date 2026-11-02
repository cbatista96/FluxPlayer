using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.IO;
using System.Runtime.InteropServices;

namespace FluxPlayer.Services;

public sealed record AudioEndpointInfo(string Id, string Name, int MixRate, int Channels);
public sealed record AudioFormatSupport(string Layout, int Rate, string Mode, bool Supported, string Detail);
public static class AudioEndpointFormats
{
    public static readonly string[] Layouts = ["mono", "stereo", "2.1", "5.1(side)", "7.1"];
    public static int Mask(string layout) => layout switch { "mono" => 4, "stereo" => 3, "2.1" => 11, "5.1(side)" => 0x60f, "7.1" => 0x63f, _ => throw new ArgumentException("Choose a named speaker layout.") };
    public static WaveFormat FloatFormat(int rate, string layout)
    {
        if (rate is not (44100 or 48000 or 88200 or 96000 or 176400 or 192000)) throw new ArgumentException("Choose a supported PCM sample rate.");
        int channels = System.Numerics.BitOperations.PopCount((uint)Mask(layout));
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        writer.Write((ushort)0xfffe); writer.Write((short)channels); writer.Write(rate); writer.Write(rate * channels * 4); writer.Write((short)(channels * 4)); writer.Write((short)32); writer.Write((short)22); writer.Write((short)32); writer.Write(Mask(layout)); writer.Write(new Guid("00000003-0000-0010-8000-00aa00389b71").ToByteArray());
        IntPtr memory = Marshal.AllocHGlobal(40);
        try { Marshal.Copy(bytes.ToArray(), 0, memory, 40); return WaveFormat.MarshalFromPtr(memory); } finally { Marshal.FreeHGlobal(memory); }
    }
    public static AudioEndpointInfo[] Enumerate()
    {
        using var enumerator = new MMDeviceEnumerator(); var result = new List<AudioEndpointInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            using (device) { var format = device.AudioClient.MixFormat; result.Add(new(device.ID, device.FriendlyName, format.SampleRate, format.Channels)); }
        return result.ToArray();
    }
    public static MMDevice Open(string id)
    { using var enumerator = new MMDeviceEnumerator(); return id == "Default" ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(id); }
    public static AudioFormatSupport[] Inspect(string id)
    {
        using var device = Open(id); var results = new List<AudioFormatSupport>();
        foreach (string layout in Layouts)
        foreach (int rate in new[] { 44100, 48000, 88200, 96000, 176400, 192000 })
        foreach (var mode in new[] { AudioClientShareMode.Shared, AudioClientShareMode.Exclusive })
        {
            try
            {
                bool supported = device.AudioClient.IsFormatSupported(mode, FloatFormat(rate, layout));
                results.Add(new(layout, rate, mode.ToString(), supported, supported ? "Exact float32 format accepted by the driver; physical output not tested" : "Exact format rejected; no fallback tested"));
            }
            catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
            { results.Add(new(layout, rate, mode.ToString(), false, ex.Message)); }
        }
        return results.ToArray();
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace FluxPlayer.Services;

public sealed record DisplayColorStatus(string Name, string Device, string ColorMode, bool? HdrSupported, bool? HdrUserEnabled, uint BitsPerChannel, string ColorEncoding, string LegacyIccProfile, string Detail);
public static class DisplayColorInspector
{
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Explicit, Size = 72)] private struct PathInfo
    { [FieldOffset(0)] public Luid SourceAdapter; [FieldOffset(8)] public uint SourceId; [FieldOffset(20)] public Luid TargetAdapter; [FieldOffset(28)] public uint TargetId; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type, Size; public Luid Adapter; public uint Id; }
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, IntPtr pathArray, ref uint modes, IntPtr modeArray, IntPtr topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(IntPtr packet);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateDCW(string driver, string device, IntPtr output, IntPtr init);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool GetICMProfileW(IntPtr dc, ref uint length, StringBuilder? profile);
    public static DisplayColorStatus[] Read()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int error = GetDisplayConfigBufferSizes(2, out uint paths, out uint modes); if (error != 0) throw new Win32Exception(error);
            if (paths > 128 || modes > 1024) throw new InvalidOperationException("Display topology exceeds the inspector's bound.");
            IntPtr pathData = Marshal.AllocHGlobal(checked((int)Math.Max(1, paths) * 72)), modeData = Marshal.AllocHGlobal(checked((int)Math.Max(1, modes) * 64));
            try
            {
                error = QueryDisplayConfig(2, ref paths, pathData, ref modes, modeData, IntPtr.Zero); if (error == 122) continue; if (error != 0) throw new Win32Exception(error);
                var results = new List<DisplayColorStatus>();
                byte[] Query(uint type, int size, Luid adapter, uint id, out int status)
                {
                    IntPtr packet = Marshal.AllocHGlobal(size);
                    try { Marshal.Copy(new byte[size], 0, packet, size); Marshal.StructureToPtr(new Header { Type = type, Size = (uint)size, Adapter = adapter, Id = id }, packet, false); status = DisplayConfigGetDeviceInfo(packet); byte[] bytes = new byte[size]; Marshal.Copy(packet, bytes, 0, size); return bytes; }
                    finally { Marshal.FreeHGlobal(packet); }
                }
                for (int i = 0; i < paths; i++)
                {
                    var path = Marshal.PtrToStructure<PathInfo>(pathData + i * 72);
                    var target = Query(2, 420, path.TargetAdapter, path.TargetId, out int targetError);
                    var source = Query(1, 84, path.SourceAdapter, path.SourceId, out int sourceError);
                    string name = targetError == 0 ? Encoding.Unicode.GetString(target, 36, 128).TrimEnd('\0') : "Display " + path.TargetId;
                    string device = sourceError == 0 ? Encoding.Unicode.GetString(source, 20, 64).TrimEnd('\0') : "";
                    var color = Query(15, 36, path.TargetAdapter, path.TargetId, out int colorError); bool modern = colorError == 0;
                    if (!modern) color = Query(9, 32, path.TargetAdapter, path.TargetId, out colorError);
                    uint flags = BitConverter.ToUInt32(color, 20), encoding = BitConverter.ToUInt32(color, 24), bits = BitConverter.ToUInt32(color, 28);
                    string mode = colorError != 0 ? "Unknown" : modern ? BitConverter.ToUInt32(color, 32) switch { 0 => "SDR", 1 => "WCG", 2 => "HDR", _ => "Unknown" } : (flags & 2) != 0 ? "Advanced color (legacy API)" : "SDR (legacy API)";
                    string profile = ""; IntPtr dc = device == "" ? IntPtr.Zero : CreateDCW("DISPLAY", device, IntPtr.Zero, IntPtr.Zero);
                    if (dc != IntPtr.Zero) try { uint length = 0; GetICMProfileW(dc, ref length, null); if (length is > 0 and <= 32768) { var text = new StringBuilder((int)length); if (GetICMProfileW(dc, ref length, text)) profile = text.ToString(); } } finally { DeleteDC(dc); }
                    results.Add(new(name, device, mode, modern ? (flags & 16) != 0 : null, modern ? (flags & 32) != 0 : null, colorError == 0 ? bits : 0, encoding switch { 0 => "RGB", 1 => "YCbCr 4:4:4", 2 => "YCbCr 4:2:2", 3 => "YCbCr 4:2:0", _ => "Unknown" }, profile,
                        "OS-reported display state, not measured luminance or proof of the video's swap-chain format. Legacy ICC filename is informational; Advanced Color manages profiles differently. API status: " + colorError));
                }
                return results.ToArray();
            }
            finally { Marshal.FreeHGlobal(pathData); Marshal.FreeHGlobal(modeData); }
        }
        throw new InvalidOperationException("The display topology kept changing; inspect it again.");
    }
}

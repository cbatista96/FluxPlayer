using System.Runtime.InteropServices;

namespace FluxPlayer.Services;

public sealed record DisplayAdapter(string Name, string Id, bool Primary, string Vendor);
public sealed record GpuRenderPolicy(bool Enabled = false, string Nvidia = "d3d11va", string Amd = "d3d11va", string Intel = "d3d11va", string Other = "any")
{
    public static readonly string[] Backends = ["any", "d3d11va", "dxva2", "none"];
    public void Validate() { if (new[] { Nvidia, Amd, Intel, Other }.Any(b => !Backends.Contains(b))) throw new ArgumentException("Choose an available decoder policy."); }
    public string Resolve(IEnumerable<DisplayAdapter> adapters, bool hardware)
    {
        Validate(); if (!hardware) return "none"; if (!Enabled) return "any";
        var list = adapters.ToArray(); var adapter = list.FirstOrDefault(a => a.Primary) ?? list.FirstOrDefault();
        return adapter?.Vendor switch { "NVIDIA" => Nvidia, "AMD" => Amd, "Intel" => Intel, "Microsoft" => "none", _ => Other };
    }
    public static DisplayAdapter[] Detect()
    {
        var result = new List<DisplayAdapter>();
        for (uint i = 0; i < 32; i++)
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, i, ref device, 0)) break;
            if ((device.Flags & 1) == 0) continue;
            string id = device.Id.ToUpperInvariant();
            string vendor = id.Contains("VEN_10DE") ? "NVIDIA" : id.Contains("VEN_1002") ? "AMD" : id.Contains("VEN_8086") ? "Intel" : device.Description.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ? "Microsoft" : "Other";
            result.Add(new(device.Description, device.Id, (device.Flags & 4) != 0, vendor));
        }
        return result.ToArray();
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayDevices(string? name, uint index, ref DisplayDevice device, uint flags);
}

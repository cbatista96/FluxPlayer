using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static class EngineCompletionChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string Folder(string root) { string path = Path.Combine(root, "engine-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    public static Task Chain()
    {
        using var chain = new PcmAudioChain(48000, 2); chain.Add(new PcmGain(2)); chain.Add(new PcmLimiter(.5f));
        float[] samples = [.4f, -.4f, .1f, -.1f]; chain.Process(samples); Check(samples.SequenceEqual(new[] { .5f, -.5f, .2f, -.2f }), "Effects run in declared order.");
        using var matrix = new PcmChannelMatrix([[0, 1], [1, 0]], [1, .5f], [0, 1]); matrix.Initialize(1000, 2); float[] impulse = [1, .5f, 0, 0]; matrix.Process(impulse);
        Check(impulse.SequenceEqual(new[] { .5f, 0, 0, .5f }), "Matrix routes first, then physical gain and delay.");
        foreach (string layout in AudioEndpointFormats.Layouts) { var f = AudioEndpointFormats.FloatFormat(48000, layout); Check(f.Channels == System.Numerics.BitOperations.PopCount((uint)AudioEndpointFormats.Mask(layout)), "Named endpoint channel count."); using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes); f.Serialize(writer); var data = bytes.ToArray(); Check(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(24)) == AudioEndpointFormats.Mask(layout), "Serialized channel mask preserves physical speaker assignment."); }
        return Task.CompletedTask;
    }
    public static Task Plugin()
    {
        using var chain = new PcmAudioChain(48000, 2); chain.LoadTrustedPlugin(typeof(TestPcmEffect).Assembly.Location, typeof(TestPcmEffect).FullName!);
        float[] samples = [.1f, -.2f]; chain.Process(samples); Check(Math.Abs(samples[0] - .3f) < .00001 && Math.Abs(samples[1] + .6f) < .00001, "A separately loaded managed effect processes the actual PCM chain contract."); return Task.CompletedTask;
    }
    public static Task QualityMatrix()
    {
        foreach (int rate in new[] { 44100, 48000, 88200, 96000, 176400, 192000 })
        foreach (string layout in AudioEndpointFormats.Layouts)
        {
            int channels = AudioEndpointFormats.FloatFormat(rate, layout).Channels;
            float[][] matrix = Enumerable.Range(0, channels).Select(i => Enumerable.Range(0, channels).Select(j => i == j ? 1f : 0f).ToArray()).ToArray();
            using var chain = new PcmAudioChain(rate, channels);
            chain.Add(new PcmChannelMatrix(matrix, Enumerable.Repeat(1f, channels).ToArray(), new int[channels])); chain.Add(new PcmGain(.5f)); chain.Add(new PcmLimiter(.9f));
            var input = Enumerable.Range(0, channels * 2053).Select(i => (float)(.7 * Math.Sin((i / channels) * 2 * Math.PI * 997 / rate) + (i % channels) * .01)).ToArray();
            var actual = (float[])input.Clone();
            // Deliberately process nonuniform blocks to expose state/frame-boundary errors.
            int at = 0; while (at < actual.Length) { int count = Math.Min(channels * 137, actual.Length - at); chain.Process(actual.AsSpan(at, count)); at += count; }
            Check(actual.Zip(input).All(p => float.IsFinite(p.First) && Math.Abs(p.First - p.Second * .5f) < 1e-6), $"Measured unity routing/gain and sample continuity: {rate}Hz {layout}.");
        }
        return Task.CompletedTask;
    }
    public static async Task LinkedEditions(string root)
    {
        string folder = Folder(root), a = Constant(folder, "a", 0), b = Constant(folder, "b", 0), c = Constant(folder, "c", 0);
        const string uidB = "11111111111111111111111111111111", uidC = "22222222222222222222222222222222";
        MatroskaChapterProject Project(string uid) { var p = new MatroskaChapterProject(); p.AddEdition(true).Element("EditionUID")!.Value = uid; return p; }
        var first = Project("1"); var firstEdition = first.Document.Root!.Element("EditionEntry")!;
        var reference = first.AddChapter(firstEdition, "link", 0, 100); first.Link(reference, uidB, "2"); reference.Element("ChapterTimeEnd")!.Remove();
        var second = Project("2"); var secondEdition = second.Document.Root!.Element("EditionEntry")!;
        second.AddChapter(secondEdition, "B", 100, 400);
        var nested = second.AddChapter(secondEdition, "C edition", 0, 1); second.Link(nested, uidC, "3");
        var third = Project("3"); var thirdEdition = third.Document.Root!.Element("EditionEntry")!; var tail = third.AddChapter(thirdEdition, "C", 200, 900);
        var map = new Dictionary<string, LinkedChapterSegment> { [uidB] = new(b, second), [uidC] = new(c, third) };
        var steps = LinkedChapters.CompileLinked(first, "1", a, map, "eng");
        Check(steps.Length == 2 && steps[0].Source == b && steps[0].StartMs == 100 && steps[0].EndMs == 400 && steps[1].Source == c && steps[1].EndMs == 900, "Recursive linked editions play in full, independent of the reference chapter duration.");
        third.Link(tail, uidB, "2"); bool cycle = false; try { LinkedChapters.CompileLinked(first, "1", a, map, "eng"); } catch (ArgumentException e) { cycle = e.Message.Contains("cycle"); }
        Check(cycle, "Cross-segment edition cycles are rejected before playback.");
        third.Link(tail, "", ""); map[uidC] = new(c); bool missing = false; try { LinkedChapters.CompileLinked(first, "1", a, map, "eng"); } catch (ArgumentException) { missing = true; }
        Check(missing, "Edition references require the linked document.");
        string xml = Path.Combine(folder, "linked.xml"); await first.Save(xml, CancellationToken.None); Check(MatroskaChapterProject.Read(xml).Document.Descendants("ChapterTimeEnd").Count() == 0, "Linked-Edition allows absent ChapterTimeEnd in validated XML.");
    }
    private static string Dsf(string folder, int frames = 5000)
    {
        string path = Path.Combine(folder, "source.dsf"); int blocks = (frames + 4095) / 4096; long length = 28 + 52 + 12 + blocks * 8192L;
        using var writer = new BinaryWriter(File.Create(path)); writer.Write("DSD "u8); writer.Write(28UL); writer.Write((ulong)length); writer.Write(0UL);
        writer.Write("fmt "u8); writer.Write(52UL); writer.Write(1U); writer.Write(0U); writer.Write(2U); writer.Write(2U); writer.Write(2822400U); writer.Write(1U); writer.Write((ulong)frames * 8); writer.Write(4096U); writer.Write(0U); writer.Write("data"u8); writer.Write((ulong)blocks * 8192 + 12);
        for (int block = 0; block < blocks; block++) for (int channel = 0; channel < 2; channel++) for (int i = 0; i < 4096; i++) writer.Write(block * 4096 + i < frames ? (byte)((block * 4096 + i + channel * 37) % 256) : (byte)0);
        return path;
    }
    public static Task DsdFiles(string root)
    {
        string folder = Folder(root), path = Dsf(folder); using var source = new DsdBitstream(path); Check(source.Channels == 2 && source.SamplesPerChannel == 40000 && source.LeastSignificantBitFirst, "DSF format is parsed without PCM conversion.");
        byte[] data = new byte[10000]; Check(source.ReadInterleaved(data, true) == 5000, "DSF padding is excluded at EOF.");
        for (int i = 0; i < 5000; i++) { Check(data[2 * i] == (byte)(i % 256) && data[2 * i + 1] == (byte)((i + 37) % 256), "DSF channel-block deinterleaving."); }
        using var reverse = new DsdBitstream(path); byte[] reversed = new byte[10000]; reverse.ReadInterleaved(reversed, false); Check(reversed.Select(DsdBitstream.Reverse).SequenceEqual(data), "DSD bit-order conversion is lossless.");
        Check(source.ReadInterleaved(data, true) == 0, "DSF ends on its declared sample count."); return Task.CompletedTask;
    }
    public static Task DffFiles(string root)
    {
        string folder = Folder(root), path = Path.Combine(folder, "native.dff");
        byte[] Number(long number, int size) { var b = new byte[size]; if (size == 8) BinaryPrimitives.WriteInt64BigEndian(b, number); else if (size == 4) BinaryPrimitives.WriteInt32BigEndian(b, (int)number); else BinaryPrimitives.WriteInt16BigEndian(b, (short)number); return b; }
        byte[] Chunk(string id, byte[] data) => Encoding.ASCII.GetBytes(id).Concat(Number(data.Length, 8)).Concat(data).Concat(data.Length % 2 == 0 ? Array.Empty<byte>() : new byte[1]).ToArray();
        byte[] raw = Enumerable.Range(0, 502).Select(i => (byte)(i % 256)).ToArray();
        byte[] properties = "SND "u8.ToArray().Concat(Chunk("FS  ", Number(2822400, 4))).Concat(Chunk("CHNL", Number(2, 2).Concat("SLFTSRGT"u8.ToArray()).ToArray())).Concat(Chunk("CMPR", "DSD "u8.ToArray().Concat(new byte[2]).ToArray())).ToArray();
        byte[] body = "DSD "u8.ToArray().Concat(Chunk("PROP", properties)).Concat(Chunk("DSD ", raw)).ToArray();
        File.WriteAllBytes(path, Chunk("FRM8", body));
        using (var source = new DsdBitstream(path))
        {
            byte[] output = new byte[raw.Length]; Check(!source.LeastSignificantBitFirst && source.ReadInterleaved(output, false) == 251 && output.SequenceEqual(raw), "DSDIFF byte-interleaved MSB data remains exact.");
        }
        using (var source = new DsdBitstream(path))
        {
            byte[] output = new byte[raw.Length]; source.ReadInterleaved(output, true); Check(output.Select(DsdBitstream.Reverse).SequenceEqual(raw), "DSDIFF native target bit order is reversible.");
        }
        bool Reject(string file) { try { using var source = new DsdBitstream(file); return false; } catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException) { return true; } }
        string dst = Path.Combine(folder, "compressed.dff"); File.WriteAllBytes(dst, Chunk("FRM8", body.Concat(Chunk("DST ", [])).ToArray())); Check(Reject(dst), "Compressed DST is rejected without PCM fallback.");
        string broken = Path.Combine(folder, "truncated.dff"); File.WriteAllBytes(broken, File.ReadAllBytes(path)[..^1]); Check(Reject(broken), "DSDIFF form truncation is rejected.");
        string dsf = Dsf(folder); var dsfBytes = File.ReadAllBytes(dsf); BinaryPrimitives.WriteUInt64LittleEndian(dsfBytes.AsSpan(84), ulong.MaxValue); File.WriteAllBytes(dsf, dsfBytes); Check(Reject(dsf), "Oversized DSF data cannot reach native buffers.");
        return Task.CompletedTask;
    }
    private sealed class FakeDsdDevice : INativeDsdDevice
    {
        private IntPtr[] _buffers = []; private Action<IntPtr[]>? _fill;
        public bool LsbFirst => true;
        public int BufferBits => 1024;
        public void Open(int rate, int channels, Action<IntPtr[]> fill) { Check(rate == 2822400 && channels == 2, "Exact native DSD device format."); _fill = fill; _buffers = Enumerable.Range(0, channels).Select(_ => Marshal.AllocHGlobal(BufferBits / 8 + 16)).ToArray(); }
        public byte[][] Pull()
        {
            byte[] guard = Enumerable.Repeat((byte)0xab, BufferBits / 8 + 16).ToArray(); foreach (var p in _buffers) Marshal.Copy(guard, 0, p, guard.Length);
            _fill!(_buffers); var result = new byte[_buffers.Length][];
            for (int i = 0; i < result.Length; i++) { var bytes = new byte[guard.Length]; Marshal.Copy(_buffers[i], bytes, 0, bytes.Length); Check(bytes.AsSpan(BufferBits / 8).ToArray().All(b => b == 0xab), "Native callback respects byte-buffer bounds."); result[i] = bytes[..(BufferBits / 8)]; }
            return result;
        }
        public void Play() { }
        public void Pause() { }
        public void Dispose() { foreach (var p in _buffers) Marshal.FreeHGlobal(p); _buffers = []; }
    }
    public static async Task NativeDsd(string root)
    {
        string path = Dsf(Folder(root)); var device = new FakeDsdDevice(); await using var session = new NativeDsdSession(); await session.Prepare(path, device, CancellationToken.None); session.Play();
        var left = new List<byte>(); var right = new List<byte>();
        while (!session.Finished) { var block = device.Pull(); left.AddRange(block[0]); right.AddRange(block[1]); }
        Check(session.Error == null && session.UnderrunByteFrames == 0, "Native DSD callback stream has no missing frames.");
        for (int i = 0; i < 5000; i++) Check(left[i] == (byte)(i % 256) && right[i] == (byte)((i + 37) % 256), "Driver-bound raw DSD bytes are bit-identical.");
        Check(left.Skip(5000).All(b => b == 0x69) && right.Skip(5000).All(b => b == 0x69), "Only the final hardware-buffer remainder is DSD silence.");
    }
    private static string Constant(string folder, string name, float value)
    {
        string path = Path.Combine(folder, name + ".wav"); using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + 48000 * 2 * 2); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000); writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(192000); for (int i = 0; i < 96000; i++) writer.Write((short)(value * 32768)); return path;
    }
    public static async Task Stream(string root, double fade)
    {
        string folder = Folder(root), a = Constant(folder, "first", .25f), b = Constant(folder, "second", .5f);
        await using var session = new RealtimeAudioSession(); await session.Prepare([a, b], new() { BufferMilliseconds = 10000, CrossfadeSeconds = fade }, null, CancellationToken.None);
        byte[] buffer = new byte[32768]; var data = new MemoryStream(); int read;
        while ((read = session.ReadForVerification(buffer, 0, buffer.Length)) > 0) data.Write(buffer, 0, read);
        var samples = MemoryMarshal.Cast<byte, float>(data.ToArray()).ToArray(); int expectedFrames = (int)Math.Round((2 - fade) * 48000);
        Check(samples.Length == expectedFrames * 2, $"Continuous decoder produces the exact joined/faded frame count: actual={samples.Length / 2}, expected={expectedFrames}.");
        Check(samples[0] == .25f && samples[^1] == .5f && !samples.Contains(0f), "No inserted silence at the live PCM boundary.");
        if (fade == 0) Check(samples[95998] == .25f && samples[96000] == .5f, "Gapless transition is sample adjacent.");
        else { int midpoint = (int)((1 - fade / 2) * 48000) * 2; Check(Math.Abs(samples[midpoint] - .375f) < .002, "Live triangular crossfade has the expected overlap amplitude."); }
    }
    public static async Task Direct(string root)
    {
        string path = Constant(Folder(root), "direct", .25f); await using var session = new RealtimeAudioSession(); await session.Prepare([path], new() { DirectPcm = true, Exclusive = true }, null, CancellationToken.None);
        var bytes = new byte[192000]; Check(session.ReadForVerification(bytes, 0, bytes.Length) == bytes.Length && bytes.SequenceEqual(File.ReadAllBytes(path).Skip(44)), "Exclusive source provider passes integer PCM bytes unchanged, without float conversion.");
    }
    public static async Task WasapiMain(string root)
    {
        string folder = Folder(root), a = Constant(folder, "silence-a", 0), b = Constant(folder, "silence-b", 0);
        var endpoints = AudioEndpointFormats.Enumerate(); Check(endpoints.Length > 0, "An active Windows output endpoint is available for the silent native test.");
        using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); await using var library = new LibraryService(folder);
        var vm = new FluxPlayer.ViewModels.MainViewModel(engine, new SettingsService(folder), new AppSettings { Volume = 0, AutoPlay = false }, library);
        async Task Wait(Func<bool> condition) { var timer = System.Diagnostics.Stopwatch.StartNew(); while (!condition()) { if (timer.ElapsedMilliseconds > 10000) throw new TimeoutException("Waiting for real-time WASAPI main transport."); await Task.Delay(10); } }
        try
        {
            vm.AddFilesToPlaylist([a, b], false); await vm.StartRealtimeAudio(new() { BufferMilliseconds = 2000 });
            await Wait(() => vm.IsPlaying && vm.IsAudioMode); vm.PlayPauseCommand.Execute(null); await Wait(() => !vm.IsPlaying);
            vm.PlayPauseCommand.Execute(null); await Wait(() => vm.IsPlaying);
            await Wait(() => vm.CurrentItem?.FilePath == b); Check(engine.Realtime?.Position().UnderrunFrames == 0, "Actual Windows output crosses the main queue boundary without an underrun.");
            vm.StopCommand.Execute(null); Check(!vm.IsRealtimeAudio && !engine.IsPlaying, "Main Stop releases real-time playback.");
        }
        finally { vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
    }
    public static Task EndpointMatrix(string root)
    {
        var endpoints = AudioEndpointFormats.Enumerate(); Check(endpoints.Length > 0, "Active endpoint discovery.");
        var matrices = endpoints.Select(e => new { endpoint = e, formats = AudioEndpointFormats.Inspect(e.Id), physicalOutputVerified = false }).ToArray();
        Check(matrices.All(m => m.formats.Length == 60 && m.formats.Select(f => (f.Layout, f.Rate, f.Mode)).Distinct().Count() == 60), "Every actual endpoint is queried for 5 layouts × 6 rates × 2 modes, without playing sound.");
        File.WriteAllText(Path.Combine(root, "audio-endpoint-formats.json"), System.Text.Json.JsonSerializer.Serialize(matrices, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return Task.CompletedTask;
    }
}

public sealed class TestPcmEffect : IPcmAudioEffect
{
    public string Name => "Test multiplier";
    public void Initialize(int sampleRate, int channels) { }
    public void Process(Span<float> samples) { for (int i = 0; i < samples.Length; i++) samples[i] *= 3; }
    public void Dispose() { }
}

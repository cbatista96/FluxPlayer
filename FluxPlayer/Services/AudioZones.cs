using FluxPlayer.Models;
using System.Collections.Immutable;
using System.IO;
using System.Windows.Threading;

namespace FluxPlayer.Services;

public sealed record AudioZoneProfile(string Id, string Name, string Device, int Volume, bool Mute, float Rate, bool Repeat, long OffsetMs, ImmutableArray<string> Queue);
public sealed record AudioZoneProfiles(ImmutableArray<AudioZoneProfile> Zones);
public sealed record AudioZoneState(string Id, string Name, string Device, string Source, bool Playing, long PositionMs, long LengthMs, int QueueCount, long RelativeDriftMs, int Volume = 0, bool Muted = false, float Rate = 1);
public sealed class AudioZones : IAsyncDisposable
{
    private sealed class Zone(AudioZoneProfile profile)
    {
        public AudioZoneProfile Profile = profile;
        public readonly MediaService Engine = new(hardwareDecoding: false, videoOutputModule: "dummy", enableDiagnosticLogging: false);
        public int Index;
        public bool Loaded;
        public long? PendingSeek;
    }
    private readonly Dictionary<string, Zone> _zones = new(StringComparer.Ordinal);
    private readonly Dispatcher _dispatcher;
    private readonly ProfileDocument<AudioZoneProfiles> _profiles;
    private bool _closed;
    public event Action<string>? Error;
    public AudioZones(string directory, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher; _profiles = new(Path.Combine(directory, "audio-zones.json"), () => new([]), data =>
        { if (data.Zones.IsDefault || data.Zones.Length > 4 || data.Zones.Select(z => z.Id).Distinct().Count() != data.Zones.Length) throw new ArgumentException("Keep at most four unique zones."); foreach (var zone in data.Zones) Validate(zone); }, 1024 * 1024);
    }
    private static void Validate(AudioZoneProfile profile)
    {
        if (!Guid.TryParseExact(profile.Id, "N", out _) || profile.Name.Length is < 1 or > 100 || profile.Name.Any(char.IsControl) || !AudioOutputProfile.ValidDeviceId(profile.Device) || profile.Volume is < 0 or > 100 || !float.IsFinite(profile.Rate) || profile.Rate is < .5f or > 2f || profile.OffsetMs is < -5000 or > 5000 || profile.Queue.IsDefault || profile.Queue.Length > 500 || profile.Queue.Any(p => p.Length > 32767 || !Path.IsPathFullyQualified(p) || MediaFormats.IsNetwork(p) || !MediaFormats.IsAudio(p))) throw new ArgumentException("Check zone identity, output, volume, speed, ±5-second offset and up to 500 absolute local audio sources.");
    }
    private Zone Get(string id) => !_closed && _zones.TryGetValue(id, out var zone) ? zone : throw new ArgumentException("Select an active zone.");
    public AudioZoneProfile[] Profiles => _zones.Values.Select(z => z.Profile).ToArray();
    public void Create(AudioZoneProfile profile)
    {
        _dispatcher.VerifyAccess(); Validate(profile); if (_closed || _zones.Count >= 4 || _zones.ContainsKey(profile.Id)) throw new ArgumentException("Close/remove a zone before adding another; maximum four.");
        var zone = new Zone(profile);
        try
        {
            // WASAPI's mmdevice backend changes the process-wide Windows audio session.
            // DirectSound keeps gain/mute on each player's own buffer, including when
            // several zones target the same physical endpoint.
            if (!zone.Engine.MediaPlayer.SetAudioOutput("directsound")) throw new IOException("Independent zone audio output is unavailable.");
            zone.Engine.SelectAudioDevice(profile.Device); zone.Engine.SetVolume(profile.Volume); zone.Engine.MediaPlayer.Mute = profile.Mute; zone.Engine.SetRate(profile.Rate);
            zone.Engine.EndReached += (_, _) => _dispatcher.BeginInvoke(new Action(() => { try { if (!_closed && _zones.ContainsKey(profile.Id)) Next(profile.Id); } catch (Exception ex) when (ex is not OutOfMemoryException) { Error?.Invoke(ex.Message); } }));
            zone.Engine.Playing += (_, _) => _dispatcher.BeginInvoke(new Action(() => { if (_closed || !_zones.ContainsKey(profile.Id)) return; try { zone.Engine.SelectAudioDevice(zone.Profile.Device); zone.Engine.SetRate(zone.Profile.Rate); if (zone.PendingSeek is long time) { zone.PendingSeek = null; zone.Engine.SeekTo(time); } } catch (Exception ex) when (ex is not OutOfMemoryException) { Error?.Invoke(ex.Message); } }));
            _zones.Add(profile.Id, zone);
        }
        catch { zone.Engine.Dispose(); throw; }
    }
    public AudioDevice[] Devices(string id) => Get(id).Engine.GetAudioDevices();
    public void Configure(AudioZoneProfile profile)
    {
        _dispatcher.VerifyAccess(); Validate(profile); var zone = Get(profile.Id); if (!zone.Profile.Queue.SequenceEqual(profile.Queue)) { zone.Engine.Stop(); zone.Index = 0; zone.PendingSeek = null; }
        zone.Profile = profile; zone.Engine.SelectAudioDevice(profile.Device); zone.Engine.SetVolume(profile.Volume); zone.Engine.MediaPlayer.Mute = profile.Mute; zone.Engine.SetRate(profile.Rate);
    }
    public void LoadQueue(string id, IEnumerable<string> sources)
    {
        var zone = Get(id); var queue = sources.Distinct(StringComparer.OrdinalIgnoreCase).Take(501).Select(Path.GetFullPath).ToImmutableArray();
        if (queue.Length == 0 || queue.Any(p => !File.Exists(p))) throw new ArgumentException("Choose available local audio files."); Configure(zone.Profile with { Queue = queue });
    }
    private void Start(Zone zone, long position)
    {
        if (zone.Profile.Queue.Length == 0) throw new ArgumentException("Load a zone queue first."); string path = zone.Profile.Queue[zone.Index]; if (!File.Exists(path)) throw new FileNotFoundException("A zone source is unavailable.", path);
        zone.PendingSeek = Math.Max(0, position + zone.Profile.OffsetMs); zone.Engine.PlayFile(path); zone.Loaded = true;
    }
    public void Transport(string id, string action)
    {
        _dispatcher.VerifyAccess(); var zone = Get(id);
        switch (action) { case "Play": if (!zone.Loaded || zone.Engine.MediaPlayer.State is VLCState.Stopped or VLCState.Ended or VLCState.Error) Start(zone, 0); else zone.Engine.Play(); break; case "Pause": zone.Engine.Pause(); break; case "Stop": zone.PendingSeek = null; zone.Engine.Stop(); break; default: throw new ArgumentException("Choose Play/Pause/Stop."); }
    }
    public void Next(string id)
    {
        var zone = Get(id); if (zone.Profile.Queue.Length == 0) return;
        if (zone.Index + 1 < zone.Profile.Queue.Length) zone.Index++; else if (zone.Profile.Repeat) zone.Index = 0; else { zone.Engine.Stop(); return; } Start(zone, 0);
    }
    public void Group(IEnumerable<string> ids, string action, long timeMs = 0)
    {
        _dispatcher.VerifyAccess(); var selected = ids.Distinct().Select(Get).ToArray(); if (selected.Length is < 1 or > 4 || timeMs is < 0 or > 86400000) throw new ArgumentException("Select 1–4 zones and a position within 24 hours.");
        if (action == "Start queues" && selected.Any(z => z.Profile.Queue.Length == 0 || !File.Exists(z.Profile.Queue[z.Index]))) throw new IOException("Every selected zone needs an available queued source.");
        foreach (var zone in selected) { if (action == "Start queues") Start(zone, timeMs); else if (action == "Seek") zone.Engine.SeekTo(Math.Max(0, timeMs + zone.Profile.OffsetMs)); else Transport(zone.Profile.Id, action); }
    }
    public AudioZoneState[] Snapshot()
    {
        _dispatcher.VerifyAccess(); var data = _zones.Values.Select(z => new AudioZoneState(z.Profile.Id, z.Profile.Name, z.Profile.Device, z.Profile.Queue.Length > 0 ? Path.GetFileName(z.Profile.Queue[z.Index]) : "", z.Engine.MediaPlayer.IsPlaying, z.Engine.MediaPlayer.Time, z.Engine.MediaPlayer.Length, z.Profile.Queue.Length, 0, z.Engine.MediaPlayer.Volume, z.Engine.MediaPlayer.Mute, z.Engine.MediaPlayer.Rate)).ToArray();
        long reference = data.FirstOrDefault(s => s.Playing)?.PositionMs ?? 0; return data.Select(s => s with { RelativeDriftMs = s.PositionMs - reference }).ToArray();
    }
    public Task Save(CancellationToken token) { var snapshot = Profiles.ToImmutableArray(); return _profiles.Edit(_ => new(snapshot), token); }
    public Task<AudioZoneProfiles> Saved(CancellationToken token) => _profiles.Read(token);
    public async Task Remove(string id)
    { _dispatcher.VerifyAccess(); var zone = Get(id); _zones.Remove(id); await Task.Run(() => { zone.Engine.Stop(); zone.Engine.Dispose(); }).ConfigureAwait(false); }
    public async ValueTask DisposeAsync()
    {
        _closed = true; var zones = _zones.Values.ToArray(); _zones.Clear();
        await Task.Run(() =>
        {
            foreach (var zone in zones)
            {
                try { zone.Engine.Stop(); } catch (Exception ex) when (ex is not OutOfMemoryException) { System.Diagnostics.Trace.WriteLine("Zone stop during close: " + ex.Message); }
                try { zone.Engine.Dispose(); } catch (Exception ex) when (ex is not OutOfMemoryException) { System.Diagnostics.Trace.WriteLine("Zone disposal during close: " + ex.Message); }
            }
        }).ConfigureAwait(false);
    }
}

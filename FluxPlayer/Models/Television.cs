using System.Collections.Immutable;

namespace FluxPlayer.Models;

public sealed record TvChannel(string Id, string Name, string Source, string Group, string GuideId) { public override string ToString() => Name; }
public sealed record TvProgramme(string GuideId, string Title, string Description, DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{ public string StartLocal => StartUtc.LocalDateTime.ToString("g"); public int DurationSeconds => (int)(EndUtc - StartUtc).TotalSeconds; }
public sealed record TvRecording(string Id, string ChannelId, string Title, string Path, DateTimeOffset StartedUtc, int Seconds);
public sealed record TvRecordingTask(string Id, string ChannelId, DateTimeOffset DueUtc, int Seconds, string Destination, string State = "Armed")
{ public string DueLocal => DueUtc.LocalDateTime.ToString("g"); }
public sealed record TelevisionData(ImmutableArray<TvChannel> Channels, ImmutableArray<TvProgramme> Guide, ImmutableArray<TvRecording> Recordings, ImmutableArray<TvRecordingTask> Tasks);

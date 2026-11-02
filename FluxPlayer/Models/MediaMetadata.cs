namespace FluxPlayer.Models;

public sealed record MediaMetadata(string Title, string Artist, string Album, string Genre,
    string Artwork, long DurationMs, bool HasAudio, bool HasVideo, string Technical, int Year = 0, int TrackNumber = 0);

public sealed record AudioDevice(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record ChapterInfo(int Index, string Name, long TimeMs);

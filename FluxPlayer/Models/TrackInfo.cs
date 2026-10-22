namespace FluxPlayer.Models;

public class TrackInfo
{
    public int Id { get; }
    public string Name { get; }

    public TrackInfo(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public override string ToString() => Name;
}

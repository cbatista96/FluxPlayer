namespace FluxPlayer.Services;

public sealed record TouchDecision(double SeekSeconds = 0, double VolumeDelta = 0, double ZoomFactor = 1, double PanX = 0, double PanY = 0, bool Tap = false);

/// <summary>One decision per contact sequence; third contacts and lost capture cancel it.</summary>
public sealed class TouchGestures
{
    private readonly Dictionary<int, (double X, double Y)> _points = new();
    private (double X, double Y) _start, _end, _centerStart, _centerEnd;
    private double _distanceStart, _distanceEnd, _width, _height;
    private bool _multi, _canceled, _moved;
    public bool Active => _points.Count > 0;
    public void Begin(int id, double x, double y, double width, double height)
    {
        if (!double.IsFinite(x + y + width + height) || width <= 0 || height <= 0) { Cancel(); return; }
        if (!Active) { _canceled = _multi = _moved = false; _start = _end = (x, y); _width = width; _height = height; }
        else if (_multi && !_points.ContainsKey(id)) _canceled = true;
        _points[id] = (x, y);
        if (_points.Count > 2) _canceled = true;
        else if (_points.Count == 2 && !_multi)
        {
            _multi = true; (_centerStart, _distanceStart) = Geometry();
            _centerEnd = _centerStart; _distanceEnd = _distanceStart;
        }
    }
    public void Move(int id, double x, double y)
    {
        if (!_points.ContainsKey(id)) return;
        if (!double.IsFinite(x) || !double.IsFinite(y)) { _canceled = true; return; }
        _points[id] = (x, y);
        if (_multi && _points.Count == 2) (_centerEnd, _distanceEnd) = Geometry();
        else if (!_multi) { _end = (x, y); _moved |= Math.Max(Math.Abs(x - _start.X), Math.Abs(y - _start.Y)) >= 24; }
    }
    private ((double X, double Y) Center, double Distance) Geometry()
    {
        var p = _points.Values.ToArray(); double dx = p[0].X - p[1].X, dy = p[0].Y - p[1].Y;
        return (((p[0].X + p[1].X) / 2, (p[0].Y + p[1].Y) / 2), Math.Sqrt(dx * dx + dy * dy));
    }
    public TouchDecision? End(int id, double x, double y)
    {
        if (!_points.ContainsKey(id)) return null;
        Move(id, x, y); _points.Remove(id); if (Active || _canceled) return null;
        if (_multi)
        {
            double ratio = _distanceStart < 24 ? 1 : Math.Clamp(_distanceEnd / _distanceStart, .125, 8);
            if (Math.Abs(ratio - 1) < .04) ratio = 1;
            return new(ZoomFactor: ratio, PanX: Math.Clamp(-2 * (_centerEnd.X - _centerStart.X) / _width, -2, 2), PanY: Math.Clamp(-2 * (_centerEnd.Y - _centerStart.Y) / _height, -2, 2));
        }
        double dx = _end.X - _start.X, dy = _end.Y - _start.Y;
        if (!_moved) return new(Tap: true);
        return Math.Abs(dx) >= Math.Abs(dy) ? new(SeekSeconds: Math.Clamp(dx / _width * 120, -120, 120)) : new(VolumeDelta: Math.Clamp(-dy / _height * 100, -100, 100));
    }
    public void Cancel() { _points.Clear(); _canceled = true; }
}

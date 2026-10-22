using FluxPlayer.Models;
using System.IO.MemoryMappedFiles;

namespace FluxPlayer.Services;

/// <summary>VLC plugin protocol defined in Native/FluxDsp/fluxdsp.c. Writes occur only on explicit EQ changes.</summary>
public sealed unsafe class NativeDspBridge : IDisposable
{
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly object _gate = new();
    private byte* _data;
    private int _sequence;
    private bool _disposed;
    private float[] _lastBands = Array.Empty<float>();
    private float _lastPreamp;
    private bool _lastEnabled, _lastAntiClip;
    public string Name { get; } = $"Local\\FluxPlayer.Dsp.{Environment.ProcessId}.{Guid.NewGuid():N}";

    public NativeDspBridge()
    {
        _mapping = MemoryMappedFile.CreateNew(Name, 1024, MemoryMappedFileAccess.ReadWrite);
        _view = _mapping.CreateViewAccessor(0, 1024, MemoryMappedFileAccess.ReadWrite);
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _data);
        Publish(new float[10], 0, false, true);
    }

    public void Publish(float[] bands, float preamp, bool enabled, bool antiClip)
    {
        if (!EqualizerLayout.IsSupported(bands.Length) || bands.Any(value => !float.IsFinite(value) || value is < -20 or > 20)
            || !float.IsFinite(preamp) || preamp is < -20 or > 20) throw new ArgumentException("Invalid native DSP settings.");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_lastBands.SequenceEqual(bands) && _lastPreamp == preamp && _lastEnabled == enabled && _lastAntiClip == antiClip) return;
            var frequencies = EqualizerLayout.Frequencies(bands.Length);
            int next = unchecked(_sequence + 2);
            Volatile.Write(ref *(int*)(_data + 8), unchecked(next - 1));
            *(uint*)_data = 0x46584450; *(uint*)(_data + 4) = 1;
            *(int*)(_data + 12) = bands.Length; *(int*)(_data + 16) = enabled ? 1 : 0;
            *(float*)(_data + 20) = preamp; *(int*)(_data + 24) = antiClip ? 1 : 0;
            *(float*)(_data + 28) = (float)EqualizerLayout.Bandwidth(bands.Length);
            for (int index = 0; index < 31; index++)
            {
                *(float*)(_data + 32 + index * 4) = index < bands.Length ? bands[index] : 0;
                *(float*)(_data + 156 + index * 4) = index < frequencies.Length ? (float)frequencies[index] : 0;
            }
            Thread.MemoryBarrier(); Volatile.Write(ref *(int*)(_data + 8), next); _sequence = next;
            _lastBands = (float[])bands.Clone(); _lastPreamp = preamp; _lastEnabled = enabled; _lastAntiClip = antiClip;
        }
    }

    public NativeDspStatus Status
    {
        get
        {
            lock (_gate)
            {
                if (_disposed) return new(false, 0, 0, 0, false, 0);
                bool active = Volatile.Read(ref *(int*)(_data + 512)) > 0;
                int applied = Volatile.Read(ref *(int*)(_data + 524));
                return new(active, *(uint*)(_data + 516), *(uint*)(_data + 520), *(float*)(_data + 528),
                    applied == _sequence, Volatile.Read(ref *(int*)(_data + 532)));
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _view.SafeMemoryMappedViewHandle.ReleasePointer(); _data = null;
            _view.Dispose(); _mapping.Dispose();
        }
    }
}

public sealed record NativeDspStatus(bool Active, uint SampleRate, uint Channels, float EffectivePreamp, bool CurrentSettingsApplied, int Error);

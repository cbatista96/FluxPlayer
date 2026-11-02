using LibVLCSharp.Shared;
using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;

namespace FluxPlayer.Services;

public partial class MediaService
{
    private readonly SemaphoreSlim _snapshotGate = new(1, 1);
    private readonly CancellationTokenSource _snapshotLifetime = new();
    private readonly ConcurrentDictionary<string, byte> _snapshotTemporaryFiles = new(StringComparer.OrdinalIgnoreCase);
    public bool IsSnapshotBusy => _snapshotGate.CurrentCount == 0;

    /// <summary>Captures a native frame and publishes a correctly encoded new file off the UI thread.</summary>
    public Task<string> CaptureSnapshotAsync(string destination, CancellationToken token = default) => Task.Run(async () =>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _snapshotLifetime.Token);
        if (!await _snapshotGate.WaitAsync(0, linked.Token).ConfigureAwait(false))
            throw new InvalidOperationException("A snapshot is already being captured.");
        string? raw = null, encoded = null;
        try
        {
            CleanupSnapshotTemporaryFiles();
            destination = Path.GetFullPath(destination);
            string extension = Path.GetExtension(destination).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp"))
                throw new ArgumentException("Choose a PNG, JPEG or BMP snapshot destination.", nameof(destination));
            if (File.Exists(destination)) throw new IOException("The snapshot destination already exists.");
            string folder = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(folder);
            raw = Path.Combine(folder, ".flux-snapshot-" + Guid.NewGuid().ToString("N") + ".png");
            _snapshotTemporaryFiles.TryAdd(raw, 0);
            string? source = _currentSourcePath;
            var cropState = PresentationCrop;
            var presentation = Presentation;
            bool cropSnapshot = cropState.Crop != null && Volatile.Read(ref _memoryCropPresenters) > 0;

            // Playing can arrive before video output. Wait briefly without blocking WPF.
            for (int i = 0; i < 40 && MediaPlayer.VoutCount == 0; i++)
                await Task.Delay(50, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            if (_disposed || source != _currentSourcePath) throw new OperationCanceledException("The media source changed.");
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<MediaPlayerSnapshotTakenEventArgs> handler = (_, e) =>
            {
                if (string.Equals(e.Filename, raw, StringComparison.OrdinalIgnoreCase)) completed.TrySetResult(true);
            };
            MediaPlayer.SnapshotTaken += handler;
            try
            {
                // LibVLC 3 always writes PNG, regardless of the destination extension.
                if (!RequestNativePngSnapshot(raw)) throw new IOException("The video output is not ready for a snapshot.");
                // A queued native write cannot be canceled. Wait for it before cleaning up user-canceled work.
                try { await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), _snapshotLifetime.Token).ConfigureAwait(false); }
                catch (TimeoutException e) { throw new IOException("The video snapshot did not finish in time.", e); }
            }
            finally { MediaPlayer.SnapshotTaken -= handler; }
            linked.Token.ThrowIfCancellationRequested();
            if (source != _currentSourcePath) throw new OperationCanceledException("The media source changed.");
            if (!File.Exists(raw) || new FileInfo(raw).Length == 0) throw new IOException("The video snapshot is empty.");
            if (extension == ".png" && !cropSnapshot && !presentation.Transformed && (SubtitleBridge == null || SubtitleBridgeDisabled || MediaPlayer.Spu < 0)) File.Move(raw, destination, false);
            else
            {
                encoded = Path.Combine(folder, ".flux-snapshot-" + Guid.NewGuid().ToString("N") + extension);
                _snapshotTemporaryFiles.TryAdd(encoded, 0);
                BitmapEncoder encoder = extension == ".png" ? new PngBitmapEncoder() : extension == ".bmp" ? new BmpBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 95 };
                using (var input = File.OpenRead(raw))
                {
                    var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    System.Windows.Media.Imaging.BitmapSource picture = decoder.Frames[0];
                    if (!SubtitleBridgeDisabled && SubtitleBridge != null && MediaPlayer.Spu >= 0) picture = SubtitleBridge.Composite(picture, MediaPlayer.State == VLCState.Paused, PlaybackSettings.SubtitleVerticalMargin);
                    if (cropSnapshot && cropState.Crop is { } crop && cropState.Width > 0 && cropState.Height > 0)
                    {
                        int left = (int)((long)crop.Left * picture.PixelWidth / cropState.Width), top = (int)((long)crop.Top * picture.PixelHeight / cropState.Height);
                        int right = (int)((long)(crop.Left + crop.Width) * picture.PixelWidth / cropState.Width), bottom = (int)((long)(crop.Top + crop.Height) * picture.PixelHeight / cropState.Height);
                        picture = new CroppedBitmap(picture, new System.Windows.Int32Rect(left, top, Math.Clamp(right - left, 1, picture.PixelWidth - left), Math.Clamp(bottom - top, 1, picture.PixelHeight - top)));
                    }
                    if (presentation.Transformed)
                    {
                        var scale = presentation.Stretch(picture.PixelWidth, picture.PixelHeight); if (picture.PixelWidth * scale.X > 32768 || picture.PixelWidth * scale.X * picture.PixelHeight * scale.Y > 64_000_000) throw new IOException("The transformed snapshot would exceed its pixel bound."); var transform = new System.Windows.Media.TransformGroup(); transform.Children.Add(new System.Windows.Media.ScaleTransform(scale.X * (presentation.MirrorHorizontal ? -1 : 1), scale.Y * (presentation.FlipVertical ? -1 : 1))); transform.Children.Add(new System.Windows.Media.RotateTransform(presentation.Rotation)); picture = new TransformedBitmap(picture, transform);
                    }
                    encoder.Frames.Add(BitmapFrame.Create(picture));
                    using var output = new FileStream(encoded, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    encoder.Save(output);
                }
                linked.Token.ThrowIfCancellationRequested();
                File.Move(encoded, destination, false);
            }
            return destination;
        }
        catch (Exception e) when (e is NotSupportedException or FormatException)
        {
            throw new IOException("The captured frame could not be encoded in the selected format.", e);
        }
        finally
        {
            CleanupSnapshotTemporaryFiles();
            _snapshotGate.Release();
        }
    }, token);

    private void CleanupSnapshotTemporaryFiles()
    {
        foreach (string path in _snapshotTemporaryFiles.Keys)
        {
            try { File.Delete(path); _snapshotTemporaryFiles.TryRemove(path, out _); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private void StopSnapshotOperations()
    {
        _snapshotLifetime.Cancel();
        _snapshotGate.Wait();
        _snapshotGate.Release();
    }
}

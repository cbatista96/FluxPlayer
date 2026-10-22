namespace FluxPlayer.Services;

public sealed record SnapshotScheduleProgress(int Captured, int Maximum, int Skipped, string? LastFile);

/// <summary>An explicit bounded capture job. No timer exists until RunAsync is called.</summary>
public static class SnapshotSchedule
{
    public static Task<int> RunAsync(TimeSpan interval, int maximum,
        Func<CancellationToken, Task<string?>> capture, IProgress<SnapshotScheduleProgress>? progress = null,
        CancellationToken token = default)
    {
        if (interval < TimeSpan.FromMilliseconds(1) || interval > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(interval));
        if (maximum is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(maximum));
        ArgumentNullException.ThrowIfNull(capture);
        return Task.Run(async () =>
        {
            int count = 0, skipped = 0;
            string? lastFile = null;
            while (count < maximum)
            {
                // Delay after each completed attempt: a slow encoder cannot build a backlog.
                await Task.Delay(interval, token).ConfigureAwait(false);
                string? file = await capture(token).ConfigureAwait(false);
                if (file != null) { count++; lastFile = file; } else skipped++;
                progress?.Report(new(count, maximum, skipped, lastFile));
            }
            return count;
        }, token);
    }
}

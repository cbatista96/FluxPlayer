using FluxPlayer.Models;
using System.Collections.Concurrent;
using System.IO;

namespace FluxPlayer.Services;

public sealed record ConversionJob(string Source, string Destination, long DurationMs = 0);
public sealed record BatchConversionProgress(int Completed, int Total, int Active, double Fraction, string Current);
public static class BatchConversion
{
    private sealed class InlineProgress(Action<double> report) : IProgress<double> { public void Report(double value) => report(value); }
    public static ConversionJob[] Plan(IEnumerable<string> sources, string directory, ConversionProfile profile)
    {
        directory = Path.GetFullPath(directory); var input = sources.Take(501).Select(FfmpegJob.Source).ToArray(); if (input.Length is < 1 or > 500) throw new ArgumentException("Convert 1–500 files per batch.");
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var jobs = new List<ConversionJob>();
        foreach (string source in input)
        {
            string stem = Path.GetFileNameWithoutExtension(source); int suffix = 0; string destination;
            do { if (suffix > 10000) throw new IOException("Too many output name conflicts."); destination = Path.Combine(directory, stem + (suffix == 0 ? "" : " (" + suffix + ")") + profile.Extension); suffix++; }
            while (File.Exists(destination) || Directory.Exists(destination) || reserved.Contains(destination));
            reserved.Add(destination); jobs.Add(new(source, destination));
        }
        return jobs.ToArray();
    }
    public static async Task Run(IReadOnlyList<ConversionJob> jobs, ConversionProfile profile, ConversionOptions options, bool normalize, int concurrency, IProgress<BatchConversionProgress>? progress, CancellationToken token)
    {
        if (jobs.Count is < 1 or > 500 || concurrency is < 1 or > 4 || jobs.Select(job => Path.GetFullPath(job.Destination)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != jobs.Count) throw new ArgumentException("Use 1–500 unique outputs and 1–4 parallel jobs."); options.Validate(profile.Encoder);
        var fractions = new double[jobs.Count]; var gate = new object(); int completed = 0, active = 0;
        void Report(int index, double fraction) { lock (gate) { fractions[index] = fraction; progress?.Report(new(completed, jobs.Count, active, fractions.Average(), Path.GetFileName(jobs[index].Source))); } }
        await Parallel.ForEachAsync(Enumerable.Range(0, jobs.Count), new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = token }, async (index, cancellation) =>
        {
            lock (gate) active++; Report(index, 0);
            try { var job = jobs[index]; await ConversionService.ConvertAsync(job.Source, job.Destination, profile, job.DurationMs, normalize, new InlineProgress(value => Report(index, value)), cancellation, options).ConfigureAwait(false); lock (gate) completed++; Report(index, 1); }
            finally { lock (gate) active--; }
        }).ConfigureAwait(false);
    }
}

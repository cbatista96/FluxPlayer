using FluxPlayer.Models;
using FluxPlayer.Services;
using System.IO;

internal static class SqlAcceptanceChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        string folder = Path.Combine(root, "sql-acceptance-" + Guid.NewGuid().ToString("N"));
        foreach (int number in Enumerable.Range(441, 10))
        {
            await test($"F{number:0000} SQL acceptance: native SQLite persistence and query semantics", async () =>
            {
                string profile = Path.Combine(folder, number.ToString()); var sql = new SqlMediaIndex(profile);
                await sql.Create("Music ñ", CancellationToken.None); string id = (await sql.Catalogs(CancellationToken.None)).Single().Id;
                var entries = Enumerable.Range(0, 125).Select(i => new LibraryEntry { Path = Path.Combine(profile, i + ".flac"), Title = i < 2 ? "Duplicate" : i == 2 ? "100%_literal's" : $"Track {i:000}", Artist = i < 2 ? "ARTIST" : i % 2 == 0 ? "Even" : "Odd", Album = "Album ñ", Genre = "Classical", Year = 2000 + i % 25, Rating = i % 6, DurationMs = i * 1000, Size = i * 100, ModifiedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }).ToArray();
                await sql.Project(id, entries, CancellationToken.None); var all = new SqlFocus("All", false, []);
                switch (number)
                {
                    case 441:
                        var reopened = new SqlMediaIndex(profile); Check((await reopened.Catalogs(CancellationToken.None)).Single().Name == "Music ñ", "Named catalog survives a new index instance.");
                        await sql.Create("Other", CancellationToken.None); Check((await sql.Catalogs(CancellationToken.None)).Length == 2, "Named catalogs remain independent."); break;
                    case 442:
                        await sql.Project(id, [entries[0] with { Rating = 5, Title = "Metadata only" }], CancellationToken.None);
                        Check((await sql.Query(id, all with { Rules = [new("Title", "Equals", "Metadata only")] }, 0, CancellationToken.None)).Single().Rating == 5, "Metadata changes apply even when source size/stamp are unchanged.");
                        using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); bool canceled = false; try { await sql.Project(id, entries, cancel.Token); } catch (OperationCanceledException) { canceled = true; } Check(canceled, "Canceled projection is rejected."); }
                        Check((await sql.Query(id, all with { Rules = [new("Title", "Equals", "Metadata only")] }, 0, CancellationToken.None)).Length == 1, "Cancellation preserves the prior projection."); break;
                    case 443:
                        var and = await sql.Query(id, all with { Rules = [new("Artist", "Equals", "Even"), new("Year", "At least", "2020")] }, 0, CancellationToken.None);
                        Check(and.Length > 0 && and.All(r => r.Artist == "Even" && r.Year >= 2020), "AND Focus combines text and numeric rules.");
                        var or = await sql.Query(id, all with { Any = true, Rules = [new("Artist", "Equals", "Even"), new("Year", "At least", "2020")] }, 0, CancellationToken.None);
                        Check(or.Length > and.Length && or.All(r => r.Artist == "Even" || r.Year >= 2020), "OR Focus combines conditions without escaping its catalog.");
                        Check((await sql.Query(id, all with { Rules = [new("Title", "Contains", "%_literal's")] }, 0, CancellationToken.None)).Length == 1, "LIKE wildcards and quotes are literal search values.");
                        bool rejected = false; try { await sql.Query(id, all with { Sort = "Title; DROP TABLE Entries" }, 0, CancellationToken.None); } catch (ArgumentException) { rejected = true; } Check(rejected, "Sort identifiers are allowlisted."); break;
                    case 444:
                        var facets = await sql.Facets(id, "Artist", CancellationToken.None); Check(facets.Sum(r => r.Count) == 125 && facets.Single(r => r.Value == "ARTIST").Count == 2, "Facet counts include every indexed record."); break;
                    case 445:
                        var page1 = await sql.Query(id, all with { Sort = "DurationMs", Descending = true }, 0, CancellationToken.None); var page2 = await sql.Query(id, all with { Sort = "DurationMs", Descending = true }, 100, CancellationToken.None);
                        Check(page1.Length == 100 && page2.Length == 25 && page1[0].DurationMs == 124000 && page2[^1].DurationMs == 0 && !page1.Select(r => r.Path).Intersect(page2.Select(r => r.Path)).Any(), "Numeric ordering and page boundaries are deterministic."); break;
                    case 446:
                        await sql.Project(id, [entries[1] with { Artist = "artist", Title = "duplicate" }], CancellationToken.None);
                        Check((await sql.DuplicateCandidates(id, CancellationToken.None)).Single().Count == 2, "Duplicate candidates case-fold artist/title and count occurrences."); break;
                    case 447:
                        var focus = new SqlFocus("Favorites", false, [new("Rating", "At least", "4")], "Year", true); await sql.SaveFocus(id, focus, CancellationToken.None);
                        var saved = (await new SqlMediaIndex(profile).FocusViews(id, CancellationToken.None)).Single(); Check(saved.Name == focus.Name && saved.Sort == "Year" && saved.Descending && saved.Rules.Single() == focus.Rules.Single(), "Named Focus criteria survive restart."); break;
                    case 448:
                        string csv = Path.Combine(profile, "reviewed.csv"); var rows = await sql.Query(id, all, 0, CancellationToken.None); rows[0] = rows[0] with { Title = "=formula", Artist = "Quote\" ñ" }; await SqlMediaIndex.ExportRows(csv, rows, CancellationToken.None);
                        string text = await File.ReadAllTextAsync(csv); Check(text.Contains("\"'=formula\"") && text.Contains("Quote\"\" ñ"), "CSV preserves Unicode/quotes and neutralizes formula prefixes.");
                        bool collision = false; try { await SqlMediaIndex.ExportRows(csv, rows, CancellationToken.None); } catch (IOException) { collision = true; } Check(collision && await File.ReadAllTextAsync(csv) == text, "CSV export preserves existing files."); break;
                    case 449:
                        await sql.Maintain(CancellationToken.None); Check((await sql.Query(id, all, 100, CancellationToken.None)).Length == 25, "Checkpoint and VACUUM preserve stored catalogs/entries."); break;
                    case 450:
                        Check(await sql.Integrity(CancellationToken.None) == "ok", "Actual Windows SQLite quick_check passes after projection."); break;
                }
            });
        }
    }
}

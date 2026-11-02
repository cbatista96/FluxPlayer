using FluxPlayer.Models;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FluxPlayer.Services;

public sealed record SqlCatalog(string Id, string Name) { public override string ToString() => Name; }
public sealed record FocusRule(string Field, string Operator, string Value);
public sealed record SqlFocus(string Name, bool Any, FocusRule[] Rules, string Sort = "Title", bool Descending = false);
public sealed record SqlMediaRow(string Path, string Title, string Artist, string Album, string Genre, long Year, long Rating, long DurationMs, long Size, long Ticks);
public sealed record SqlFacet(string Value, long Count);

/// <summary>Explicit derived index, using the Windows SQLite ABI. Does not replace the existing library.</summary>
public sealed class SqlMediaIndex(string profile)
{
    public static readonly string[] Fields = ["Title", "Artist", "Album", "Genre", "Year", "Rating", "DurationMs", "Size"];
    public static readonly string[] Operators = ["Contains", "Equals", "Not equal", "At least", "At most"];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = Path.Combine(profile, "derived-media-index.sqlite");
    private Task<T> With<T>(Func<Database, T> action, CancellationToken token) => Task.Run(async () =>
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); using var db = new Database(_path); using var cancel = token.Register(db.Interrupt); token.ThrowIfCancellationRequested(); return action(db); }
        finally { _gate.Release(); }
    }, token);
    public Task<SqlCatalog[]> Catalogs(CancellationToken token) => With(db => db.Rows("SELECT Id,Name FROM Catalogs ORDER BY Name").Select(r => new SqlCatalog(r[0], r[1])).ToArray(), token);
    public Task Create(string name, CancellationToken token) => With(db => { if (name.Length is < 1 or > 100 || name.Any(char.IsControl)) throw new ArgumentException("Use a bounded catalog name."); if (int.Parse(db.Rows("SELECT COUNT(*) FROM Catalogs")[0][0], CultureInfo.InvariantCulture) >= 32) throw new IOException("Use at most 32 derived catalogs."); db.Execute("INSERT INTO Catalogs(Id,Name) VALUES(?,?)", Guid.NewGuid().ToString("N"), name); return true; }, token);
    public Task<int> Project(string catalog, IEnumerable<LibraryEntry> source, CancellationToken token)
    {
        var entries = source.Take(200001).ToArray(); if (entries.Length > 200000) throw new ArgumentException("Project up to 200,000 entries per operation.");
        return With(db =>
        {
            if (!db.Rows("SELECT Id FROM Catalogs WHERE Id=?", catalog).Any()) throw new ArgumentException("Select an existing catalog.");
            db.Exec("BEGIN IMMEDIATE");
            try
            {
                db.ExecuteMany("INSERT INTO Entries(Catalog,Path,Title,Artist,Album,Genre,Year,Rating,DurationMs,Size,Ticks) VALUES(?,?,?,?,?,?,?,?,?,?,?) ON CONFLICT(Catalog,Path) DO UPDATE SET Title=excluded.Title,Artist=excluded.Artist,Album=excluded.Album,Genre=excluded.Genre,Year=excluded.Year,Rating=excluded.Rating,DurationMs=excluded.DurationMs,Size=excluded.Size,Ticks=excluded.Ticks WHERE Entries.Ticks<>excluded.Ticks OR Entries.Size<>excluded.Size OR Entries.Title<>excluded.Title OR Entries.Artist<>excluded.Artist OR Entries.Album<>excluded.Album OR Entries.Genre<>excluded.Genre OR Entries.Year<>excluded.Year OR Entries.Rating<>excluded.Rating OR Entries.DurationMs<>excluded.DurationMs", entries.Select(row => new object[] { catalog, row.Path, row.Title, row.Artist, row.Album, row.Genre, row.Year, row.Rating, row.DurationMs, row.Size, row.ModifiedUtc.Ticks }), token);
                db.Exec("COMMIT"); return entries.Length;
            }
            catch { try { db.Exec("ROLLBACK"); } catch (IOException) { } throw; }
        }, token);
    }
    private static (string Where, object[] Parameters) Filter(string catalog, SqlFocus focus)
    {
        if (focus.Name.Length > 100 || focus.Rules == null || focus.Rules.Length > 20 || !Fields.Contains(focus.Sort)) throw new ArgumentException("Use up to twenty Focus rules and an allowlisted sort field.");
        var parameters = new List<object> { catalog }; var conditions = new List<string>();
        foreach (var rule in focus.Rules)
        {
            if (!Fields.Contains(rule.Field) || !Operators.Contains(rule.Operator) || rule.Value.Length > 500) throw new ArgumentException("Check rule field/operator/value bounds.");
            bool numeric = rule.Field is "Year" or "Rating" or "DurationMs" or "Size";
            if (numeric && rule.Operator == "Contains") throw new ArgumentException("Numeric rules require equality or a numeric bound.");
            object value = numeric ? long.TryParse(rule.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) ? number : throw new ArgumentException("Use a whole number for numeric rules.") : rule.Value;
            string op = rule.Operator switch { "Equals" => "=", "Not equal" => "<>", "At least" => ">=", "At most" => "<=", _ => "LIKE" };
            if (op == "LIKE") value = "%" + rule.Value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            conditions.Add(rule.Field + " " + op + " ?" + (op == "LIKE" ? " ESCAPE '\\'" : "")); parameters.Add(value);
        }
        return ("Catalog=?" + (conditions.Count == 0 ? "" : " AND (" + string.Join(focus.Any ? " OR " : " AND ", conditions) + ")"), parameters.ToArray());
    }
    public Task<SqlMediaRow[]> Query(string catalog, SqlFocus focus, int offset, CancellationToken token)
    {
        var filter = Filter(catalog, focus); if (offset is < 0 or > 1000000) throw new ArgumentException("Use an offset up to one million.");
        return With(db => db.Rows("SELECT Path,Title,Artist,Album,Genre,Year,Rating,DurationMs,Size,Ticks FROM Entries WHERE " + filter.Where + " ORDER BY " + focus.Sort + (focus.Descending ? " DESC" : " ASC") + ",Path LIMIT 100 OFFSET ?", filter.Parameters.Append((object)offset).ToArray()).Select(r => new SqlMediaRow(r[0], r[1], r[2], r[3], r[4], long.Parse(r[5], CultureInfo.InvariantCulture), long.Parse(r[6], CultureInfo.InvariantCulture), long.Parse(r[7], CultureInfo.InvariantCulture), long.Parse(r[8], CultureInfo.InvariantCulture), long.Parse(r[9], CultureInfo.InvariantCulture))).ToArray(), token);
    }
    public Task<SqlFacet[]> Facets(string catalog, string field, CancellationToken token)
    { if (!Fields.Contains(field)) throw new ArgumentException("Choose an indexed facet."); return With(db => db.Rows("SELECT " + field + ",COUNT(*) FROM Entries WHERE Catalog=? GROUP BY " + field + " ORDER BY COUNT(*) DESC LIMIT 100", catalog).Select(r => new SqlFacet(r[0], long.Parse(r[1], CultureInfo.InvariantCulture))).ToArray(), token); }
    public Task<SqlFacet[]> DuplicateCandidates(string catalog, CancellationToken token) => With(db => db.Rows("SELECT lower(Artist)||' / '||lower(Title),COUNT(*) FROM Entries WHERE Catalog=? AND Artist<>'' AND Title<>'' GROUP BY lower(Artist),lower(Title) HAVING COUNT(*)>1 ORDER BY COUNT(*) DESC LIMIT 100", catalog).Select(r => new SqlFacet(r[0], long.Parse(r[1], CultureInfo.InvariantCulture))).ToArray(), token);
    public Task SaveFocus(string catalog, SqlFocus focus, CancellationToken token) { _ = Filter(catalog, focus); if (focus.Name.Trim() == "") throw new ArgumentException("Name the Focus view."); return With(db => { db.Execute("INSERT INTO FocusViews(Catalog,Name,Json) VALUES(?,?,?) ON CONFLICT(Catalog,Name) DO UPDATE SET Json=excluded.Json", catalog, focus.Name, JsonSerializer.Serialize(focus)); return true; }, token); }
    public Task<SqlFocus[]> FocusViews(string catalog, CancellationToken token) => With(db => db.Rows("SELECT Json FROM FocusViews WHERE Catalog=? ORDER BY Name LIMIT 100", catalog).Select(r => JsonSerializer.Deserialize<SqlFocus>(r[0]) ?? throw new IOException("Invalid saved Focus view.")).ToArray(), token);
    public Task<string> Maintain(CancellationToken token) => With(db => { db.Exec("PRAGMA wal_checkpoint(TRUNCATE); VACUUM"); return "Checkpoint and database compaction requested for the derived index only."; }, token);
    public Task<string> Integrity(CancellationToken token) => With(db => string.Join('\n', db.Rows("PRAGMA quick_check").Take(100).Select(r => string.Join(" · ", r))), token);
    public static Task ExportRows(string path, SqlMediaRow[] rows, CancellationToken token)
    {
        if (rows.Length > 500) throw new ArgumentException("Export at most 500 reviewed rows.");
        string Cell(object value) { string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""; string beginning = text.TrimStart(); if (beginning.StartsWith('=') || beginning.StartsWith('+') || beginning.StartsWith('-') || beginning.StartsWith('@')) text = "'" + text; return "\"" + text.Replace("\"", "\"\"") + "\""; }
        string text = "Path,Title,Artist,Album,Genre,Year,Rating,DurationMs,Size\n" + string.Join('\n', rows.Select(r => string.Join(',', new object[] { r.Path, r.Title, r.Artist, r.Album, r.Genre, r.Year, r.Rating, r.DurationMs, r.Size }.Select(Cell))));
        return OfflineMediaJob.Publish(path, ".csv", [], staging => File.WriteAllTextAsync(staging, text, token), token);
    }
    private sealed class Database : IDisposable
    {
        private IntPtr _handle;
        public Database(string path)
        {
            if (Native.Version() < 3024000) throw new IOException("This Windows SQLite runtime predates UPSERT support (3.24). Update Windows before using the derived index.");
            int result = Native.Open(path, out _handle, 2 | 4 | 0x10000, IntPtr.Zero);
            if (result != 0) { string message = Error(); Dispose(); throw new IOException(message); }
            Native.Busy(_handle, 3000);
            try { Exec("PRAGMA journal_mode=WAL; PRAGMA cache_size=-8192; CREATE TABLE IF NOT EXISTS Catalogs(Id TEXT PRIMARY KEY,Name TEXT NOT NULL UNIQUE); CREATE TABLE IF NOT EXISTS Entries(Catalog TEXT NOT NULL,Path TEXT COLLATE NOCASE NOT NULL,Title TEXT COLLATE NOCASE NOT NULL,Artist TEXT COLLATE NOCASE NOT NULL,Album TEXT COLLATE NOCASE NOT NULL,Genre TEXT COLLATE NOCASE NOT NULL,Year INTEGER,Rating INTEGER,DurationMs INTEGER,Size INTEGER,Ticks INTEGER,PRIMARY KEY(Catalog,Path)); CREATE INDEX IF NOT EXISTS EntriesArtist ON Entries(Catalog,Artist,Album,Title); CREATE INDEX IF NOT EXISTS EntriesYear ON Entries(Catalog,Year); CREATE TABLE IF NOT EXISTS FocusViews(Catalog TEXT,Name TEXT,Json TEXT,PRIMARY KEY(Catalog,Name));"); } catch { Dispose(); throw; }
        }
        private string Error() => Marshal.PtrToStringUTF8(Native.Error(_handle)) ?? "SQLite operation failed.";
        public void Interrupt() { if (_handle != IntPtr.Zero) Native.Interrupt(_handle); }
        public void Exec(string sql) { int code = Native.Exec(_handle, sql, IntPtr.Zero, IntPtr.Zero, out var error); if (error != IntPtr.Zero) Native.Free(error); if (code != 0) throw new IOException(Error()); }
        private IntPtr Prepare(string sql, object[] values)
        {
            int code = Native.Prepare(_handle, sql, -1, out var statement, IntPtr.Zero); if (code != 0) { if (statement != IntPtr.Zero) Native.Finalize(statement); throw new IOException(Error()); }
            try { for (int i = 0; i < values.Length; i++) { code = values[i] is string text ? Native.BindText(statement, i + 1, text, -1, new IntPtr(-1)) : Native.BindInteger(statement, i + 1, Convert.ToInt64(values[i], CultureInfo.InvariantCulture)); if (code != 0) throw new IOException(Error()); } return statement; }
            catch { Native.Finalize(statement); throw; }
        }
        public void Execute(string sql, params object[] values) { var statement = Prepare(sql, values); try { if (Native.Step(statement) != 101) throw new IOException(Error()); } finally { Native.Finalize(statement); } }
        public void ExecuteMany(string sql, IEnumerable<object[]> rows, CancellationToken token)
        {
            var statement = Prepare(sql, []);
            try
            {
                foreach (var values in rows)
                {
                    token.ThrowIfCancellationRequested();
                    for (int i = 0; i < values.Length; i++) { int bound = values[i] is string text ? Native.BindText(statement, i + 1, text, -1, new IntPtr(-1)) : Native.BindInteger(statement, i + 1, Convert.ToInt64(values[i], CultureInfo.InvariantCulture)); if (bound != 0) throw new IOException(Error()); }
                    if (Native.Step(statement) != 101 || Native.Reset(statement) != 0 || Native.ClearBindings(statement) != 0) throw new IOException(Error());
                }
            }
            finally { Native.Finalize(statement); }
        }
        public List<string[]> Rows(string sql, params object[] values)
        {
            var statement = Prepare(sql, values); try { var rows = new List<string[]>(); int code; while ((code = Native.Step(statement)) == 100) { var row = new string[Native.Columns(statement)]; for (int i = 0; i < row.Length; i++) row[i] = Marshal.PtrToStringUTF8(Native.Text(statement, i)) ?? ""; rows.Add(row); if (rows.Count > 1000) throw new IOException("The index query exceeded its row bound."); } if (code != 101) throw new IOException(Error()); return rows; } finally { Native.Finalize(statement); }
        }
        public void Dispose() { if (_handle != IntPtr.Zero) { Native.Close(_handle); _handle = IntPtr.Zero; } }
    }
    private static class Native
    {
        private const string Library = "winsqlite3.dll";
        [DllImport(Library, EntryPoint = "sqlite3_libversion_number", CallingConvention = CallingConvention.Cdecl)] internal static extern int Version();
        [DllImport(Library, EntryPoint = "sqlite3_open_v2", CallingConvention = CallingConvention.Cdecl)] internal static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Library, EntryPoint = "sqlite3_close_v2", CallingConvention = CallingConvention.Cdecl)] internal static extern int Close(IntPtr db);
        [DllImport(Library, EntryPoint = "sqlite3_busy_timeout", CallingConvention = CallingConvention.Cdecl)] internal static extern int Busy(IntPtr db, int milliseconds);
        [DllImport(Library, EntryPoint = "sqlite3_errmsg", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr Error(IntPtr db);
        [DllImport(Library, EntryPoint = "sqlite3_exec", CallingConvention = CallingConvention.Cdecl)] internal static extern int Exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr context, out IntPtr error);
        [DllImport(Library, EntryPoint = "sqlite3_free", CallingConvention = CallingConvention.Cdecl)] internal static extern void Free(IntPtr memory);
        [DllImport(Library, EntryPoint = "sqlite3_prepare_v2", CallingConvention = CallingConvention.Cdecl)] internal static extern int Prepare(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr statement, IntPtr tail);
        [DllImport(Library, EntryPoint = "sqlite3_bind_text", CallingConvention = CallingConvention.Cdecl)] internal static extern int BindText(IntPtr statement, int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int bytes, IntPtr destructor);
        [DllImport(Library, EntryPoint = "sqlite3_bind_int64", CallingConvention = CallingConvention.Cdecl)] internal static extern int BindInteger(IntPtr statement, int index, long value);
        [DllImport(Library, EntryPoint = "sqlite3_step", CallingConvention = CallingConvention.Cdecl)] internal static extern int Step(IntPtr statement);
        [DllImport(Library, EntryPoint = "sqlite3_finalize", CallingConvention = CallingConvention.Cdecl)] internal static extern int Finalize(IntPtr statement);
        [DllImport(Library, EntryPoint = "sqlite3_reset", CallingConvention = CallingConvention.Cdecl)] internal static extern int Reset(IntPtr statement);
        [DllImport(Library, EntryPoint = "sqlite3_clear_bindings", CallingConvention = CallingConvention.Cdecl)] internal static extern int ClearBindings(IntPtr statement);
        [DllImport(Library, EntryPoint = "sqlite3_column_count", CallingConvention = CallingConvention.Cdecl)] internal static extern int Columns(IntPtr statement);
        [DllImport(Library, EntryPoint = "sqlite3_column_text", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr Text(IntPtr statement, int column);
        [DllImport(Library, EntryPoint = "sqlite3_interrupt", CallingConvention = CallingConvention.Cdecl)] internal static extern void Interrupt(IntPtr db);
    }
}

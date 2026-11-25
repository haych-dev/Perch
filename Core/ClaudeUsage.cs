using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Perch.Core;

/// <summary>
/// Claude Code usage, read from the session history Claude Code writes on this PC
/// (%UserProfile%\.claude\projects\**\*.jsonl). Nothing is sent anywhere and no sign-in is read.
/// Files are read incrementally: each scan only reads what was appended since the last one.
/// </summary>
public sealed class ClaudeUsage
{
    public static ClaudeUsage Instance { get; } = new();

    public sealed record Reply(DateTime Utc, string Model, long Input, long Output, long CacheWrite, long CacheRead, string Session, string Project)
    {
        /// <summary>New tokens the reply cost: everything except what came back from the prompt cache.</summary>
        public long Tokens => Input + Output + CacheWrite;
    }

    public sealed record Window(DateTime Start, DateTime End, int Replies, long Tokens, long CacheRead);

    public sealed record Snapshot(
        bool Installed,
        Window? Current,
        long[] Days,              // tokens per local day, oldest first, today last (7 entries)
        long WeekTokens,
        int WeekReplies,
        int WeekSessions,
        List<(string Model, double Share)> Models,
        List<(string Project, DateTime Last)> Active,
        (double? Session, double? Week, DateTime At)? Plan);

    public event Action? Changed;
    public Snapshot? Current { get; private set; }

    static readonly TimeSpan Keep = TimeSpan.FromDays(8);
    sealed class FileState { public long Offset; }
    readonly Dictionary<string, FileState> _files = new(StringComparer.OrdinalIgnoreCase);
    // Claude Code writes one line per content block, all carrying the same message; keep one per message.
    readonly Dictionary<string, Reply> _replies = new();
    readonly System.Threading.Timer _timer;
    int _busy, _users;

    ClaudeUsage() { _timer = new System.Threading.Timer(_ => Scan(), null, Timeout.Infinite, Timeout.Infinite); }

    /// <summary>Widgets call this while they are on the dock; scanning stops when none are.</summary>
    public void Start() { if (Interlocked.Increment(ref _users) == 1) _timer.Change(0, 30_000); }
    public void Stop() { if (Interlocked.Decrement(ref _users) == 0) _timer.Change(Timeout.Infinite, Timeout.Infinite); }
    public void ScanSoon() => _timer.Change(0, 30_000);

    public static string Root
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            return dir;
        }
    }

    void Scan()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var projects = Path.Combine(Root, "projects");
            bool installed = Directory.Exists(projects);
            var cutoff = DateTime.UtcNow - Keep;
            if (installed)
            {
                foreach (var path in Directory.EnumerateFiles(projects, "*.jsonl", SearchOption.AllDirectories))
                {
                    try
                    {
                        var info = new FileInfo(path);
                        if (info.LastWriteTimeUtc < cutoff) continue;
                        if (!_files.TryGetValue(path, out var st)) _files[path] = st = new FileState();
                        if (info.Length == st.Offset) continue;
                        if (info.Length < st.Offset) st.Offset = 0; // rewritten
                        st.Offset = ReadFrom(path, st.Offset);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                foreach (var key in _replies.Where(r => r.Value.Utc < cutoff).Select(r => r.Key).ToList()) _replies.Remove(key);
            }
            var snap = Build(installed);
            Application.Current?.Dispatcher.BeginInvoke(() => { Current = snap; Changed?.Invoke(); });
        }
        catch (Exception ex) { Log.Error(ex, "Claude Code history could not be read"); }
        finally { Volatile.Write(ref _busy, 0); }
    }

    /// <summary>Reads whole lines from <paramref name="offset"/> on and returns the offset after the last complete line.</summary>
    long ReadFrom(string path, long offset)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        fs.Position = offset;
        var buf = new byte[1 << 20];
        using var carry = new MemoryStream();
        long pos = offset, lastLineEnd = offset;
        int n;
        while ((n = fs.Read(buf, 0, buf.Length)) > 0)
        {
            int start = 0;
            for (int i = 0; i < n; i++)
            {
                if (buf[i] != (byte)'\n') continue;
                if (carry.Length > 0)
                {
                    carry.Write(buf, start, i - start);
                    Line(carry.GetBuffer().AsSpan(0, (int)carry.Length));
                    carry.SetLength(0);
                }
                else Line(buf.AsSpan(start, i - start));
                start = i + 1;
                lastLineEnd = pos + i + 1;
            }
            carry.Write(buf, start, n - start);
            pos += n;
        }
        return lastLineEnd;
    }

    void Line(ReadOnlySpan<byte> line)
    {
        if (line.IndexOf("\"usage\""u8) < 0 || line.IndexOf("\"assistant\""u8) < 0) return;
        try
        {
            using var doc = JsonDocument.Parse(line.ToArray());
            var r = doc.RootElement;
            if (Str(r, "type") != "assistant" || !r.TryGetProperty("message", out var m) || !m.TryGetProperty("usage", out var u)) return;
            string model = Str(m, "model");
            if (model.Length == 0 || model.StartsWith('<')) return; // "<synthetic>" replies cost nothing
            if (!DateTime.TryParse(Str(r, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)) return;
            string key = Str(m, "id") + "|" + Str(r, "requestId");
            var reply = new Reply(at, model, Num(u, "input_tokens"), Num(u, "output_tokens"), Num(u, "cache_creation_input_tokens"), Num(u, "cache_read_input_tokens"),
                Str(r, "sessionId"), ProjectName(Str(r, "cwd")));
            // Later lines of the same message can carry a fuller count.
            if (!_replies.TryGetValue(key, out var old) || reply.Output > old.Output) _replies[key] = reply;
        }
        catch (JsonException) { }
    }

    static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static long Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
    static string ProjectName(string cwd) => cwd.Length == 0 ? "" : Path.GetFileName(cwd.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : cwd;

    Snapshot Build(bool installed)
    {
        var all = _replies.Values.OrderBy(r => r.Utc).ToList();
        var nowUtc = DateTime.UtcNow;

        // Claude's 5-hour windows begin with the first message after the last one ended. Rebuild them
        // the way usage tools do: a window starts on the hour of its first reply and lasts five hours.
        Window? current = null;
        DateTime start = default, end = default;
        int count = 0; long tokens = 0, cache = 0;
        foreach (var r in all)
        {
            if (count == 0 || r.Utc >= end)
            {
                start = new DateTime(r.Utc.Year, r.Utc.Month, r.Utc.Day, r.Utc.Hour, 0, 0, DateTimeKind.Utc);
                end = start.AddHours(5);
                count = 0; tokens = 0; cache = 0;
            }
            count++; tokens += r.Tokens; cache += r.CacheRead;
        }
        if (count > 0 && nowUtc < end) current = new Window(start.ToLocalTime(), end.ToLocalTime(), count, tokens, cache);

        var today = DateTime.Today;
        var days = new long[7];
        var week = all.Where(r => r.Utc.ToLocalTime().Date > today.AddDays(-7)).ToList();
        foreach (var r in week) days[6 - (today - r.Utc.ToLocalTime().Date).Days] += r.Tokens;
        long weekTokens = week.Sum(r => r.Tokens);
        var models = week.GroupBy(r => ModelName(r.Model))
            .Select(g => (g.Key, Share: weekTokens > 0 ? g.Sum(r => r.Tokens) / (double)weekTokens : 0))
            .OrderByDescending(x => x.Share).ToList();
        var active = all.Where(r => nowUtc - r.Utc < TimeSpan.FromMinutes(10))
            .GroupBy(r => r.Session)
            .Select(g => (Project: g.Last().Project, Last: g.Max(r => r.Utc).ToLocalTime()))
            .OrderByDescending(x => x.Last).ToList();

        return new Snapshot(installed, current, days, weekTokens, week.Count, week.Select(r => r.Session).Distinct().Count(), models, active, ReadPlan());
    }

    /// <summary>
    /// Plan limits aren't in the history. A status line script can save them (Claude Code passes them to it),
    /// as "five_hour=NN seven_day=NN" in .claude\state\rate-limits.txt; use them while they're recent.
    /// </summary>
    static (double? Session, double? Week, DateTime At)? ReadPlan()
    {
        try
        {
            var path = Path.Combine(Root, "state", "rate-limits.txt");
            if (!File.Exists(path)) return null;
            var at = File.GetLastWriteTime(path);
            if (DateTime.Now - at > TimeSpan.FromMinutes(30)) return null;
            double? five = null, week = null;
            foreach (var part in File.ReadAllText(path).Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2 || !double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                if (kv[0] == "five_hour") five = v;
                else if (kv[0] == "seven_day") week = v;
            }
            return five == null && week == null ? null : (five, week, at);
        }
        catch { return null; }
    }

    /// <summary>"claude-opus-5-5" → "Opus 5.5", "claude-3-5-sonnet-20241022" → "Sonnet 3.5".</summary>
    public static string ModelName(string id)
    {
        var parts = id.Split('-').Where(p => p != "claude" && !(p.Length == 8 && p.All(char.IsDigit))).ToList();
        var name = parts.FirstOrDefault(p => !p.All(char.IsDigit));
        if (name == null) return id;
        var version = string.Join(".", parts.Where(p => p.All(char.IsDigit)));
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name) + (version.Length > 0 ? " " + version : "");
    }

    /// <summary>1234567 → "1.2M".</summary>
    public static string Short(long n) => n switch
    {
        >= 1_000_000_000 => (n / 1e9).ToString(n >= 10_000_000_000 ? "0" : "0.0", CultureInfo.CurrentCulture) + "B",
        >= 1_000_000 => (n / 1e6).ToString(n >= 10_000_000 ? "0" : "0.0", CultureInfo.CurrentCulture) + "M",
        >= 1_000 => (n / 1e3).ToString(n >= 10_000 ? "0" : "0.0", CultureInfo.CurrentCulture) + "K",
        _ => n.ToString(CultureInfo.CurrentCulture),
    };
}

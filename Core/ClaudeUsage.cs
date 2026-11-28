using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Text.Json;
using System.Windows;

namespace Perch.Core;

/// <summary>
/// Claude Code usage, read from the session history Claude Code writes on this PC
/// (%UserProfile%\.claude\projects\**\*.jsonl). Nothing is sent anywhere and no sign-in is read.
///
/// Kept cheap on purpose:
/// - Files are read incrementally: only what was appended since the last read.
/// - A file watcher says which files changed, so a quiet scan touches nothing; the whole folder is
///   only listed at start-up and every 10 minutes as a safety net.
/// - Lines are parsed in place with Utf8JsonReader (no copies, no DOM), and only lines that carry usage.
/// - Each reply is a 32-byte struct; model, session and project names are stored once.
/// - The summary is only rebuilt when something changed or a time boundary (window end, midnight) passed.
/// </summary>
public sealed class ClaudeUsage
{
    public static ClaudeUsage Instance { get; } = new();

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

    /// <summary>One reply. New tokens are everything except what came back from the prompt cache.</summary>
    struct Reply
    {
        public long Ticks; // UTC
        public int Input, Output, CacheWrite, CacheRead;
        public int Session;
        public short Model, Project;
        public readonly long Tokens => (long)Input + Output + CacheWrite;
    }

    static readonly TimeSpan Keep = TimeSpan.FromDays(8);
    static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(10);
    static readonly TimeSpan ActiveFor = TimeSpan.FromMinutes(10);
    const int ScanEveryMs = 30_000;

    // Read state (only touched by the scan, which never runs twice at once).
    readonly Dictionary<string, long> _offsets = new(StringComparer.OrdinalIgnoreCase);
    List<Reply> _replies = [];
    Dictionary<ulong, int> _byKey = []; // message id + request id → index in _replies
    Names _models = new(), _sessions = new(), _projects = new();
    readonly byte[] _buf = new byte[64 * 1024]; // under the large-object threshold
    byte[] _carry = new byte[16 * 1024];
    int _carryLen;
    char[] _chars = new char[512];
    long _cutoffTicks;
    bool _dirtyData = true;
    DateTime _nextSweep, _validUntil, _planStamp;
    (double? Session, double? Week, DateTime At)? _plan;

    // Shared with the watcher's callbacks.
    readonly object _gate = new();
    readonly HashSet<string> _changedFiles = new(StringComparer.OrdinalIgnoreCase);
    bool _sweep = true;
    FileSystemWatcher? _watcher;

    readonly Timer _timer;
    int _busy, _users;

    ClaudeUsage() { _timer = new Timer(_ => Scan(), null, Timeout.Infinite, Timeout.Infinite); }

    /// <summary>Widgets call this while they are on the dock; reading stops when none are.</summary>
    public void Start()
    {
        if (Interlocked.Increment(ref _users) != 1) return;
        lock (_gate) _sweep = true; // writes made while stopped weren't watched
        _timer.Change(0, ScanEveryMs);
    }

    public void Stop()
    {
        if (Interlocked.Decrement(ref _users) != 0) return;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        lock (_gate) { _watcher?.Dispose(); _watcher = null; }
    }

    public static string Root
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            return dir;
        }
    }

    // ───────────────────────── Scanning ─────────────────────────

    void Scan()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var root = Root;
            var projects = Path.Combine(root, "projects");
            bool installed = Directory.Exists(projects);
            var now = DateTime.UtcNow;
            var cutoff = now - Keep;
            _cutoffTicks = cutoff.Ticks;

            if (installed)
            {
                bool sweep;
                string[] changed;
                lock (_gate)
                {
                    if (_watcher == null && Volatile.Read(ref _users) > 0) Watch(projects);
                    sweep = _sweep || _watcher == null || now >= _nextSweep;
                    _sweep = false;
                    changed = _changedFiles.Count == 0 ? [] : [.. _changedFiles];
                    _changedFiles.Clear();
                }
                if (sweep) Sweep(projects, cutoff, now);
                else foreach (var path in changed) ReadChanged(path);
                Prune();
            }

            bool planChanged = CheckPlan(root);
            if (_dirtyData || planChanged || now >= _validUntil || Current?.Installed != installed)
            {
                _dirtyData = false;
                var snap = Build(installed, now);
                Application.Current?.Dispatcher.BeginInvoke(() => { Current = snap; Changed?.Invoke(); });
            }
            TrimBuffers();
        }
        catch (Exception ex) { Log.Error(ex, "Claude Code history could not be read"); }
        finally { Volatile.Write(ref _busy, 0); }
    }

    /// <summary>Watches the history folder so a scan only opens the files that were written to.</summary>
    void Watch(string projects)
    {
        try
        {
            var w = new FileSystemWatcher(projects, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 16 * 1024,
            };
            FileSystemEventHandler mark = (_, e) => { lock (_gate) _changedFiles.Add(e.FullPath); };
            w.Changed += mark;
            w.Created += mark;
            w.Renamed += (_, e) => { lock (_gate) _changedFiles.Add(e.FullPath); };
            w.Error += (_, _) => { lock (_gate) _sweep = true; }; // events were lost: list everything next time
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch (Exception ex) { Log.Error(ex, "Claude Code history can't be watched"); }
    }

    /// <summary>Lists every history file written to recently and reads what's new in each.</summary>
    void Sweep(string projects, DateTime cutoff, DateTime now)
    {
        _nextSweep = now + SweepEvery;
        var files = new FileSystemEnumerable<(string Path, long Length)>(projects,
            (ref FileSystemEntry e) => (e.ToFullPath(), e.Length),
            new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 })
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) =>
                !e.IsDirectory && e.LastWriteTimeUtc.UtcDateTime >= cutoff && e.FileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase),
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, length) in files)
        {
            seen.Add(path);
            Read(path, length);
        }
        // Forget files that went quiet long ago, so the table doesn't grow forever.
        if (_offsets.Count > seen.Count)
            foreach (var old in _offsets.Keys.Where(k => !seen.Contains(k)).ToList()) _offsets.Remove(old);
    }

    void ReadChanged(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists) Read(path, info.Length);
            else _offsets.Remove(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    void Read(string path, long length)
    {
        _offsets.TryGetValue(path, out long offset);
        if (length == offset) return;
        if (length < offset) offset = 0; // the file was rewritten
        try { _offsets[path] = ReadFrom(path, offset); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Reads whole lines from <paramref name="offset"/> on and returns the offset after the last complete line.</summary>
    long ReadFrom(string path, long offset)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0, FileOptions.SequentialScan);
        fs.Position = offset;
        long pos = offset, lastLineEnd = offset;
        _carryLen = 0;
        int n;
        while ((n = fs.Read(_buf, 0, _buf.Length)) > 0)
        {
            var chunk = _buf.AsSpan(0, n);
            int start = 0, nl;
            while ((nl = chunk[start..].IndexOf((byte)'\n')) >= 0)
            {
                var piece = chunk.Slice(start, nl);
                if (_carryLen > 0)
                {
                    Carry(piece);
                    Line(_carry.AsSpan(0, _carryLen));
                    _carryLen = 0;
                }
                else Line(piece);
                start += nl + 1;
                lastLineEnd = pos + start;
            }
            if (start < n) Carry(chunk[start..]);
            pos += n;
        }
        _carryLen = 0;
        return lastLineEnd;
    }

    /// <summary>Keeps the start of a line that runs past the read buffer.</summary>
    void Carry(ReadOnlySpan<byte> part)
    {
        if (_carryLen + part.Length > _carry.Length)
            Array.Resize(ref _carry, Math.Max(_carry.Length * 2, _carryLen + part.Length));
        part.CopyTo(_carry.AsSpan(_carryLen));
        _carryLen += part.Length;
    }

    /// <summary>Lets go of buffers that grew for one very long line.</summary>
    void TrimBuffers()
    {
        if (_carry.Length > 256 * 1024) _carry = new byte[16 * 1024];
        if (_chars.Length > 4096) _chars = new char[512];
    }

    // ───────────────────────── Parsing ─────────────────────────

    struct Parsed
    {
        public bool Assistant, HasUsage, Synthetic;
        public DateTime At;
        public ulong Id, Request;
        public int Model, Session, Project;
        public long Input, Output, CacheWrite, CacheRead;
    }

    void Line(ReadOnlySpan<byte> line)
    {
        // Most bytes are tool output in user lines; only assistant lines carry usage. Both searches are vectorised.
        if (line.IndexOf("\"usage\""u8) < 0 || line.IndexOf("\"assistant\""u8) < 0) return;
        try
        {
            var r = new Utf8JsonReader(line);
            if (!r.Read() || r.TokenType != JsonTokenType.StartObject) return;
            var p = new Parsed { Model = -1, Session = -1, Project = -1 };
            while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
            {
                int prop = r.ValueTextEquals("message"u8) ? 1
                    : r.ValueTextEquals("type"u8) ? 2
                    : r.ValueTextEquals("timestamp"u8) ? 3
                    : r.ValueTextEquals("requestId"u8) ? 4
                    : r.ValueTextEquals("sessionId"u8) ? 5
                    : r.ValueTextEquals("cwd"u8) ? 6 : 0;
                r.Read();
                bool str = r.TokenType == JsonTokenType.String;
                if (prop == 1 && r.TokenType == JsonTokenType.StartObject) ReadMessage(ref r, ref p);
                else if (prop == 2 && str) p.Assistant = r.ValueTextEquals("assistant"u8);
                else if (prop == 3 && str) { if (r.TryGetDateTime(out var at)) p.At = at.ToUniversalTime(); }
                else if (prop == 4 && str) p.Request = Hash(r.ValueSpan);
                else if (prop == 5 && str) p.Session = _sessions.Id(Text(ref r));
                else if (prop == 6 && str) p.Project = _projects.Id(ProjectName(Text(ref r)));
                else r.Skip();
            }
            if (!p.Assistant || !p.HasUsage || p.Synthetic || p.Model < 0 || p.At.Ticks < _cutoffTicks) return;
            if (p.Session < 0) p.Session = _sessions.Id("");
            if (p.Project < 0) p.Project = _projects.Id("");

            var reply = new Reply
            {
                Ticks = p.At.Ticks,
                Input = Clamp(p.Input), Output = Clamp(p.Output), CacheWrite = Clamp(p.CacheWrite), CacheRead = Clamp(p.CacheRead),
                Session = p.Session, Model = (short)p.Model, Project = (short)p.Project,
            };
            // Claude Code writes one line per content block, all carrying the same message; keep one per message,
            // the one with the fullest count.
            ulong key = p.Id ^ (p.Request * 0x9E3779B97F4A7C15UL);
            if (_byKey.TryGetValue(key, out int i))
            {
                if (reply.Output > _replies[i].Output) { _replies[i] = reply; _dirtyData = true; }
            }
            else
            {
                _byKey[key] = _replies.Count;
                _replies.Add(reply);
                _dirtyData = true;
            }
        }
        catch (JsonException) { }
    }

    void ReadMessage(ref Utf8JsonReader r, ref Parsed p)
    {
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            int prop = r.ValueTextEquals("usage"u8) ? 1 : r.ValueTextEquals("model"u8) ? 2 : r.ValueTextEquals("id"u8) ? 3 : 0;
            r.Read();
            if (prop == 1 && r.TokenType == JsonTokenType.StartObject)
            {
                p.HasUsage = true;
                while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
                {
                    int field = r.ValueTextEquals("input_tokens"u8) ? 1
                        : r.ValueTextEquals("output_tokens"u8) ? 2
                        : r.ValueTextEquals("cache_creation_input_tokens"u8) ? 3
                        : r.ValueTextEquals("cache_read_input_tokens"u8) ? 4 : 0;
                    r.Read();
                    if (field != 0 && r.TokenType == JsonTokenType.Number && r.TryGetInt64(out long v))
                    {
                        if (field == 1) p.Input = v;
                        else if (field == 2) p.Output = v;
                        else if (field == 3) p.CacheWrite = v;
                        else p.CacheRead = v;
                    }
                    else r.Skip();
                }
            }
            else if (prop == 2 && r.TokenType == JsonTokenType.String)
            {
                // "<synthetic>" replies are written by Claude Code itself and cost nothing.
                if (r.ValueSpan.Length == 0 || r.ValueSpan[0] == (byte)'<') p.Synthetic = true;
                else p.Model = _models.Id(Text(ref r));
            }
            else if (prop == 3 && r.TokenType == JsonTokenType.String) p.Id = Hash(r.ValueSpan);
            else r.Skip(); // the content blocks: skipped without building anything
        }
    }

    /// <summary>The string value, unescaped into a reused buffer.</summary>
    ReadOnlySpan<char> Text(ref Utf8JsonReader r)
    {
        int max = r.HasValueSequence ? (int)r.ValueSequence.Length : r.ValueSpan.Length;
        if (_chars.Length < max) _chars = new char[max];
        int n = r.CopyString(_chars);
        return _chars.AsSpan(0, n);
    }

    static ReadOnlySpan<char> ProjectName(ReadOnlySpan<char> cwd)
    {
        var trimmed = cwd.TrimEnd(['\\', '/']);
        int slash = trimmed.LastIndexOfAny('\\', '/');
        var name = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        return name.Length > 0 ? name : cwd;
    }

    static int Clamp(long v) => (int)Math.Clamp(v, 0, int.MaxValue);

    /// <summary>FNV-1a, 64-bit: a stable key for message and request ids without keeping the strings.</summary>
    static ulong Hash(ReadOnlySpan<byte> s)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in s) { h ^= b; h *= 1099511628211UL; }
        return h;
    }

    /// <summary>Drops replies older than the 8 days kept, and names nothing refers to any more.</summary>
    void Prune()
    {
        bool any = false;
        foreach (var r in _replies) if (r.Ticks < _cutoffTicks) { any = true; break; }
        if (!any) return;

        var models = new Names(); var sessions = new Names(); var projects = new Names();
        var kept = new List<Reply>(_replies.Count);
        var byKey = new Dictionary<ulong, int>(_replies.Count);
        foreach (var (key, i) in _byKey)
        {
            var r = _replies[i];
            if (r.Ticks < _cutoffTicks) continue;
            r.Model = (short)models.Id(_models[r.Model]);
            r.Session = sessions.Id(_sessions[r.Session]);
            r.Project = (short)projects.Id(_projects[r.Project]);
            byKey[key] = kept.Count;
            kept.Add(r);
        }
        _replies = kept; _byKey = byKey;
        _models = models; _sessions = sessions; _projects = projects;
        _dirtyData = true;
    }

    // ───────────────────────── Summary ─────────────────────────

    Snapshot Build(bool installed, DateTime nowUtc)
    {
        var all = _replies.ToArray();
        Array.Sort(all, static (a, b) => a.Ticks.CompareTo(b.Ticks));
        var validUntil = DateTime.MaxValue;

        // Claude's 5-hour windows begin with the first message after the last one ended. Rebuild them
        // the way usage tools do: a window starts on the hour of its first reply and lasts five hours.
        Window? current = null;
        long start = 0, end = 0;
        int count = 0; long tokens = 0, cache = 0;
        foreach (var r in all)
        {
            if (count == 0 || r.Ticks >= end)
            {
                start = r.Ticks - r.Ticks % TimeSpan.TicksPerHour;
                end = start + 5 * TimeSpan.TicksPerHour;
                count = 0; tokens = 0; cache = 0;
            }
            count++; tokens += r.Tokens; cache += r.CacheRead;
        }
        if (count > 0 && nowUtc.Ticks < end)
        {
            current = new Window(new DateTime(start, DateTimeKind.Utc).ToLocalTime(), new DateTime(end, DateTimeKind.Utc).ToLocalTime(), count, tokens, cache);
            validUntil = new DateTime(end, DateTimeKind.Utc);
        }

        // The last 7 local days.
        var today = DateTime.Today;
        var days = new long[7];
        var modelTokens = new long[_models.Count];
        var sessionsSeen = new HashSet<int>();
        long weekTokens = 0;
        int weekReplies = 0;
        foreach (var r in all)
        {
            int ago = (today - new DateTime(r.Ticks, DateTimeKind.Utc).ToLocalTime().Date).Days;
            if (ago < 0 || ago > 6) continue;
            long t = r.Tokens;
            days[6 - ago] += t;
            weekTokens += t;
            weekReplies++;
            modelTokens[r.Model] += t;
            sessionsSeen.Add(r.Session);
        }
        var byName = new Dictionary<string, long>();
        for (int m = 0; m < modelTokens.Length; m++)
            if (modelTokens[m] > 0) byName[ModelName(_models[m])] = byName.GetValueOrDefault(ModelName(_models[m])) + modelTokens[m];
        var models = byName.Select(kv => (kv.Key, Share: weekTokens > 0 ? kv.Value / (double)weekTokens : 0)).OrderByDescending(x => x.Share).ToList();
        var midnight = today.AddDays(1).ToUniversalTime();
        if (midnight < validUntil) validUntil = midnight;

        // Sessions with a reply in the last 10 minutes; the list changes when the oldest one goes quiet.
        var active = new List<(string Project, DateTime Last)>();
        var lastBySession = new Dictionary<int, Reply>();
        long recent = nowUtc.Ticks - ActiveFor.Ticks;
        for (int i = all.Length - 1; i >= 0 && all[i].Ticks > recent; i--)
            lastBySession.TryAdd(all[i].Session, all[i]);
        foreach (var r in lastBySession.Values)
        {
            active.Add((_projects[r.Project], new DateTime(r.Ticks, DateTimeKind.Utc).ToLocalTime()));
            var quiet = new DateTime(r.Ticks + ActiveFor.Ticks, DateTimeKind.Utc);
            if (quiet < validUntil) validUntil = quiet;
        }
        active.Sort((a, b) => b.Last.CompareTo(a.Last));

        if (_plan is { } plan)
        {
            var stale = plan.At.ToUniversalTime() + PlanFresh;
            if (stale < validUntil) validUntil = stale;
        }
        _validUntil = validUntil;
        return new Snapshot(installed, current, days, weekTokens, weekReplies, sessionsSeen.Count, models, active, _plan);
    }

    // ───────────────────────── Plan limits ─────────────────────────

    static readonly TimeSpan PlanFresh = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Plan limits aren't in the history. A status line script can save them (Claude Code passes them to it),
    /// as "five_hour=NN seven_day=NN" in .claude\state\rate-limits.txt; use them while they're recent.
    /// The file is only read again when it changes. Returns true when the result changed.
    /// </summary>
    bool CheckPlan(string root)
    {
        var old = _plan;
        try
        {
            var path = Path.Combine(root, "state", "rate-limits.txt");
            var stamp = File.GetLastWriteTimeUtc(path); // 1601 when the file doesn't exist
            if (stamp != _planStamp)
            {
                _planStamp = stamp;
                _plan = null;
                if (File.Exists(path))
                {
                    double? five = null, week = null;
                    foreach (var part in File.ReadAllText(path).Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var kv = part.Split('=', 2);
                        if (kv.Length != 2 || !double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                        if (kv[0] == "five_hour") five = v;
                        else if (kv[0] == "seven_day") week = v;
                    }
                    if (five != null || week != null) _plan = (five, week, stamp.ToLocalTime());
                }
            }
            if (_plan is { } p && DateTime.Now - p.At > PlanFresh) _plan = null;
        }
        catch { _plan = null; }
        return !Equals(old, _plan);
    }

    // ───────────────────────── Names ─────────────────────────

    /// <summary>Each distinct string once; replies refer to it by number. Looks up spans without allocating.</summary>
    sealed class Names
    {
        readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
        readonly List<string> _list = [];
        public int Count => _list.Count;
        public string this[int i] => _list[i];

        public int Id(ReadOnlySpan<char> s)
        {
            if (_ids.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(s, out int id)) return id;
            var str = s.ToString();
            _ids[str] = id = _list.Count;
            _list.Add(str);
            return id;
        }

        public int Id(string s)
        {
            if (_ids.TryGetValue(s, out int id)) return id;
            _ids[s] = id = _list.Count;
            _list.Add(s);
            return id;
        }
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

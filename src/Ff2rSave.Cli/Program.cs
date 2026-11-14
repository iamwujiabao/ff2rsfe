using System.Reflection;
using Ff2rSave.Core;

namespace Ff2rSave.Cli;

public static class Program
{
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    const string Usage = """
        ff2rsave - Fatal Frame II: Crimson Butterfly Remake (PC) save tool

        usage: ff2rsave <command> [args] [--config local_config.json] [--fields fields.json]
          info   <file|slotdir>                      header, integrity, key fields
          verify <file|dir> [--roundtrip]            check integrity; --roundtrip also re-encrypts each file and compares bytes
          dump   <file> (--raw out.dec | --json out.json)   decrypted container / JSON section
          get    <file> <fieldId> | --path /Json/Path       read a value (collection fields list all items)
          set    <file|slotdir> <fieldId> <value> (--out <dest> | --in-place) [--path /concrete/path] [--both] [--allow-unverified]
          pack   <in.dec> <out.BIN>                  encrypt a dumped container, recomputing checksums
          fields                                     list field ids from fields.json
        set never overwrites the original by default:
          --out <dest>   write an edited COPY (slotdir input: <dest> becomes the slot folder; file input: <dest> is a file or folder). Recommended.
          --in-place     overwrite the original SAVEDATA.BIN (BACKUP.BIN is left alone unless --both). A timestamped .bak-* copy is made first.
        Fields that are not 'confirmed' are locked: add --allow-unverified to edit them (never tested in the game).
        Every write goes to a temp file, is re-read and verified, then atomically replaces the target. BACK UP YOUR SAVES YOURSELF TOO.
        """;

    public static int Run(string[] argv, TextWriter o, TextWriter e)
    {
        try
        {
            var (pos, opt) = Parse(argv);
            if (pos.Count == 0 || pos[0] is "help" or "-h" or "--help") { o.WriteLine(Usage); return pos.Count == 0 ? 2 : 0; }
            var cmd = pos[0]; var rest = pos.Skip(1).ToList();
            var schema = LoadSchema(opt);
            if (cmd == "fields") { foreach (var f in schema.Fields) o.WriteLine($"{f.Id,-32} {f.Source,-6} {(f.Editable ? "rw" : "ro")} {f.Confidence,-9} {f.Name}"); return 0; }
            var cfg = CryptoConfig.Load(CryptoConfig.Find([Directory.GetCurrentDirectory(), AppContext.BaseDirectory], opt.GetValueOrDefault("config")));
            return cmd switch
            {
                "info" => Info(rest, cfg, schema, o), "verify" => Verify(rest, opt, cfg, o), "dump" => Dump(rest, opt, cfg, o),
                "get" => Get(rest, opt, cfg, schema, o), "set" => Set(rest, opt, cfg, schema, o), "pack" => Pack(rest, cfg, o),
                _ => Fail($"unknown command '{cmd}'", e)
            };
        }
        catch (SaveFormatException ex) { e.WriteLine("error: " + ex.Message); return 2; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException or OverflowException)
        { e.WriteLine("error: " + ex.Message); return 2; }
    }

    static int Fail(string m, TextWriter e) { e.WriteLine("error: " + m); return 2; }

    static readonly HashSet<string> Flags = ["roundtrip", "in-place", "both", "allow-unverified"];

    static (List<string> pos, Dictionary<string, string> opt) Parse(string[] a)
    {
        var pos = new List<string>(); var opt = new Dictionary<string, string>();
        for (int i = 0; i < a.Length; i++)
            if (a[i].StartsWith("--") && Flags.Contains(a[i][2..])) opt[a[i][2..]] = "true";
            else if (a[i].StartsWith("--")) { if (i + 1 >= a.Length) throw new ArgumentException($"option {a[i]} needs a value"); opt[a[i][2..]] = a[++i]; }
            else pos.Add(a[i]);
        return (pos, opt);
    }

    static string? FindUp(string name)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent) { var p = Path.Combine(d.FullName, name); if (File.Exists(p)) return p; }
        return null;
    }

    static FieldSchema LoadSchema(Dictionary<string, string> opt)
    {
        var path = opt.GetValueOrDefault("fields") ?? FindUp("fields.json") ?? FindUp(Path.Combine("docs", "fields.json"));
        if (path != null) return FieldSchema.Load(path);
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("fields.json")!; // embedded default
        var tmp = Path.GetTempFileName(); try { using (var f = File.Create(tmp)) s.CopyTo(f); return FieldSchema.Load(tmp); } finally { File.Delete(tmp); }
    }

    static string Need(List<string> r, int i, string what) => r.Count > i ? r[i] : throw new ArgumentException($"missing argument: {what}");

    static int Info(List<string> r, CryptoConfig cfg, FieldSchema s, TextWriter o)
    {
        var target = Need(r, 0, "file or slot dir"); bool dir = Directory.Exists(target);
        var path = dir ? Path.Combine(target, "SAVEDATA.BIN") : target; var f = SaveFile.Open(path, cfg); var h = f.Header;
        o.WriteLine($"file        {path}\ntype        {(h.IsUser ? "user save (WLNUSR)" : "system save (WLNSYS)")}\nversion     0x{h.Version:X}\nuser id     {h.UserId} (0x{h.UserId:X})\nchapter     {h.Chapter}\nprogress    {h.Checkpoint}\nplay time   {h.PlayTime} (unit unverified; ~{TimeSpan.FromSeconds(h.PlayTime / 1e6):hh\\:mm\\:ss} if microseconds)\nsaved       {h.Timestamp.LocalDateTime:u}");
        o.WriteLine("integrity   " + (f.LoadProblems.Count == 0 ? "OK" : "PROBLEMS: " + string.Join("; ", f.LoadProblems)));
        if (dir) o.WriteLine("BACKUP.BIN  " + (!File.Exists(Path.Combine(target, "BACKUP.BIN")) ? "missing" : new SlotFolder(target, cfg).BackupIsIdentical() ? "identical" : "differs"));
        if (h.IsUser) foreach (var d in s.Fields.Where(d => d.Source == "json" && !d.Collection && d.Confidence != "guessed" && d.Type is "int" or "bool"))
            o.WriteLine($"{d.Id,-32} {f.GetText(d)}   [{d.Confidence}]");
        return f.LoadProblems.Count == 0 ? 0 : 1;
    }

    static int Verify(List<string> r, Dictionary<string, string> opt, CryptoConfig cfg, TextWriter o)
    {
        var target = Need(r, 0, "file or dir"); var files = Directory.Exists(target) ? Directory.GetFiles(target, "*.BIN", SearchOption.AllDirectories).Order().ToList() : [target];
        bool rt = opt.ContainsKey("roundtrip"); int bad = 0;
        foreach (var p in files)
        {
            try
            {
                var raw = File.ReadAllBytes(p); var f = SaveFile.FromBytes(raw, cfg); var problems = f.LoadProblems.ToList();
                if (rt)
                {
                    var again = f.ToBytes(); var d = DiffRanges(raw, again);
                    if (d.Count == 0) o.WriteLine($"ROUNDTRIP OK   {p}  (decrypt -> re-encrypt is byte-identical, {raw.Length:N0} bytes)");
                    else { problems.Add($"round trip differs in {d.Count} range(s): " + string.Join(", ", d.Take(10).Select(x => $"0x{x.start:X}+{x.len}{(x.start < SaveHeader.Size ? " (header)" : " (payload)")}")) + (d.Count > 10 ? ", …" : "")); }
                }
                bool ok = problems.Count == 0; o.WriteLine($"{(ok ? "OK  " : "FAIL")} {p}{(ok ? "" : "  " + string.Join("; ", problems))}"); bad += ok ? 0 : 1;
            }
            catch (SaveFormatException ex) { o.WriteLine($"FAIL {p}  {ex.Message}"); bad++; }
        }
        o.WriteLine($"{files.Count - bad}/{files.Count} OK (user-ID binding is not checked here)");
        return bad == 0 ? 0 : 1;
    }

    /// <summary>Contiguous differing byte ranges between two buffers (a length mismatch counts as a range at the shorter length).</summary>
    public static List<(int start, int len)> DiffRanges(byte[] a, byte[] b)
    {
        var r = new List<(int, int)>(); int n = Math.Min(a.Length, b.Length), i = 0;
        while (i < n) { if (a[i] == b[i]) { i++; continue; } int s = i; while (i < n && a[i] != b[i]) i++; r.Add((s, i - s)); }
        if (a.Length != b.Length) r.Add((n, Math.Abs(a.Length - b.Length)));
        return r;
    }

    static int Dump(List<string> r, Dictionary<string, string> opt, CryptoConfig cfg, TextWriter o)
    {
        var path = Need(r, 0, "file"); var raw = File.ReadAllBytes(path); var (h, pt) = new SaveCodec(cfg).Decrypt(raw);
        if (opt.TryGetValue("raw", out var rawOut)) { File.WriteAllBytes(rawOut, [.. h.Raw, .. pt]); o.WriteLine($"wrote {rawOut} (256-byte header + decrypted payload, {raw.Length:N0} bytes)"); return 0; }
        if (opt.TryGetValue("json", out var jsonOut))
        {
            var f = SaveFile.FromBytes(raw, cfg); File.WriteAllBytes(jsonOut, f.OriginalPlaintext.AsSpan(SaveFile.JsonStart, f.OriginalJsonLength).ToArray()); o.WriteLine($"wrote {jsonOut} ({f.OriginalJsonLength:N0} bytes of JSON)"); return 0;
        }
        throw new ArgumentException("dump needs --raw <out> or --json <out>");
    }

    static int Get(List<string> r, Dictionary<string, string> opt, CryptoConfig cfg, FieldSchema s, TextWriter o)
    {
        var f = SaveFile.Open(Need(r, 0, "file"), cfg);
        if (opt.TryGetValue("path", out var p)) { o.WriteLine(f.GetRaw(p) ?? throw new KeyNotFoundException($"path not found: {p}")); return 0; }
        var d = s[Need(r, 1, "fieldId")];
        if (d.Collection) foreach (var path in f.Expand(d.Path)) o.WriteLine($"{path}\t{f.GetText(d, path)}"); else o.WriteLine(f.GetText(d));
        return 0;
    }

    static int Set(List<string> r, Dictionary<string, string> opt, CryptoConfig cfg, FieldSchema s, TextWriter o)
    {
        var target = Need(r, 0, "file or slot dir"); var d = s[Need(r, 1, "fieldId")]; var value = Need(r, 2, "value");
        bool dir = Directory.Exists(target); bool inPlace = opt.ContainsKey("in-place"); opt.TryGetValue("out", out var outPath);
        if (outPath == null && !inPlace) throw new ArgumentException("refusing to modify the original. Add --out <dest> to write an edited copy (recommended) or --in-place to overwrite it (a .bak-* backup is made first).");
        if (outPath != null && inPlace) throw new ArgumentException("use either --out or --in-place, not both.");
        var path = dir ? Path.Combine(target, "SAVEDATA.BIN") : target;
        var f = SaveFile.Open(path, cfg); f.AllowUnverified = opt.ContainsKey("allow-unverified");
        if (d.Collection && !opt.ContainsKey("path")) throw new ArgumentException($"{d.Id} is a collection; pass --path (see: get <file> {d.Id})");
        f.SetText(d, value, opt.GetValueOrDefault("path"));

        if (dir)
        {
            var slot = new SlotFolder(target, cfg);
            if (outPath != null) { var w = slot.Export(f, outPath); o.WriteLine($"wrote edited copy {w} (original untouched), verified OK"); }
            else { var made = slot.Save(f, opt.ContainsKey("both") ? BackupFilePolicy.Both : BackupFilePolicy.PrimaryOnly); o.WriteLine($"overwrote original SAVEDATA.BIN{(opt.ContainsKey("both") ? " and BACKUP.BIN" : " (BACKUP.BIN left unchanged)")}, verified OK; backups: {string.Join(", ", made.Select(Path.GetFileName))}"); }
            return 0;
        }
        var dest = outPath != null ? (Directory.Exists(outPath) ? Path.Combine(outPath, Path.GetFileName(path)) : outPath) : path;
        if (outPath != null && Path.GetFullPath(dest) == Path.GetFullPath(path)) throw new ArgumentException("--out points at the original file; use --in-place if you really mean to overwrite it.");
        var b = AtomicFile.WriteVerified(dest, f.ToBytes(), raw => SaveFile.FromBytes(raw, cfg).LoadProblems);
        o.WriteLine($"wrote {dest}, verified OK" + (outPath != null ? " (original untouched)" : "") + (b != null ? $"; backup {Path.GetFileName(b)}" : "")); return 0;
    }

    static int Pack(List<string> r, CryptoConfig cfg, TextWriter o)
    {
        var inp = File.ReadAllBytes(Need(r, 0, "in.dec")); var outPath = Need(r, 1, "out.BIN");
        if (inp.Length <= SaveHeader.Size || (inp.Length - SaveHeader.Size) % 16 != 0) throw new SaveFormatException("input is not a dumped container (header + payload, payload multiple of 16 bytes).");
        var h = new SaveHeader(inp.AsSpan(0, SaveHeader.Size)); var pt = inp.AsSpan(SaveHeader.Size).ToArray();
        new IntegrityService(cfg).Fix(h, pt); var data = new SaveCodec(cfg).Encrypt(h, pt);
        var b = AtomicFile.WriteVerified(outPath, data, raw => SaveFile.FromBytes(raw, cfg).LoadProblems);
        o.WriteLine($"wrote {outPath} ({data.Length:N0} bytes), verified OK" + (b != null ? $"; backup {Path.GetFileName(b)}" : "")); return 0;
    }
}

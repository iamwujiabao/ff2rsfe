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
          verify <file|dir>                          check integrity of every .BIN (exit 1 on problems)
          dump   <file> (--raw out.dec | --json out.json)   decrypted container / JSON section
          get    <file> <fieldId> | --path /Json/Path       read a value (collection fields list all items)
          set    <file|slotdir> <fieldId> <value> [--path /concrete/path] [--out new.BIN]
          pack   <in.dec> <out.BIN>                  encrypt a dumped container, recomputing checksums
          fields                                     list field ids from fields.json
        A slot dir writes SAVEDATA.BIN and BACKUP.BIN. Every write makes a timestamped .bak-* copy first
        and is re-read and verified afterwards. BACK UP YOUR SAVES YOURSELF TOO.
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
            var cfg = CryptoConfig.Load(opt.GetValueOrDefault("config") ?? FindUp("local_config.json") ?? "local_config.json");
            return cmd switch
            {
                "info" => Info(rest, cfg, schema, o), "verify" => Verify(rest, cfg, o), "dump" => Dump(rest, opt, cfg, o),
                "get" => Get(rest, opt, cfg, schema, o), "set" => Set(rest, opt, cfg, schema, o), "pack" => Pack(rest, cfg, o),
                _ => Fail($"unknown command '{cmd}'", e)
            };
        }
        catch (SaveFormatException ex) { e.WriteLine("error: " + ex.Message); return 2; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException or OverflowException)
        { e.WriteLine("error: " + ex.Message); return 2; }
    }

    static int Fail(string m, TextWriter e) { e.WriteLine("error: " + m); return 2; }

    static (List<string> pos, Dictionary<string, string> opt) Parse(string[] a)
    {
        var pos = new List<string>(); var opt = new Dictionary<string, string>();
        for (int i = 0; i < a.Length; i++)
            if (a[i].StartsWith("--")) { if (i + 1 >= a.Length) throw new ArgumentException($"option {a[i]} needs a value"); opt[a[i][2..]] = a[++i]; }
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

    static int Verify(List<string> r, CryptoConfig cfg, TextWriter o)
    {
        var target = Need(r, 0, "file or dir"); var files = Directory.Exists(target) ? Directory.GetFiles(target, "*.BIN", SearchOption.AllDirectories).Order().ToList() : [target];
        int bad = 0;
        foreach (var p in files)
        {
            try { var f = SaveFile.Open(p, cfg); bool ok = f.LoadProblems.Count == 0; o.WriteLine($"{(ok ? "OK  " : "FAIL")} {p}{(ok ? "" : "  " + string.Join("; ", f.LoadProblems))}"); bad += ok ? 0 : 1; }
            catch (SaveFormatException ex) { o.WriteLine($"FAIL {p}  {ex.Message}"); bad++; }
        }
        o.WriteLine($"{files.Count - bad}/{files.Count} OK (user-ID binding is not checked here)");
        return bad == 0 ? 0 : 1;
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
        bool dir = Directory.Exists(target); var path = dir ? Path.Combine(target, "SAVEDATA.BIN") : target;
        var f = SaveFile.Open(path, cfg);
        if (d.Collection && !opt.ContainsKey("path")) throw new ArgumentException($"{d.Id} is a collection; pass --path (see: get <file> {d.Id})");
        f.SetText(d, value, opt.GetValueOrDefault("path"));
        if (opt.TryGetValue("out", out var outPath)) { WriteVerified(f, outPath, cfg, o); return 0; }
        if (dir) { var made = new SlotFolder(target, cfg).Save(f); o.WriteLine($"wrote SAVEDATA.BIN and BACKUP.BIN, verified OK; backups: {string.Join(", ", made.Select(Path.GetFileName))}"); }
        else WriteVerified(f, path, cfg, o);
        return 0;
    }

    static void WriteVerified(SaveFile f, string path, CryptoConfig cfg, TextWriter o)
    {
        var data = f.ToBytes(); var b = BackupService.Backup(path); File.WriteAllBytes(path, data);
        var back = SaveFile.Open(path, cfg); if (back.LoadProblems.Count > 0) throw new SaveFormatException($"verification failed after write: {string.Join("; ", back.LoadProblems)}");
        o.WriteLine($"wrote {path}, verified OK" + (b != null ? $"; backup {Path.GetFileName(b)}" : ""));
    }

    static int Pack(List<string> r, CryptoConfig cfg, TextWriter o)
    {
        var inp = File.ReadAllBytes(Need(r, 0, "in.dec")); var outPath = Need(r, 1, "out.BIN");
        if (inp.Length <= SaveHeader.Size || (inp.Length - SaveHeader.Size) % 16 != 0) throw new SaveFormatException("input is not a dumped container (header + payload, payload multiple of 16 bytes).");
        var h = new SaveHeader(inp.AsSpan(0, SaveHeader.Size)); var pt = inp.AsSpan(SaveHeader.Size).ToArray();
        new IntegrityService(cfg).Fix(h, pt); var data = new SaveCodec(cfg).Encrypt(h, pt);
        var b = BackupService.Backup(outPath); File.WriteAllBytes(outPath, data);
        var back = SaveFile.Open(outPath, cfg); if (back.LoadProblems.Count > 0) throw new SaveFormatException("verification failed: " + string.Join("; ", back.LoadProblems));
        o.WriteLine($"wrote {outPath} ({data.Length:N0} bytes), verified OK" + (b != null ? $"; backup {Path.GetFileName(b)}" : "")); return 0;
    }
}

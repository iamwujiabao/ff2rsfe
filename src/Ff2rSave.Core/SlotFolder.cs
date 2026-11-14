namespace Ff2rSave.Core;

/// <summary>Which files an in-place save touches. The game's real write order is NOT verified, so the default leaves BACKUP.BIN alone.</summary>
public enum BackupFilePolicy
{
    /// <summary>Write only SAVEDATA.BIN; BACKUP.BIN keeps the pre-edit generation (a natural way back, and a state the game itself produces).</summary>
    PrimaryOnly,
    /// <summary>Write identical content to SAVEDATA.BIN and BACKUP.BIN (overwrites the older generation).</summary>
    Both
}

/// <summary>One slot directory: SAVEDATA.BIN plus BACKUP.BIN.</summary>
public sealed class SlotFolder(string directory, CryptoConfig cfg)
{
    public string Directory { get; } = directory;
    public string SavePath => Path.Combine(Directory, "SAVEDATA.BIN");
    public string BackupPath => Path.Combine(Directory, "BACKUP.BIN");
    public bool HasBackup => File.Exists(BackupPath);

    public SaveFile OpenSave() => SaveFile.Open(SavePath, cfg);
    public SaveFile OpenBackup() => SaveFile.Open(BackupPath, cfg);

    /// <summary>True when BACKUP.BIN exists and is byte-identical to SAVEDATA.BIN.</summary>
    public bool BackupIsIdentical() => HasBackup && File.ReadAllBytes(SavePath).AsSpan().SequenceEqual(File.ReadAllBytes(BackupPath));

    static void RefuseSystem(SaveFile f) { if (f.Header.IsSystem) throw new SaveFormatException("System saves (SYSTEMSAVEDATA00) are read-only in this version: nothing is written."); }

    IReadOnlyList<string> Check(byte[] raw) => SaveFile.FromBytes(raw, cfg).LoadProblems;

    /// <summary>OVERWRITES the original (atomically, with a timestamped .bak-* backup of each replaced file and read-back verification). Returns backup paths.</summary>
    public IReadOnlyList<string> Save(SaveFile file, BackupFilePolicy policy = BackupFilePolicy.PrimaryOnly)
    {
        RefuseSystem(file);
        var data = file.ToBytes(); var made = new List<string>();
        var targets = policy == BackupFilePolicy.Both ? new[] { SavePath, BackupPath } : new[] { SavePath };
        foreach (var p in targets) if (AtomicFile.WriteVerified(p, data, Check) is { } b) made.Add(b);
        return made;
    }

    /// <summary>Writes the edited save into destSlotDir WITHOUT touching this slot. BACKUP.BIN is copied unchanged so the copy is a complete slot. Returns the written SAVEDATA.BIN path.</summary>
    public string Export(SaveFile file, string destSlotDir)
    {
        RefuseSystem(file);
        var src = Path.GetFullPath(Directory).TrimEnd(Path.DirectorySeparatorChar); var dst = Path.GetFullPath(destSlotDir).TrimEnd(Path.DirectorySeparatorChar);
        var cmp = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(src, dst, cmp) || dst.StartsWith(src + Path.DirectorySeparatorChar, cmp)) throw new SaveFormatException("The export folder must be different from the original slot folder (and not inside it).");
        System.IO.Directory.CreateDirectory(dst);
        var outPath = Path.Combine(dst, "SAVEDATA.BIN"); AtomicFile.WriteVerified(outPath, file.ToBytes(), Check);
        var destBackup = Path.Combine(dst, "BACKUP.BIN");
        if (HasBackup && !File.Exists(destBackup)) AtomicFile.WriteVerified(destBackup, File.ReadAllBytes(BackupPath), Check);
        return outPath;
    }

    /// <summary>Slot directories (SAVEDATA00..) under a user folder.</summary>
    public static IEnumerable<SlotFolder> Enumerate(string userDir, CryptoConfig cfg) =>
        System.IO.Directory.Exists(userDir) ? System.IO.Directory.GetDirectories(userDir, "SAVEDATA*").Order().Select(d => new SlotFolder(d, cfg)) : [];
}

namespace Ff2rSave.Core;

/// <summary>One slot directory: SAVEDATA.BIN plus BACKUP.BIN. Which file the game writes first is unverified,
/// so Save writes identical content to both (safe default; see docs/save_format.md).</summary>
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

    /// <summary>Back up both files, write, then re-read, decrypt and verify. Returns the backup paths made.</summary>
    public IReadOnlyList<string> Save(SaveFile file)
    {
        var data = file.ToBytes(); var made = new List<string>();
        foreach (var p in new[] { SavePath, BackupPath })
        {
            if (BackupService.Backup(p) is { } b) made.Add(b);
            File.WriteAllBytes(p, data);
            var back = SaveFile.Open(p, cfg);
            if (back.LoadProblems.Count > 0) throw new SaveFormatException($"Verification failed for {p}: {string.Join("; ", back.LoadProblems)}");
        }
        return made;
    }

    /// <summary>Slot directories (SAVEDATA00..) under a user folder.</summary>
    public static IEnumerable<SlotFolder> Enumerate(string userDir, CryptoConfig cfg) =>
        System.IO.Directory.Exists(userDir) ? System.IO.Directory.GetDirectories(userDir, "SAVEDATA*").Order().Select(d => new SlotFolder(d, cfg)) : [];
}

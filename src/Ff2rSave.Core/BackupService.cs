namespace Ff2rSave.Core;

/// <summary>Timestamped copy before every write.</summary>
public static class BackupService
{
    public static string? Backup(string path)
    {
        if (!File.Exists(path)) return null;
        var dest = $"{path}.bak-{DateTime.Now:yyyyMMdd-HHmmss-fff}";
        File.Copy(path, dest, overwrite: false); return dest;
    }
}

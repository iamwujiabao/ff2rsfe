namespace Ff2rSave.Core;

/// <summary>Write-to-temp, verify, back up the existing file, then atomically replace. A failure at any step leaves the original untouched.</summary>
public static class AtomicFile
{
    /// <param name="verify">Decrypts/validates the bytes read back from disk; returns problems (empty == OK). May throw SaveFormatException.</param>
    /// <returns>Path of the timestamped backup of the previous file, or null if there was none.</returns>
    public static string? WriteVerified(string path, byte[] data, Func<byte[], IReadOnlyList<string>> verify)
    {
        var tmp = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { fs.Write(data); fs.Flush(true); }
            var back = File.ReadAllBytes(tmp);
            if (!back.AsSpan().SequenceEqual(data)) throw new SaveFormatException($"Verification failed: the temporary file for {Path.GetFileName(path)} does not match what was written (disk problem?). The original was not touched.");
            var problems = verify(back);
            if (problems.Count > 0) throw new SaveFormatException($"Verification failed for {Path.GetFileName(path)}: {string.Join("; ", problems)}. The original was not touched.");

            var backup = BackupService.Backup(path);
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);

            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(data)) throw new SaveFormatException($"{path} does not match after replacing" + (backup != null ? $"; restore from {backup}" : "") + ".");
            return backup;
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}

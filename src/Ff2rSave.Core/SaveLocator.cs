namespace Ff2rSave.Core;

/// <summary>Finds the default save location: %LOCALAPPDATA%\KoeiTecmo\FatalFrameII\Savedata\&lt;digits&gt;\ (user-id folders).</summary>
public static class SaveLocator
{
    public static string DefaultRoot(string? localAppData = null)
    {
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "KoeiTecmo", "FatalFrameII", "Savedata");
    }

    /// <summary>Digit-named user folders that exist under the Savedata root.</summary>
    public static IReadOnlyList<string> FindUserFolders(string? localAppData = null)
    {
        var root = DefaultRoot(localAppData);
        return Directory.Exists(root) ? Directory.GetDirectories(root).Where(d => Path.GetFileName(d).All(char.IsAsciiDigit) && Path.GetFileName(d).Length > 0).Order().ToList() : [];
    }

    /// <summary>Slot dirs for a chosen folder: a user folder (contains SAVEDATAnn and SYSTEMSAVEDATAnn) or a single slot folder (contains SAVEDATA.BIN).</summary>
    public static IReadOnlyList<string> ResolveSlots(string folder)
    {
        if (File.Exists(Path.Combine(folder, "SAVEDATA.BIN"))) return [folder];
        if (!Directory.Exists(folder)) return [];
        bool Has(string d) => File.Exists(Path.Combine(d, "SAVEDATA.BIN"));
        // user slots first, then the (read-only) system save folder(s)
        return Directory.GetDirectories(folder, "SAVEDATA*").Where(Has).Order().Concat(Directory.GetDirectories(folder, "SYSTEMSAVEDATA*").Where(Has).Order()).ToList();
    }
}

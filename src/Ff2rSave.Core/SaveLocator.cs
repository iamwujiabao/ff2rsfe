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

    /// <summary>Slot dirs for a chosen folder: a user folder (contains SAVEDATAnn) or a single slot folder (contains SAVEDATA.BIN).</summary>
    public static IReadOnlyList<string> ResolveSlots(string folder)
    {
        if (File.Exists(Path.Combine(folder, "SAVEDATA.BIN"))) return [folder];
        return Directory.Exists(folder) ? Directory.GetDirectories(folder, "SAVEDATA*").Where(d => File.Exists(Path.Combine(d, "SAVEDATA.BIN"))).Order().ToList() : [];
    }
}

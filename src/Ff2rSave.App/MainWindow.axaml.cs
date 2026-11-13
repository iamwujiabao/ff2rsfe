using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Ff2rSave.App;

public partial class MainWindow : Window
{
    readonly MainViewModel vm = new();

    public MainWindow() : this(null) { }

    public MainWindow(string? startPath)
    {
        InitializeComponent();
        DataContext = vm;
        vm.PickFolder = async () =>
        {
            var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a Savedata\\<id> folder or a single SAVEDATAnn slot folder", AllowMultiple = false });
            return r.Count > 0 ? r[0].TryGetLocalPath() : null;
        };
        vm.Confirm = Confirm;
        vm.ScrollToRow = r => { if (vm.HexRows is { } h && r >= 0 && r < h.Count) this.FindControl<ListBox>("HexList")!.ScrollIntoView(r); };
        this.FindControl<TabControl>("Tabs")!.SelectionChanged += (_, e) => { if (e.Source is TabControl) vm.RefreshHexCommand.Execute(null); };
        if (startPath != null) Opened += (_, _) => vm.OpenPath(startPath);
    }

    /// <summary>Avalonia has no MessageBox; a tiny modal dialog.</summary>
    async Task<bool> Confirm(string message)
    {
        var ok = new Button { Content = "Write files", IsDefault = true }; var cancel = new Button { Content = "Cancel", IsCancel = true };
        var dlg = new Window { Title = "Confirm save", Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false,
            Content = new StackPanel { Margin = new Thickness(16), Spacing = 14, Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } } } } };
        ok.Click += (_, _) => dlg.Close(true); cancel.Click += (_, _) => dlg.Close(false);
        return await dlg.ShowDialog<bool>(this);
    }
}

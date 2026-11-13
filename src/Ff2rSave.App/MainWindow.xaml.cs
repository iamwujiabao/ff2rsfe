using System.Windows;

namespace Ff2rSave.App;

public partial class MainWindow : Window
{
    void TabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TabControl && DataContext is MainViewModel vm) { vm.RefreshHexCommand.Execute(null); }
    }

    public MainWindow() { InitializeComponent(); var vm = new MainViewModel(); DataContext = vm; vm.ScrollToRow = r => { if (r >= 0 && r < (vm.HexRows?.Count ?? 0)) HexList.ScrollIntoView(vm.HexRows![r]!); }; }
}

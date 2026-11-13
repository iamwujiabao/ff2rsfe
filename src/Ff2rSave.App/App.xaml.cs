using System.Windows;
using Microsoft.Win32;

namespace Ff2rSave.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeService.Apply(this);
        SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.Invoke(() => ThemeService.Apply(this)); // follow system light/dark live
        base.OnStartup(e);
    }
}

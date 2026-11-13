using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Ff2rSave.App;

/// <summary>Brushes for the system light/dark mode (AppsUseLightTheme registry value).</summary>
public static class ThemeService
{
    public static bool SystemIsLight()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is not 0; }
        catch { return true; }
    }

    public static void Apply(Application app)
    {
        bool light = SystemIsLight();
        void Set(string key, string hex) => app.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        Set("Bg", light ? "#FFFFFF" : "#1E1E1E"); Set("Panel", light ? "#F3F3F3" : "#2A2A2A"); Set("Fg", light ? "#1A1A1A" : "#EDEDED");
        Set("Muted", light ? "#666666" : "#A0A0A0"); Set("Border", light ? "#D0D0D0" : "#444444"); Set("Accent", light ? "#0A64C8" : "#4DA3FF");
        Set("Warn", light ? "#FFF4CE" : "#4A3F10"); Set("Bad", light ? "#FDE7E9" : "#5A1D22"); Set("Changed", light ? "#FFC9C9" : "#8A2A2A"); Set("Good", light ? "#1E7B34" : "#5FD07A");
    }
}

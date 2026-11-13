using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Ff2rSave.App;
using Ff2rSave.Core;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Ff2rSave.App.Tests.TestAppBuilder))]

namespace Ff2rSave.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Ff2rSave.App.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class UiTests
{
    static string Root { get { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "docs", "fields.json"))) return d.FullName; throw new InvalidOperationException(); } }
    static string Shots => Environment.GetEnvironmentVariable("FF2R_SHOTS") ?? Path.Combine(Path.GetTempPath(), "ff2r-shots");

    static string TempUserDir()
    {
        var t = Path.Combine(Path.GetTempPath(), "ff2r-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(t);
        foreach (var slot in new[] { "SAVEDATA00", "SAVEDATA01" })
        { Directory.CreateDirectory(Path.Combine(t, slot)); foreach (var f in Directory.GetFiles(Path.Combine(Root, "samples", "mine", slot), "*.BIN")) File.Copy(f, Path.Combine(t, slot, Path.GetFileName(f))); }
        return t;
    }

    static (MainWindow w, MainViewModel vm, string dir) Open(ThemeVariant? theme = null)
    {
        var dir = TempUserDir(); var w = new MainWindow(); var vm = (MainViewModel)w.DataContext!;
        if (theme != null) w.RequestedThemeVariant = theme;
        w.Show(); vm.OpenPath(dir); Dispatcher.UIThread.RunJobs(); return (w, vm, dir);
    }

    static void Shot(Window w, string name)
    {
        Directory.CreateDirectory(Shots); Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()!.Save(Path.Combine(Shots, name));
    }

    [AvaloniaFact]
    public void Opens_slots_shows_fields_and_integrity()
    {
        var (w, vm, dir) = Open(ThemeVariant.Light);
        try
        {
            Assert.Equal(2, vm.Slots.Count); Assert.All(vm.Slots, s => Assert.True(s.IntegrityOk));
            Assert.True(vm.Fields.Count > 30); Assert.Contains(vm.Fields, f => f.Label.Contains("Points (senki)") && f.Value == "5865");
            Assert.Contains(vm.Fields, f => f.Unverified && f.StatusText.StartsWith("⚠")); Assert.Contains(vm.Fields, f => f.StatusText == "confirmed");
            Shot(w, "fields-light.png");
            w.RequestedThemeVariant = ThemeVariant.Dark; Shot(w, "fields-dark.png");
        }
        finally { w.Close(); Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void Edit_validates_enables_save_and_highlights_hex_changes()
    {
        var (w, vm, dir) = Open(ThemeVariant.Light);
        try
        {
            var senki = vm.Fields.First(f => f.Def.Id == "player.senki");
            Assert.False(vm.SaveCommand.CanExecute(null));
            senki.Value = "-5"; Assert.True(senki.HasError); Assert.False(vm.SaveCommand.CanExecute(null)); Assert.Contains("outside", senki.StatusText);
            senki.Value = "abc"; Assert.True(senki.HasError);
            senki.Value = "7777"; Assert.False(senki.HasError); Assert.True(vm.SaveCommand.CanExecute(null));
            var ro = vm.Fields.First(f => f.Def.Id == "hdr.chapter"); var before = ro.Value; ro.Value = "99"; Assert.Equal(before, ro.Value); // read-only ignored

            w.FindControl<TabControl>("Tabs")!.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            Assert.NotNull(vm.HexRows); Assert.True(vm.HexRows!.ChangedByteCount() > 0); Assert.True(vm.HexRows.FirstChangedRow() >= 0);
            vm.GoToChangeCommand.Execute(null); Shot(w, "hex-light.png");
        }
        finally { w.Close(); Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public async Task Save_flow_backs_up_writes_both_files_and_verifies()
    {
        var (w, vm, dir) = Open();
        try
        {
            string? asked = null; vm.Confirm = m => { asked = m; return Task.FromResult(true); };
            vm.Fields.First(f => f.Def.Id == "player.senki").Value = "4321"; vm.Fields.First(f => f.Def.Id == "player.sen").Value = "4321";
            vm.SaveCommand.Execute(null); for (int i = 0; i < 50 && !vm.Status.StartsWith("Saved"); i++) { await Task.Delay(50); Dispatcher.UIThread.RunJobs(); }
            Assert.NotNull(asked); Assert.Contains("BACKUP.BIN", asked); Assert.StartsWith("Saved SAVEDATA00", vm.Status);
            var slot = Path.Combine(dir, "SAVEDATA00"); Assert.Equal(2, Directory.GetFiles(slot, "*.bak-*").Length);
            var cfg = CryptoConfig.Load(Path.Combine(Root, "local_config.json")); Assert.Empty(SaveFile.Open(Path.Combine(slot, "BACKUP.BIN"), cfg).LoadProblems);
            Assert.Equal("4321", vm.Fields.First(f => f.Def.Id == "player.senki").Value); Assert.False(vm.SaveCommand.CanExecute(null));
            Shot(w, "after-save.png");
        }
        finally { w.Close(); Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void Cancelled_confirmation_writes_nothing()
    {
        var (w, vm, dir) = Open();
        try
        {
            vm.Confirm = _ => Task.FromResult(false); var p = Path.Combine(dir, "SAVEDATA00", "SAVEDATA.BIN"); var before = File.ReadAllBytes(p);
            vm.Fields.First(f => f.Def.Id == "player.senki").Value = "1"; vm.SaveCommand.Execute(null); Dispatcher.UIThread.RunJobs(); Thread.Sleep(200); Dispatcher.UIThread.RunJobs();
            Assert.True(File.ReadAllBytes(p).AsSpan().SequenceEqual(before)); Assert.Empty(Directory.GetFiles(dir, "*.bak-*", SearchOption.AllDirectories));
        }
        finally { w.Close(); Directory.Delete(dir, true); }
    }
}

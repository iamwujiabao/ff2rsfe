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
            Assert.Contains(vm.Fields, f => f.Unverified && f.StatusText.StartsWith("🔒")); Assert.Contains(vm.Fields, f => f.StatusText == "confirmed");
            Shot(w, "fields-light.png");
            w.RequestedThemeVariant = ThemeVariant.Dark; Shot(w, "fields-dark.png");
        }
        finally { w.Close(); Directory.Delete(dir, true); }
    }

    static async Task Pump(Func<bool> done) { for (int i = 0; i < 100 && !done(); i++) { await Task.Delay(30); Dispatcher.UIThread.RunJobs(); } }

    [AvaloniaFact]
    public async Task Unverified_fields_are_locked_until_the_warning_is_accepted()
    {
        var (w, vm, dir) = Open(ThemeVariant.Light);
        try
        {
            var senki = vm.Fields.First(f => f.Def.Id == "player.senki");
            Assert.Equal("🔒 locked (unverified)", senki.StatusText); senki.Value = "7777"; Assert.Equal("5865", senki.Value); Assert.False(vm.SaveCommand.CanExecute(null));
            string? warning = null; vm.Confirm = m => { warning = m; return Task.FromResult(false); };
            vm.AllowUnverified = true; await Pump(() => warning != null); Assert.Contains("WARNING", warning); Assert.False(vm.AllowUnverified); senki.Value = "7777"; Assert.Equal("5865", senki.Value);
            vm.Confirm = _ => Task.FromResult(true); vm.AllowUnverified = true; await Pump(() => vm.AllowUnverified);
            Assert.True(vm.AllowUnverified); Assert.StartsWith("⚠ unverified", senki.StatusText); senki.Value = "7777"; Assert.Equal("7777", senki.Value);
            Shot(w, "unlocked-light.png");
        }
        finally { w.Close(); Directory.Delete(dir, true); }
    }

    static async Task Unlock(MainViewModel vm) { vm.Confirm = _ => Task.FromResult(true); vm.AllowUnverified = true; await Pump(() => vm.AllowUnverified); }

    [AvaloniaFact]
    public async Task Edit_validates_enables_save_and_highlights_hex_changes()
    {
        var (w, vm, dir) = Open(ThemeVariant.Light);
        try
        {
            await Unlock(vm);
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
    public async Task Default_save_exports_a_copy_and_never_touches_the_original()
    {
        var (w, vm, dir) = Open(); var dest = Path.Combine(Path.GetTempPath(), "ff2r-uiexp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.False(vm.OverwriteOriginal); Assert.Equal("Export edited copy…", vm.SaveButtonText); await Unlock(vm);
            var orig = File.ReadAllBytes(Path.Combine(dir, "SAVEDATA00", "SAVEDATA.BIN")); var names = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Order().ToList();
            string? asked = null; vm.PickFolder = t => { asked = t; return Task.FromResult<string?>(dest); };
            vm.Fields.First(f => f.Def.Id == "player.senki").Value = "4321"; vm.Fields.First(f => f.Def.Id == "player.sen").Value = "4321";
            vm.SaveCommand.Execute(null); await Pump(() => vm.Status.StartsWith("Exported"));
            Assert.Contains("original is not touched", asked); Assert.StartsWith("Exported edited copy", vm.Status);
            var cfg = CryptoConfig.Load(Path.Combine(Root, "local_config.json")); var copy = SaveFile.Open(Path.Combine(dest, "SAVEDATA00", "SAVEDATA.BIN"), cfg);
            Assert.Empty(copy.LoadProblems); Assert.Equal(4321, copy.GetInt(new FieldSchemaHelper().Senki));
            Assert.True(File.ReadAllBytes(Path.Combine(dir, "SAVEDATA00", "SAVEDATA.BIN")).AsSpan().SequenceEqual(orig)); Assert.Equal(names, Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Order().ToList());
            Shot(w, "after-export.png");
        }
        finally { w.Close(); Directory.Delete(dir, true); if (Directory.Exists(dest)) Directory.Delete(dest, true); }
    }

    sealed class FieldSchemaHelper { public FieldDef Senki => FieldSchema.Load(Path.Combine(Root, "docs", "fields.json"))["player.senki"]; }

    [AvaloniaFact]
    public async Task Overwrite_is_explicit_writes_only_SAVEDATA_backs_up_and_verifies()
    {
        var (w, vm, dir) = Open();
        try
        {
            await Unlock(vm); vm.OverwriteOriginal = true; Assert.Contains("OVER original", vm.SaveButtonText);
            var slot = Path.Combine(dir, "SAVEDATA00"); var backupBefore = File.ReadAllBytes(Path.Combine(slot, "BACKUP.BIN"));
            string? asked = null; vm.Confirm = m => { asked = m; return Task.FromResult(true); };
            vm.Fields.First(f => f.Def.Id == "player.senki").Value = "4321";
            vm.SaveCommand.Execute(null); await Pump(() => vm.Status.StartsWith("Overwrote"));
            Assert.Contains("OVERWRITE THE ORIGINAL", asked); Assert.Contains("BACKUP.BIN is left unchanged", asked); Assert.StartsWith("Overwrote SAVEDATA00", vm.Status);
            Assert.Single(Directory.GetFiles(slot, "*.bak-*")); Assert.True(File.ReadAllBytes(Path.Combine(slot, "BACKUP.BIN")).AsSpan().SequenceEqual(backupBefore));
            var cfg = CryptoConfig.Load(Path.Combine(Root, "local_config.json")); Assert.Empty(SaveFile.Open(Path.Combine(slot, "SAVEDATA.BIN"), cfg).LoadProblems);
            Assert.Equal("4321", vm.Fields.First(f => f.Def.Id == "player.senki").Value); Assert.False(vm.SaveCommand.CanExecute(null));
            Shot(w, "after-overwrite.png");
        }
        finally { w.Close(); Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public async Task Cancelled_overwrite_confirmation_and_cancelled_export_write_nothing()
    {
        var (w, vm, dir) = Open();
        try
        {
            await Unlock(vm); var p = Path.Combine(dir, "SAVEDATA00", "SAVEDATA.BIN"); var before = File.ReadAllBytes(p);
            vm.Fields.First(f => f.Def.Id == "player.senki").Value = "1";
            vm.PickFolder = _ => Task.FromResult<string?>(null); vm.SaveCommand.Execute(null); await Task.Delay(150); Dispatcher.UIThread.RunJobs();
            vm.OverwriteOriginal = true; vm.Confirm = _ => Task.FromResult(false); vm.SaveCommand.Execute(null); await Task.Delay(150); Dispatcher.UIThread.RunJobs();
            Assert.True(File.ReadAllBytes(p).AsSpan().SequenceEqual(before)); Assert.Empty(Directory.GetFiles(dir, "*.bak-*", SearchOption.AllDirectories));
        }
        finally { w.Close(); Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void Missing_config_shows_plain_language_help_instead_of_crashing()
    {
        var vm = new MainViewModel("/nonexistent/local_config.json");
        Assert.StartsWith("Setup problem:", vm.Status); Assert.Contains("tools/keys.py", vm.Status); Assert.Contains("README", vm.Status);
        Assert.False(vm.OpenFolderCommand.CanExecute(null)); vm.OpenPath("/anything"); Assert.Empty(vm.Slots);
        var bad = Path.GetTempFileName(); try { File.WriteAllText(bad, "{ broken"); var vm2 = new MainViewModel(bad); Assert.Contains("not a valid local_config.json", vm2.Status); } finally { File.Delete(bad); }
    }
}

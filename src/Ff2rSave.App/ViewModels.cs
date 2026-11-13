using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Ff2rSave.Core;

namespace Ff2rSave.App;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? n = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Raise(n); return true; }
}

public sealed class RelayCommand(Action<object?> run, Func<object?, bool>? can = null) : ICommand
{
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? p) => can?.Invoke(p) ?? true;
    public void Execute(object? p) => run(p);
}

/// <summary>One slot in the overview list.</summary>
public sealed class SlotVM(SlotFolder folder) : ObservableObject
{
    public SlotFolder Folder { get; } = folder;
    public string Name => System.IO.Path.GetFileName(Folder.Directory);
    public string Summary { get; private set; } = ""; public string IntegrityText { get; private set; } = ""; public string BackupText { get; private set; } = "";
    public Brush IntegrityBrush { get; private set; } = Brushes.Gray;

    public void Refresh()
    {
        try
        {
            var f = Folder.OpenSave(); var h = f.Header;
            var t = TimeSpan.FromSeconds(h.PlayTime / 1_000_000.0); // unit (µs) is a guess: shown as "~"
            Summary = $"Chapter {h.Chapter} · progress {h.Checkpoint} · play time ~{(int)t.TotalHours}h{t.Minutes:D2}m\nSaved {h.Timestamp.LocalDateTime:g} · user ID {h.UserId}";
            IntegrityText = f.LoadProblems.Count == 0 ? "Integrity: OK (checksums valid)" : "Integrity: " + string.Join("; ", f.LoadProblems);
            IntegrityBrush = (Brush)Application.Current.Resources[f.LoadProblems.Count == 0 ? "Good" : "Accent"];
            BackupText = Folder.HasBackup ? (Folder.BackupIsIdentical() ? "BACKUP.BIN: identical to SAVEDATA.BIN" : "BACKUP.BIN: differs (older generation?)") : "BACKUP.BIN: missing";
        }
        catch (Exception ex) { Summary = ""; IntegrityText = "Cannot read: " + ex.Message; IntegrityBrush = Brushes.IndianRed; BackupText = ""; }
        Raise(nameof(Summary)); Raise(nameof(IntegrityText)); Raise(nameof(IntegrityBrush)); Raise(nameof(BackupText));
    }
}

/// <summary>One editable row (a schema field, or one concrete item of a collection field).</summary>
public sealed class FieldVM : ObservableObject, IDataErrorInfo
{
    readonly SaveFile file; readonly string? path; readonly Action changed; string text; string error = "";
    public FieldDef Def { get; }
    public string Label { get; }
    public FieldVM(SaveFile f, FieldDef d, string? concretePath, string label, Action onChanged)
    { file = f; Def = d; path = concretePath; Label = label; changed = onChanged; text = f.GetText(d, concretePath); }

    public string Value
    {
        get => text;
        set
        {
            if (!Def.Editable) return;
            text = value;
            try { file.SetText(Def, value, path); error = ""; }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentOutOfRangeException or InvalidOperationException or SaveFormatException) { error = ex.Message; }
            Raise(); Raise(nameof(HasError)); changed();
        }
    }
    public bool HasError => error != "";
    public bool Unverified => Def.IsUnverified;
    public string StatusText => !Def.Editable ? "read-only" : Def.IsUnverified ? "⚠ unverified (" + Def.Confidence + ")" : "confirmed";
    public string RangeText => Def.Min is null ? "" : $"{Def.Min}…{Def.Max}";
    public string Description => Def.Description;
    public string Error => error; public string this[string c] => c == nameof(Value) ? error : "";
}

public sealed record ByteCell(string Text, bool Changed);
public sealed record HexRow(int Index, string Offset, ByteCell[] Cells, string Ascii);

/// <summary>Lazy row source so a 9 MB payload stays virtualized.</summary>
public sealed class HexRows(byte[] now, byte[] orig) : IList
{
    public int Count => (now.Length + 15) / 16;
    public object? this[int i] { get => Row(i); set => throw new NotSupportedException(); }
    HexRow Row(int i)
    {
        int s = i * 16, n = Math.Min(16, now.Length - s); var cells = new ByteCell[n]; var asc = new char[n];
        for (int k = 0; k < n; k++) { byte b = now[s + k]; cells[k] = new ByteCell(b.ToString("X2"), s + k >= orig.Length || orig[s + k] != b); asc[k] = b is >= 32 and < 127 ? (char)b : '.'; }
        return new HexRow(i, s.ToString("X8"), cells, new string(asc));
    }
    public int FirstChangedRow() { for (int i = 0; i < Math.Min(now.Length, orig.Length); i++) if (now[i] != orig[i]) return i / 16; return -1; }
    public int ChangedByteCount() { int c = 0; for (int i = 0; i < Math.Min(now.Length, orig.Length); i++) if (now[i] != orig[i]) c++; return c; }
    public IEnumerator GetEnumerator() { for (int i = 0; i < Count; i++) yield return Row(i); }
    public int IndexOf(object? v) => v is HexRow r ? r.Index : -1; public bool Contains(object? v) => v is HexRow;
    public bool IsReadOnly => true; public bool IsFixedSize => true; public bool IsSynchronized => false; public object SyncRoot => this;
    public int Add(object? v) => throw new NotSupportedException(); public void Clear() => throw new NotSupportedException(); public void Insert(int i, object? v) => throw new NotSupportedException();
    public void Remove(object? v) => throw new NotSupportedException(); public void RemoveAt(int i) => throw new NotSupportedException(); public void CopyTo(Array a, int i) => throw new NotSupportedException();
}

public sealed class MainViewModel : ObservableObject
{
    CryptoConfig? cfg; FieldSchema? schema; SaveFile? file; SlotVM? selected; string status = ""; string? currentFolder; string? selectedUserFolder; string hexInfo = "";
    HexRows? hexRows;

    public ObservableCollection<SlotVM> Slots { get; } = [];
    public ObservableCollection<FieldVM> Fields { get; } = [];
    public ObservableCollection<string> UserFolders { get; } = [];
    public bool HasMultipleUserFolders => UserFolders.Count > 1;
    public Action<int>? ScrollToRow { get; set; }

    public ICommand OpenFolderCommand { get; }
    public ICommand FindDefaultCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand RefreshHexCommand { get; }
    public ICommand GoToChangeCommand { get; }

    public string Status { get => status; set => Set(ref status, value); }
    public string HexInfo { get => hexInfo; set => Set(ref hexInfo, value); }
    public HexRows? HexRows { get => hexRows; private set => Set(ref hexRows, value); }

    public string? SelectedUserFolder { get => selectedUserFolder; set { if (Set(ref selectedUserFolder, value) && value != null) LoadFolder(value); } }
    public SlotVM? SelectedSlot { get => selected; set { if (Set(ref selected, value)) LoadSlot(); } }

    public MainViewModel()
    {
        OpenFolderCommand = new RelayCommand(_ => OpenFolder(), _ => cfg != null);
        FindDefaultCommand = new RelayCommand(_ => FindDefault(), _ => cfg != null);
        SaveCommand = new RelayCommand(_ => Save(), _ => file?.HasChanges == true && !Fields.Any(f => f.HasError));
        ReloadCommand = new RelayCommand(_ => { LoadSlot(); }, _ => selected != null);
        RefreshHexCommand = new RelayCommand(_ => RefreshHex(), _ => file != null);
        GoToChangeCommand = new RelayCommand(_ => { if (hexRows?.FirstChangedRow() is >= 0 and var r) ScrollToRow?.Invoke(r); }, _ => hexRows != null);
        try
        {
            cfg = CryptoConfig.Load(FindUp("local_config.json") ?? System.IO.Path.Combine(AppContext.BaseDirectory, "local_config.json"));
            schema = FieldSchema.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "fields.json"));
            Status = "Ready. Use “Find default location” or “Open folder…” (a Savedata\\<id> folder or a single slot folder).";
        }
        catch (Exception ex) { Status = "Setup problem: " + ex.Message + "\nCreate local_config.json by running tools/keys.py against your own FatalFrameII.exe and put it next to this program."; }
    }

    static string? FindUp(string name)
    {
        for (var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent) if (System.IO.File.Exists(System.IO.Path.Combine(d.FullName, name))) return System.IO.Path.Combine(d.FullName, name);
        return null;
    }

    void FindDefault()
    {
        var root = SaveLocator.DefaultRoot(); var found = SaveLocator.FindUserFolders();
        UserFolders.Clear(); foreach (var f in found) UserFolders.Add(f); Raise(nameof(HasMultipleUserFolders));
        if (found.Count == 0) { Status = $"Default location checked: {root}\nNo user-ID folders found there (folder missing or empty). Use “Open folder…” to point at your saves."; return; }
        Status = $"Default location: {root}\nFound {found.Count} user-ID folder(s): {string.Join(", ", found.Select(System.IO.Path.GetFileName))}" + (found.Count > 1 ? "\nPick one in the list." : "");
        if (found.Count == 1) LoadFolder(found[0]);
    }

    void OpenFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a Savedata\\<id> folder or a single SAVEDATAnn slot folder" };
        if (dlg.ShowDialog() == true) LoadFolder(dlg.FolderName);
    }

    void LoadFolder(string folder)
    {
        currentFolder = folder; Slots.Clear(); Fields.Clear(); file = null; HexRows = null;
        var slots = SaveLocator.ResolveSlots(folder);
        foreach (var s in slots) { var vm = new SlotVM(new SlotFolder(s, cfg!)); vm.Refresh(); Slots.Add(vm); }
        Status = slots.Count == 0 ? $"No SAVEDATA.BIN found in {folder} or its SAVEDATA* sub-folders." : $"Opened {folder}: {slots.Count} slot(s). Select one to edit.";
        if (Slots.Count > 0) SelectedSlot = Slots[0];
    }

    void LoadSlot()
    {
        Fields.Clear(); file = null; HexRows = null; HexInfo = "";
        if (selected == null) return;
        try
        {
            file = selected.Folder.OpenSave();
            foreach (var d in schema!.Fields)
            {
                if (d.Source == "json" && d.Collection)
                {
                    foreach (var p in file.Expand(d.Path))
                    {
                        if (file.GetInt(d, p) == 0) continue; // only owned items
                        var baseP = p[..p.LastIndexOf('/')]; var keyPath = baseP + "/key"; string key = "";
                        try { key = " key=" + file.GetText(schema["item.key"], keyPath); } catch { }
                        Fields.Add(new FieldVM(file, d, p, $"{d.Name} {p.Split('/')[^3]}{key}", OnChanged));
                    }
                }
                else Fields.Add(new FieldVM(file, d, null, d.Name, OnChanged));
            }
            Status = $"{selected.Name}: {Fields.Count} fields. " + (file.LoadProblems.Count == 0 ? "Integrity OK." : "Integrity problems: " + string.Join("; ", file.LoadProblems));
        }
        catch (Exception ex) { Status = $"Cannot open {selected.Name}: {ex.Message}"; }
    }

    void OnChanged() { CommandManager.InvalidateRequerySuggested(); if (file?.HasChanges == true) Status = $"{selected?.Name}: unsaved changes."; }

    void RefreshHex()
    {
        if (file == null) return;
        try { var rows = new HexRows(file.BuildPlaintext(), file.OriginalPlaintext); HexRows = rows; HexInfo = $"  {file.OriginalPlaintext.Length:N0} bytes · {rows.ChangedByteCount():N0} changed (red) vs. original"; }
        catch (Exception ex) { HexInfo = "  " + ex.Message; }
    }

    void Save()
    {
        if (file == null || selected == null) return;
        var unverified = Fields.Where(f => f.Unverified && f.Def.Editable).Count();
        var ok = MessageBox.Show($"Write {selected.Name}\\SAVEDATA.BIN and BACKUP.BIN?\n\nTimestamped .bak-* copies of both files are made first, then the files are re-read and verified.\n" +
            "Edits to ⚠ unverified fields are untested in the game; keep your own backup.", "Confirm save", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.OK) return;
        try
        {
            var made = selected.Folder.Save(file);
            var name = selected.Name; selected.Refresh(); LoadSlot();
            Status = $"Saved {name}. Verified OK. Backups: {string.Join(", ", made.Select(System.IO.Path.GetFileName))}";
        }
        catch (Exception ex) { Status = "Save FAILED: " + ex.Message + " (originals were backed up before writing; check the .bak-* files)."; MessageBox.Show(Status, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}

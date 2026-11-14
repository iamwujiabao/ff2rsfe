using System.Security.Cryptography;
using System.Text.Json;
using Ff2rSave.Core;
using Xunit;

namespace Ff2rSave.Tests;

public class RoundTripAndParity
{
    [Fact]
    public void Samples_are_present() => Assert.True(Fixture.AllBins().Count() >= 14, "expected the 14 sample .BIN files under samples/");

    [Fact]
    public void Every_sample_decrypts_verifies_and_reencrypts_byte_exact()
    {
        var cfg = Fixture.Cfg; var codec = new SaveCodec(cfg); var integ = new IntegrityService(cfg);
        foreach (var path in Fixture.AllBins())
        {
            var raw = File.ReadAllBytes(path); var (h, pt) = codec.Decrypt(raw);
            Assert.Empty(integ.Verify(h, pt, raw.Length));
            var h2 = new SaveHeader(h.Raw); integ.Fix(h2, pt);
            Assert.True(codec.Encrypt(h2, pt).AsSpan().SequenceEqual(raw), $"round trip differs: {path}");
        }
    }

    [Fact]
    public void Unmodified_save_serializes_to_identical_bytes_for_all_samples()
    {
        var cfg = Fixture.Cfg;
        foreach (var path in Fixture.AllBins()) { var raw = File.ReadAllBytes(path); Assert.True(SaveFile.FromBytes(raw, cfg).ToBytes().AsSpan().SequenceEqual(raw), path); }
    }

    [Fact]
    public void Compact_json_reserialization_is_byte_identical() // guards the editor's write path against formatting drift
    {
        var cfg = Fixture.Cfg;
        foreach (var path in Fixture.AllBins().Where(p => !p.Contains("SYSTEM")).Take(4))
        {
            var f = SaveFile.Open(path, cfg); var orig = f.OriginalPlaintext.AsSpan(SaveFile.JsonStart, f.OriginalJsonLength).ToArray();
            var again = System.Text.Encoding.UTF8.GetBytes(f.Root.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            Assert.True(orig.AsSpan().SequenceEqual(again), path);
        }
    }

    [Fact] // golden.json is produced by tools/make_golden.py (Python implementation)
    public void Csharp_matches_python_decryption_and_checksums()
    {
        var gold = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixture.Root, "tests", "golden.json"))).RootElement;
        var cfg = Fixture.Cfg; var codec = new SaveCodec(cfg); var integ = new IntegrityService(cfg); int n = 0;
        foreach (var g in gold.EnumerateObject())
        {
            var raw = File.ReadAllBytes(Path.Combine(Fixture.Samples, g.Name)); var (h, pt) = codec.Decrypt(raw);
            Assert.Equal(g.Value.GetProperty("plaintext_sha256").GetString(), Convert.ToHexString(SHA256.HashData(pt)).ToLowerInvariant());
            Assert.Equal(g.Value.GetProperty("chk_payload").GetString(), Convert.ToHexString(integ.Chk32(pt)).ToLowerInvariant());
            Assert.Equal(g.Value.GetProperty("header_chk").GetString(), Convert.ToHexString(h.Raw.AsSpan(0x70, 32)).ToLowerInvariant()); n++;
        }
        Assert.Equal(14, n);
    }
}

public class EditingTests
{
    static (SaveFile f, FieldSchema s) Open() { var f = SaveFile.Open(Fixture.Mine("SAVEDATA00"), Fixture.Cfg); f.AllowUnverified = true; return (f, Fixture.Schema); }

    [Fact]
    public void Edit_save_reload_has_valid_integrity_and_only_expected_changes()
    {
        var cfg = Fixture.Cfg; var (f, s) = Open();
        f.SetInt(s["player.senki"], 12345); f.SetInt(s["player.sen"], 12345);
        var bytes = f.ToBytes(); var g = SaveFile.FromBytes(bytes, cfg);
        Assert.Empty(g.LoadProblems);
        Assert.Equal(12345, g.GetInt(s["player.senki"])); Assert.Equal(12345, g.GetInt(s["player.sen"]));
        Assert.Equal(f.GetInt(s["player.total_play_time"]), g.GetInt(s["player.total_play_time"]));
        var orig = f.OriginalPlaintext; var now = new SaveCodec(cfg).Decrypt(bytes).Plaintext;
        Assert.True(orig.AsSpan(SaveFile.JsonAreaEnd).SequenceEqual(now.AsSpan(SaveFile.JsonAreaEnd)), "binary tail must be untouched");
    }

    [Fact]
    public void Editing_progress_syncs_header_copies_and_money_shrinks_json_with_zero_padding()
    {
        var (f, s) = Open(); f.SetInt(s["mission.progress"], 9036);
        var g = SaveFile.FromBytes(f.ToBytes(), Fixture.Cfg);
        Assert.Equal(9036u, g.Header.Checkpoint); Assert.Equal(9u, g.Header.Chapter); Assert.Empty(g.LoadProblems);
    }

    [Fact]
    public void Rebind_changes_only_header_and_stays_valid()
    {
        var cfg = Fixture.Cfg; var f = SaveFile.Open(Fixture.Mine("SAVEDATA00"), cfg); var s = Fixture.Schema;
        f.SetInt(s["hdr.user_id"], 123456789);
        var g = SaveFile.FromBytes(f.ToBytes(), cfg);
        Assert.Equal(123456789u, g.Header.UserId); Assert.Empty(g.LoadProblems);
        Assert.True(g.OriginalPlaintext.AsSpan().SequenceEqual(f.OriginalPlaintext), "payload must not change on rebind");
    }

    [Fact]
    public void Collection_field_needs_concrete_path_and_edits_one_item()
    {
        var (f, s) = Open(); var def = s["item.num"];
        Assert.Throws<InvalidOperationException>(() => f.GetInt(def));
        var path = f.Expand(def.Path).First(); var old = f.GetInt(def, path);
        f.SetInt(def, old + 1, path);
        Assert.Equal(old + 1, SaveFile.FromBytes(f.ToBytes(), Fixture.Cfg).GetInt(def, path));
    }

    [Fact]
    public void Validation_rejects_out_of_range_and_read_only()
    {
        var (f, s) = Open();
        Assert.Throws<ArgumentOutOfRangeException>(() => f.SetInt(s["player.senki"], -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => f.SetInt(s["player.unlock_amulet_slot_num"], 9));
        Assert.Throws<InvalidOperationException>(() => f.SetInt(s["hdr.chapter"], 1));
    }

    [Fact]
    public void Oversized_json_is_refused_not_truncated()
    {
        var (f, _) = Open(); f.Root["PlayerData"]!["big"] = new string('x', 300_000);
        typeof(SaveFile).GetProperty("Dirty")!.SetValue(f, true);
        Assert.Throws<SaveFormatException>(() => f.ToBytes());
    }
}

public class CorruptionAndSchemaTests
{
    [Fact]
    public void Flipped_payload_byte_is_detected_or_rejected()
    {
        var raw = File.ReadAllBytes(Fixture.Mine("SAVEDATA00")); raw[0x100 + 200_000] ^= 1;
        try { Assert.NotEmpty(SaveFile.FromBytes(raw, Fixture.Cfg).LoadProblems); } catch (SaveFormatException) { /* also acceptable: clear error */ }
    }
    [Fact]
    public void Flipped_header_byte_fails_header_checksum()
    {
        var raw = File.ReadAllBytes(Fixture.Mine("SAVEDATA00")); raw[0x20] ^= 1;
        Assert.Contains(SaveFile.FromBytes(raw, Fixture.Cfg).LoadProblems, p => p.Contains("0x70"));
    }
    [Fact] public void Truncated_file_gives_clear_error() => Assert.Contains("multiple of 16", Assert.Throws<SaveFormatException>(() => SaveFile.FromBytes(File.ReadAllBytes(Fixture.Mine("SAVEDATA00"))[..^7], Fixture.Cfg)).Message);
    [Fact] public void Tiny_file_gives_clear_error() => Assert.Contains("too small", Assert.Throws<SaveFormatException>(() => SaveFile.FromBytes(new byte[50], Fixture.Cfg)).Message);
    [Fact] public void Bad_magic_gives_clear_error() { var raw = File.ReadAllBytes(Fixture.Mine("SAVEDATA00")); raw[0] = (byte)'X'; Assert.Contains("magic", Assert.Throws<SaveFormatException>(() => SaveFile.FromBytes(raw, Fixture.Cfg)).Message); }
    [Fact] public void Missing_config_gives_clear_error() => Assert.Contains("local_config", Assert.Throws<SaveFormatException>(() => Ff2rSave.Core.CryptoConfig.Load("/nonexistent/local_config.json")).Message);

    [Fact]
    public void Schema_loads_and_every_json_field_resolves_in_every_user_sample()
    {
        var s = Fixture.Schema; var cfg = Fixture.Cfg; Assert.True(s.Fields.Count >= 30);
        foreach (var p in Fixture.AllBins().Where(p => !p.Contains("SYSTEM")))
        { var f = SaveFile.Open(p, cfg); foreach (var d in s.Fields.Where(d => d.Source == "json")) Assert.NotEmpty(f.Expand(d.Path)); }
    }
}

public class SchemaConsistencyTests
{
    [Fact]
    public void Wildcard_paths_are_flagged_collection_and_every_field_text_roundtrips()
    {
        var s = Fixture.Schema; var f = SaveFile.Open(Fixture.Mine("SAVEDATA00"), Fixture.Cfg);
        foreach (var d in s.Fields.Where(d => d.Source == "json" && d.Path.Contains('*'))) Assert.True(d.Collection, $"{d.Id} has a wildcard path but collection != true");
        foreach (var d in s.Fields) // what the UI does for every row must not throw
            foreach (var p in d.Source == "json" && d.Collection ? f.Expand(d.Path).Take(5) : [null]) Assert.NotNull(f.GetText(d, p));
    }
}

public class AppliedProposalsTests
{
    [Fact]
    public void Schema_reflects_the_applied_observations_and_keeps_the_evidence_rules()
    {
        var s = Fixture.Schema;
        Assert.Equal("likely", s["hdr.u48"].Confidence); Assert.Equal("likely", s["record.ngp_tutorial"].Confidence); Assert.False(s["hdr.u48"].Editable);
        Assert.Equal(new[] { "hdr.checkpoint", "hdr.chapter", "hdr.payload_len", "hdr.play_time", "hdr.timestamp", "hdr.user_id", "mission.progress" }.Order(), s.Fields.Where(f => f.Confidence == "confirmed").Select(f => f.Id).Order()); // uncontrolled data confirmed nothing new
        Assert.Equal(25, s["mission.progress"].ObservedValues!.Length); Assert.Contains(9030L, s["mission.progress"].ObservedValues!);
        Assert.Equal(10, s["player.current_film_slot"].Max); // optional tightening was deliberately NOT applied
        foreach (var id in new[] { "item.key_num", "item.entry_number" }) { var d = s[id]; Assert.True(d.Collection && !d.Editable && d.Confidence == "guessed"); }
        var f = SaveFile.Open(Fixture.Mine("SAVEDATA00"), Fixture.Cfg); Assert.Equal(650, f.Expand(s["item.key_num"].Path).Count());
        Assert.Throws<InvalidOperationException>(() => f.SetInt(s["item.key_num"], 1, f.Expand(s["item.key_num"].Path).First()));
    }

    [Fact]
    public void Header_0x48_packed_code_formula_holds_in_every_sample()
    {
        var s = Fixture.Schema; int n = 0;
        foreach (var p in Fixture.AllBins().Where(p => !p.Contains("SYSTEM") && p.EndsWith("SAVEDATA.BIN")))
        {
            var f = SaveFile.Open(p, Fixture.Cfg); long ce = f.GetInt(s["record.current_end"]); long d3 = f.GetBool(s["record.difficulty_clear"], "/PlayRecordData/difficulty_clear_flag_/3") ? 1 : 0; long tut = f.GetBool(s["record.ngp_tutorial"]) ? 1 : 0;
            Assert.Equal(ce + 10 * d3 + 100 * tut, f.GetInt(s["hdr.u48"])); n++;
        }
        Assert.True(n >= 3);
    }
}

public class SystemSaveTests
{
    [Fact]
    public void System_fields_are_all_guessed_read_only_unique_and_resolve_in_every_system_sample()
    {
        var s = Fixture.SchemaWithSystem; var sys = s.For(true).ToList();
        Assert.Equal(108, sys.Count); Assert.All(sys, d => { Assert.Equal("system", d.Scope); Assert.Equal("guessed", d.Confidence); Assert.False(d.Editable); Assert.StartsWith("sys.", d.Id); });
        Assert.DoesNotContain(s.For(false), d => d.Id.StartsWith("sys.")); Assert.Equal(s.Fields.Count, s.Fields.Select(f => f.Id).Distinct().Count());
        var files = Fixture.SystemBins().ToList(); Assert.True(files.Count >= 6);
        foreach (var path in files) { var f = SaveFile.Open(path, Fixture.Cfg); Assert.Empty(f.LoadProblems); foreach (var d in sys) Assert.NotEmpty(f.Expand(d.Path)); }
    }

    [Fact]
    public void Duplicate_ids_across_the_field_files_are_rejected()
    {
        var tmp = Path.GetTempFileName();
        try { File.WriteAllText(tmp, "{\"fields\":[{\"id\":\"hdr.user_id\",\"path\":\"/x\"}]}"); Assert.Throws<SaveFormatException>(() => FieldSchema.Load(Path.Combine(Fixture.Root, "docs", "fields.json"), tmp)); }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void System_saves_refuse_every_edit_even_with_unverified_unlocked_and_round_trip_unchanged_without_a_second_copy()
    {
        var (full, empty) = Fixture.SystemExtremes(); var s = Fixture.SchemaWithSystem; var f = SaveFile.Open(full, Fixture.Cfg); f.AllowUnverified = true;
        var ex = Assert.Throws<InvalidOperationException>(() => f.SetInt(s["sys.record.shop_point"], 1)); Assert.Contains("read-only", ex.Message);
        Assert.Throws<InvalidOperationException>(() => f.SetText(s["sys.record.ending_cleared_flag"], "false", "/SystemPlayerRecordData/ending_cleared_flag_/0"));
        Assert.Throws<InvalidOperationException>(() => f.SetInt(s["hdr.user_id"], 5)); // header fields too
        Assert.False(f.HasChanges); Assert.Same(f.OriginalPlaintext, f.BuildPlaintext()); // memory: no 60 MB copy
        Assert.True(f.ToBytes().AsSpan().SequenceEqual(File.ReadAllBytes(full)));
        Assert.Equal((5, 5), f.Summarize(s["sys.record.ending_cleared_flag"].Path)); Assert.Equal((5, 0), SaveFile.Open(empty, Fixture.Cfg).Summarize(s["sys.record.ending_cleared_flag"].Path));
    }

    [Fact]
    public void Saving_or_exporting_a_system_slot_is_refused_and_writes_nothing()
    {
        var (full, _) = Fixture.SystemExtremes(); var dir = Fixture.TempSlotFrom(Path.GetDirectoryName(full)!);
        try
        {
            var slot = new SlotFolder(dir, Fixture.Cfg); var f = slot.OpenSave(); var before = File.ReadAllBytes(slot.SavePath); var names = Directory.GetFiles(dir).Select(Path.GetFileName).Order().ToList();
            Assert.Throws<SaveFormatException>(() => slot.Save(f)); Assert.Throws<SaveFormatException>(() => slot.Export(f, dir + "-copy"));
            Assert.True(File.ReadAllBytes(slot.SavePath).AsSpan().SequenceEqual(before)); Assert.Equal(names, Directory.GetFiles(dir).Select(Path.GetFileName).Order().ToList()); Assert.False(Directory.Exists(dir + "-copy"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Diff_of_two_system_saves_maps_changes_to_system_field_ids()
    {
        var (full, empty) = Fixture.SystemExtremes(); var s = Fixture.SchemaWithSystem;
        var d = SaveDiff.Compare(SaveFile.Open(empty, Fixture.Cfg), SaveFile.Open(full, Fixture.Cfg), s);
        Assert.Contains(d.Json, e => e.FieldId == "sys.record.ending_cleared_flag" && e.Old == "false" && e.New == "true"); Assert.Contains(d.Json, e => e.FieldId == "sys.record.shop_point");
        Assert.All(d.Json.Where(e => e.FieldId != null), e => Assert.StartsWith("sys.", e.FieldId));
    }

    [Fact]
    public void Locator_lists_the_system_folder_after_the_user_slots()
    {
        var t = Path.Combine(Path.GetTempPath(), "ff2r-sysloc-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var n in new[] { "SYSTEMSAVEDATA00", "SAVEDATA01", "SAVEDATA00" }) { Directory.CreateDirectory(Path.Combine(t, n)); File.WriteAllBytes(Path.Combine(t, n, "SAVEDATA.BIN"), [1]); }
            Assert.Equal(["SAVEDATA00", "SAVEDATA01", "SYSTEMSAVEDATA00"], SaveLocator.ResolveSlots(t).Select(Path.GetFileName).ToArray());
        }
        finally { Directory.Delete(t, true); }
    }
}

public class SlotAndBackupTests
{
    static string TempSlot() => Fixture.TempSlotFrom(Path.GetDirectoryName(Fixture.Mine("SAVEDATA00"))!);
    static SaveFile Edited(SlotFolder slot) { var f = slot.OpenSave(); f.AllowUnverified = true; var s = Fixture.Schema; f.SetInt(s["player.senki"], 777); f.SetInt(s["player.sen"], 777); return f; }

    [Fact]
    public void Default_save_writes_only_SAVEDATA_leaves_BACKUP_untouched_and_verifies()
    {
        var dir = TempSlot();
        try
        {
            var slot = new SlotFolder(dir, Fixture.Cfg); var before = File.ReadAllBytes(slot.SavePath); var backupBefore = File.ReadAllBytes(slot.BackupPath);
            var backups = slot.Save(Edited(slot));
            Assert.Single(backups); Assert.True(File.ReadAllBytes(backups[0]).AsSpan().SequenceEqual(before));
            Assert.True(File.ReadAllBytes(slot.BackupPath).AsSpan().SequenceEqual(backupBefore), "BACKUP.BIN must not change");
            Assert.Equal(777, slot.OpenSave().GetInt(Fixture.Schema["player.senki"])); Assert.Empty(Directory.GetFiles(dir, "*.tmp-*"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Both_policy_writes_identical_content_to_both_files_with_two_backups()
    {
        var dir = TempSlot();
        try
        {
            var slot = new SlotFolder(dir, Fixture.Cfg); Assert.Equal(2, slot.Save(Edited(slot), BackupFilePolicy.Both).Count);
            Assert.True(slot.BackupIsIdentical()); Assert.Equal(777, slot.OpenBackup().GetInt(Fixture.Schema["player.senki"]));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Failed_verification_leaves_original_untouched_and_no_temp_files()
    {
        var dir = TempSlot();
        try
        {
            var p = Path.Combine(dir, "SAVEDATA.BIN"); var before = File.ReadAllBytes(p);
            Assert.Throws<SaveFormatException>(() => AtomicFile.WriteVerified(p, [1, 2, 3], _ => ["simulated verification problem"]));
            Assert.Throws<SaveFormatException>(() => AtomicFile.WriteVerified(p, [1, 2, 3], _ => throw new SaveFormatException("decrypt failed")));
            Assert.True(File.ReadAllBytes(p).AsSpan().SequenceEqual(before)); Assert.Empty(Directory.GetFiles(dir, "*.tmp-*")); Assert.Empty(Directory.GetFiles(dir, "*.bak-*"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Unwritable_target_directory_fails_cleanly_without_touching_the_original()
    {
        var dir = TempSlot();
        try
        {
            var slot = new SlotFolder(dir, Fixture.Cfg); var f = Edited(slot); var before = File.ReadAllBytes(slot.SavePath);
            // a directory squatting on the temp-file's final name is not possible; instead make the target a directory-less path
            Assert.ThrowsAny<IOException>(() => AtomicFile.WriteVerified(Path.Combine(dir, "nope", "SAVEDATA.BIN"), f.ToBytes(), _ => []));
            Assert.True(File.ReadAllBytes(slot.SavePath).AsSpan().SequenceEqual(before));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Export_writes_a_verified_copy_and_never_touches_the_original()
    {
        var dir = TempSlot(); var dest = Path.Combine(Path.GetTempPath(), "ff2r-exp-" + Guid.NewGuid().ToString("N"), "SAVEDATA00");
        try
        {
            var slot = new SlotFolder(dir, Fixture.Cfg); var s1 = File.ReadAllBytes(slot.SavePath); var b1 = File.ReadAllBytes(slot.BackupPath); var names = Directory.GetFiles(dir).Select(Path.GetFileName).Order().ToList();
            var written = slot.Export(Edited(slot), dest);
            Assert.Equal(777, SaveFile.Open(written, Fixture.Cfg).GetInt(Fixture.Schema["player.senki"])); Assert.Empty(SaveFile.Open(written, Fixture.Cfg).LoadProblems);
            Assert.True(File.ReadAllBytes(Path.Combine(dest, "BACKUP.BIN")).AsSpan().SequenceEqual(b1), "BACKUP.BIN is copied unchanged");
            Assert.True(File.ReadAllBytes(slot.SavePath).AsSpan().SequenceEqual(s1)); Assert.Equal(names, Directory.GetFiles(dir).Select(Path.GetFileName).Order().ToList());
        }
        finally { Directory.Delete(dir, true); if (Directory.Exists(Path.GetDirectoryName(dest))) Directory.Delete(Path.GetDirectoryName(dest)!, true); }
    }

    [Fact]
    public void Export_refuses_the_original_folder_and_folders_inside_it()
    {
        var dir = TempSlot();
        try
        {
            var slot = new SlotFolder(dir, Fixture.Cfg); var f = Edited(slot);
            Assert.Throws<SaveFormatException>(() => slot.Export(f, dir)); Assert.Throws<SaveFormatException>(() => slot.Export(f, Path.Combine(dir, "sub")));
            Assert.Throws<SaveFormatException>(() => slot.Export(f, dir + Path.DirectorySeparatorChar));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact] public void Backup_of_missing_file_returns_null() => Assert.Null(BackupService.Backup("/nonexistent/x.BIN"));
}

public class SafetyPolicyTests
{
    [Fact]
    public void Unverified_fields_are_locked_by_default_confirmed_ones_are_not()
    {
        var f = SaveFile.Open(Fixture.Mine("SAVEDATA00"), Fixture.Cfg); var s = Fixture.Schema;
        var ex = Assert.Throws<InvalidOperationException>(() => f.SetInt(s["player.senki"], 1)); Assert.Contains("locked", ex.Message);
        Assert.Throws<InvalidOperationException>(() => f.SetText(s["record.ngp_tutorial"], "true"));
        f.SetInt(s["mission.progress"], 9031); f.SetInt(s["hdr.user_id"], 5);          // confirmed + editable
        f.AllowUnverified = true; f.SetInt(s["player.senki"], 1); Assert.Equal(1, f.GetInt(s["player.senki"]));
    }

    [Fact]
    public void Config_problems_give_plain_language_errors()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ff2r-cfg-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var e1 = Assert.Throws<SaveFormatException>(() => Ff2rSave.Core.CryptoConfig.Find([dir])); Assert.Contains("tools/keys.py", e1.Message); Assert.Contains("not found", e1.Message);
            var e2 = Assert.Throws<SaveFormatException>(() => Ff2rSave.Core.CryptoConfig.Find([dir], Path.Combine(dir, "x.json"))); Assert.Contains("does not exist", e2.Message);
            foreach (var bad in new[] { "not json", "{}", "{\"aes_key\":\"zz\",\"aes_iv\":\"00\",\"chk_A\":\"00\",\"chk_B\":\"00\"}", "{\"aes_key\":\"00\",\"aes_iv\":\"00\",\"chk_A\":\"00\",\"chk_B\":\"00\"}" })
            {
                var p = Path.Combine(dir, "local_config.json"); File.WriteAllText(p, bad);
                var ex = Assert.Throws<SaveFormatException>(() => Ff2rSave.Core.CryptoConfig.Load(p)); Assert.Contains("tools/keys.py", ex.Message);
            }
            Assert.Throws<SaveFormatException>(() => FieldSchema.Load(Path.Combine(dir, "missing-fields.json")));
            File.WriteAllText(Path.Combine(dir, "f.json"), "{"); Assert.Throws<SaveFormatException>(() => FieldSchema.Load(Path.Combine(dir, "f.json")));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class DiffAndItemsTests
{
    static SaveFile Mine(string slot, string file = "SAVEDATA.BIN") => SaveFile.Open(Fixture.Mine(slot, file), Fixture.Cfg);

    [Fact]
    public void Identical_saves_have_an_empty_diff()
    {
        var d = SaveDiff.Compare(Mine("SAVEDATA00"), Mine("SAVEDATA00"), Fixture.Schema);
        Assert.Empty(d.Header); Assert.Empty(d.Json); Assert.Equal(0, d.TailChangedBytes);
    }

    [Fact]
    public void A_two_field_edit_shows_up_as_exactly_those_two_mapped_changes()
    {
        var a = Mine("SAVEDATA00"); var b = Mine("SAVEDATA00"); b.AllowUnverified = true; var s = Fixture.Schema;
        b.SetInt(s["player.senki"], 111); b.SetInt(s["player.sen"], 111);
        var d = SaveDiff.Compare(a, SaveFile.FromBytes(b.ToBytes(), Fixture.Cfg), s);
        Assert.Empty(d.Header); Assert.Equal(0, d.TailChangedBytes);
        Assert.Equal(["player.sen", "player.senki"], d.Json.Select(e => e.FieldId!).Order().ToArray());
        Assert.All(d.Json, e => { Assert.Equal("5865", e.Old); Assert.Equal("111", e.New); });
    }

    [Fact]
    public void Progress_edit_maps_header_copies_and_json_value()
    {
        var a = Mine("SAVEDATA00"); var b = Mine("SAVEDATA00"); var s = Fixture.Schema; b.SetInt(s["mission.progress"], 9036);
        var d = SaveDiff.Compare(a, SaveFile.FromBytes(b.ToBytes(), Fixture.Cfg), s);
        Assert.Equal(["hdr.checkpoint"], d.Header.Select(e => e.FieldId!).ToArray()); // chapter stays 9 (9030 -> 9036)
        Assert.Equal(["mission.progress"], d.Json.Select(e => e.FieldId!).ToArray());
    }

    [Fact]
    public void Save_vs_older_backup_maps_known_fields_and_reports_unmapped_and_tail()
    {
        var s = Fixture.Schema; var d = SaveDiff.Compare(Mine("SAVEDATA00", "BACKUP.BIN"), Mine("SAVEDATA00"), s);
        Assert.Contains(d.Header, e => e.FieldId == "hdr.checkpoint" && e.Old == "9015" && e.New == "9030");
        Assert.Contains(d.Json, e => e.FieldId == "mission.progress" && e.Old == "9015" && e.New == "9030");
        Assert.NotEmpty(d.Unmapped); Assert.True(d.TailChangedBytes > 0 && d.TailRanges > 0);
    }

    [Fact]
    public void Paths_outside_fields_json_are_reported_unmapped_and_added_or_removed_paths_show_absent()
    {
        var a = Mine("SAVEDATA00"); var b = Mine("SAVEDATA00");
        b.Root["UIData"]!["projector_set_film_item_label_"] = 42; b.Root["UIData"]!["brand_new_key"] = true;
        var d = SaveDiff.Compare(a, b, Fixture.Schema);
        Assert.Contains(d.Json, e => e.Location == "/UIData/projector_set_film_item_label_" && e.FieldId == null && e.New == "42");
        Assert.Contains(d.Json, e => e.Location == "/UIData/brand_new_key" && e.Old == null && e.New == "true" && e.FieldId == null);
    }

    [Fact]
    public void Item_listing_skips_empty_slots_and_resolves_names_from_the_catalog()
    {
        var f = Mine("SAVEDATA00"); var rows = ItemListing.Rows(f, ItemCatalog.Empty);
        Assert.Equal(166, rows.Count); Assert.Equal(185346688, rows[0].Key); Assert.All(rows, r => Assert.True(r.Key != 0 || r.Num != 0)); Assert.All(rows, r => Assert.Null(r.Name));
        Assert.Equal(1200, ItemListing.Rows(f, ItemCatalog.Empty, all: true).Count);
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "{\"schema\":1,\"items\":[{\"key\":185346688,\"name\":\"Test item\",\"source\":\"unit test\",\"confidence\":\"guessed\"}]}");
            var named = ItemListing.Rows(f, ItemCatalog.Load(tmp)); Assert.Equal("Test item", named[0].Name); Assert.Equal("guessed", named[0].NameConfidence); Assert.Single(named, r => r.Name != null);
            File.WriteAllText(tmp, "{\"items\":[{\"name\":\"no key\"}]}");
            Assert.Throws<SaveFormatException>(() => ItemCatalog.Load(tmp));
            File.WriteAllText(tmp, "{ broken"); Assert.Throws<SaveFormatException>(() => ItemCatalog.Load(tmp));
            File.WriteAllText(tmp, "{\"items\":[{\"key\":0,\"name\":\"zero\"}]}"); Assert.Throws<SaveFormatException>(() => ItemCatalog.Load(tmp));
            File.WriteAllText(tmp, "{\"items\":[{\"key\":5,\"name\":\"a\"},{\"key\":5,\"name\":\"b\"}]}"); Assert.Throws<SaveFormatException>(() => ItemCatalog.Load(tmp)); // duplicate key
        }
        finally { File.Delete(tmp); }
        Assert.Equal(0, ItemCatalog.Load("/nonexistent/items.json").Count); // missing file is fine
        ItemCatalog.Load(Path.Combine(Fixture.Root, "docs", "items.json")); // the shipped file parses
    }
}

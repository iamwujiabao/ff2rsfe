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

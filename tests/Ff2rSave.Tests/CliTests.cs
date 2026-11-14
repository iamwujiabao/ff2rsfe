using Ff2rSave.Cli;
using Ff2rSave.Core;
using Xunit;

namespace Ff2rSave.Tests;

public class CliTests
{
    static string Cfg => Path.Combine(Fixture.Root, "local_config.json");
    static (int code, string outp, string err) Run(params string[] a)
    {
        var o = new StringWriter(); var e = new StringWriter();
        var code = Program.Run([.. a, "--config", Cfg, "--fields", Path.Combine(Fixture.Root, "docs", "fields.json")], o, e); return (code, o.ToString(), e.ToString());
    }
    static string Slot() => Fixture.TempSlotFrom(Path.GetDirectoryName(Fixture.Mine("SAVEDATA00"))!);

    [Fact] public void Info_reports_integrity_ok_and_fields()
    {
        var (c, o, _) = Run("info", Fixture.Mine("SAVEDATA00")); Assert.Equal(0, c);
        Assert.Contains("integrity   OK", o); Assert.Contains(SaveFile.Open(Fixture.Mine("SAVEDATA00"), Fixture.Cfg).Header.UserId.ToString(), o); // the id comes from the file, never hard-coded Assert.Contains("mission.progress", o);
    }

    [Fact] public void Verify_ok_for_all_samples_and_fails_for_corrupt()
    {
        var (c, o, _) = Run("verify", Fixture.Samples); Assert.Equal(0, c); int n = Fixture.AllBins().Count(); Assert.Contains($"{n}/{n} OK", o); Assert.True(n >= 14);
        var d = Slot(); try
        {
            var p = Path.Combine(d, "SAVEDATA.BIN"); var raw = File.ReadAllBytes(p); raw[0x21] ^= 1; File.WriteAllBytes(p, raw);
            var (c2, o2, _) = Run("verify", p); Assert.Equal(1, c2); Assert.Contains("FAIL", o2);
        } finally { Directory.Delete(d, true); }
    }

    [Fact] public void Set_refuses_without_out_or_in_place_and_without_unverified_flag()
    {
        var d = Slot(); try
        {
            var before = File.ReadAllBytes(Path.Combine(d, "SAVEDATA.BIN"));
            var (c, _, e) = Run("set", d, "player.senki", "9999", "--allow-unverified"); Assert.Equal(2, c); Assert.Contains("refusing to modify the original", e);
            var (c2, _, e2) = Run("set", d, "player.senki", "9999", "--in-place"); Assert.Equal(2, c2); Assert.Contains("locked", e2);
            Assert.Equal(2, Run("set", d, "player.senki", "9999", "--in-place", "--out", Path.Combine(d, "x")).code);
            Assert.True(File.ReadAllBytes(Path.Combine(d, "SAVEDATA.BIN")).AsSpan().SequenceEqual(before)); Assert.Empty(Directory.GetFiles(d, "*.bak-*"));
        } finally { Directory.Delete(d, true); }
    }

    [Fact] public void Set_out_writes_an_edited_copy_and_leaves_the_original_untouched()
    {
        var d = Slot(); var o = Path.Combine(Path.GetTempPath(), "ff2r-out-" + Guid.NewGuid().ToString("N")); try
        {
            var before = File.ReadAllBytes(Path.Combine(d, "SAVEDATA.BIN")); var names = Directory.GetFiles(d).Select(Path.GetFileName).Order().ToList();
            var (c, so, e) = Run("set", d, "player.senki", "9999", "--out", o, "--allow-unverified"); Assert.True(c == 0, e + so); Assert.Contains("original untouched", so);
            Assert.Equal("9999", Run("get", Path.Combine(o, "SAVEDATA.BIN"), "player.senki").outp.Trim()); Assert.Equal(0, Run("verify", o).code);
            Assert.True(File.ReadAllBytes(Path.Combine(d, "SAVEDATA.BIN")).AsSpan().SequenceEqual(before)); Assert.Equal(names, Directory.GetFiles(d).Select(Path.GetFileName).Order().ToList());
            // file input, --out as a folder
            var o2 = Path.Combine(Path.GetTempPath(), "ff2r-out2-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(o2);
            try { Assert.Equal(0, Run("set", Path.Combine(d, "SAVEDATA.BIN"), "player.sen", "5", "--out", o2, "--allow-unverified").code); Assert.True(File.Exists(Path.Combine(o2, "SAVEDATA.BIN"))); Assert.Equal(2, Run("set", Path.Combine(d, "SAVEDATA.BIN"), "player.sen", "5", "--out", Path.Combine(d, "SAVEDATA.BIN"), "--allow-unverified").code); }
            finally { Directory.Delete(o2, true); }
        } finally { Directory.Delete(d, true); if (Directory.Exists(o)) Directory.Delete(o, true); }
    }

    [Fact] public void Set_in_place_writes_only_SAVEDATA_unless_both()
    {
        var d = Slot(); try
        {
            var backupBefore = File.ReadAllBytes(Path.Combine(d, "BACKUP.BIN"));
            Assert.Equal(0, Run("set", d, "player.senki", "9999", "--in-place", "--allow-unverified").code);
            Assert.Equal("9999", Run("get", Path.Combine(d, "SAVEDATA.BIN"), "player.senki").outp.Trim());
            Assert.True(File.ReadAllBytes(Path.Combine(d, "BACKUP.BIN")).AsSpan().SequenceEqual(backupBefore)); Assert.Single(Directory.GetFiles(d, "*.bak-*"));
            Assert.Equal(0, Run("set", d, "player.senki", "8888", "--in-place", "--both", "--allow-unverified").code);
            Assert.Equal("8888", Run("get", Path.Combine(d, "BACKUP.BIN"), "player.senki").outp.Trim());
        } finally { Directory.Delete(d, true); }
    }

    [Fact] public void Verify_roundtrip_passes_for_all_samples_and_reports_exact_offsets_when_bytes_differ()
    {
        var (c, o, _) = Run("verify", Fixture.Samples, "--roundtrip"); Assert.Equal(0, c); Assert.Equal(Fixture.AllBins().Count(), o.Split("ROUNDTRIP OK").Length - 1);
        var d = Slot(); try
        {
            var p = Path.Combine(d, "SAVEDATA.BIN"); var raw = File.ReadAllBytes(p); raw[0x20] ^= 1; File.WriteAllBytes(p, raw); // header checksum no longer matches
            var (c2, o2, _) = Run("verify", p, "--roundtrip"); Assert.Equal(1, c2); Assert.Contains("round trip differs", o2); Assert.Contains("0x70", o2); Assert.Contains("(header)", o2);
        } finally { Directory.Delete(d, true); }
        Assert.Equal([(1, 2)], Program.DiffRanges([1, 2, 3, 4], [1, 9, 8, 4]).Select(x => (x.start, x.len)).ToList());
        Assert.Equal([(2, 3)], Program.DiffRanges([1, 2], [1, 2, 7, 7, 7]).Select(x => (x.start, x.len)).ToList());
    }

    [Fact] public void Missing_or_invalid_config_prints_how_to_fix_it()
    {
        var (c, e) = (0, ""); var o = new StringWriter(); var er = new StringWriter();
        c = Program.Run(["info", Fixture.Mine("SAVEDATA00"), "--config", "/nope/local_config.json"], o, er); e = er.ToString(); Assert.Equal(2, c); Assert.Contains("tools/keys.py", e);
        var bad = Path.GetTempFileName(); try { File.WriteAllText(bad, "{ broken"); er = new StringWriter(); c = Program.Run(["info", Fixture.Mine("SAVEDATA00"), "--config", bad], o, er); Assert.Equal(2, c); Assert.Contains("not a valid local_config.json", er.ToString()); } finally { File.Delete(bad); }
    }

    [Fact] public void Dump_raw_then_pack_roundtrips_exactly()
    {
        var d = Slot(); try
        {
            var src = Path.Combine(d, "SAVEDATA.BIN"); var dec = Path.Combine(d, "x.dec"); var outp = Path.Combine(d, "packed.BIN");
            Assert.Equal(0, Run("dump", src, "--raw", dec).code); Assert.Equal(0, Run("pack", dec, outp).code);
            Assert.True(File.ReadAllBytes(outp).AsSpan().SequenceEqual(File.ReadAllBytes(src)));
        } finally { Directory.Delete(d, true); }
    }

    [Fact] public void Dump_json_is_valid_json_and_get_path_works()
    {
        var d = Slot(); try
        {
            var src = Path.Combine(d, "SAVEDATA.BIN"); var j = Path.Combine(d, "x.json");
            Assert.Equal(0, Run("dump", src, "--json", j).code); using (System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(j))) { }
            Assert.Equal("9030", Run("get", src, "--path", "/MissionData/main_mission_progress").outp.Trim());
        } finally { Directory.Delete(d, true); }
    }

    [Fact] public void Unknown_command_missing_file_and_missing_config_give_clear_errors()
    {
        Assert.Equal(2, Run("bogus").code);
        var (c, _, e) = Run("info", "/nonexistent/SAVEDATA.BIN"); Assert.Equal(2, c); Assert.Contains("error:", e);
        var o = new StringWriter(); var er = new StringWriter();
        Assert.Equal(2, Program.Run(["info", Fixture.Mine("SAVEDATA00"), "--config", "/nope/local_config.json"], o, er)); Assert.Contains("local_config", er.ToString());
        Assert.Equal(2, Program.Run([], o, er));
    }
}

public class CliDiffItemsTests
{
    static (int code, string outp, string err) Run(params string[] a)
    {
        var o = new StringWriter(); var e = new StringWriter();
        var code = Program.Run([.. a, "--config", Path.Combine(Fixture.Root, "local_config.json"), "--fields", Path.Combine(Fixture.Root, "docs", "fields.json"), "--items", Path.Combine(Fixture.Root, "docs", "items.json")], o, e); return (code, o.ToString(), e.ToString());
    }
    static string Slot() => Path.GetDirectoryName(Fixture.Mine("SAVEDATA00"))!;

    [Fact] public void Diff_of_save_vs_backup_lists_mapped_ids_unmapped_summary_and_tail()
    {
        var (c, o, e) = Run("diff", Fixture.Mine("SAVEDATA00", "BACKUP.BIN"), Fixture.Mine("SAVEDATA00"), "--max", "2"); Assert.True(c == 0, e);
        Assert.Contains("[hdr.checkpoint]", o); Assert.Contains("[mission.progress]", o); Assert.Contains("9015 -> 9030", o); Assert.Contains("[unmapped]", o);
        Assert.Contains("more unmapped", o); Assert.Contains("by area:", o); Assert.Contains("binary tail:", o); Assert.Contains("derived", o);
    }

    [Fact] public void Diff_of_slot_dirs_compares_both_files_and_says_identical_when_equal()
    {
        var (c, o, _) = Run("diff", Slot(), Slot()); Assert.Equal(0, c); Assert.Equal(2, o.Split("IDENTICAL").Length - 1); Assert.Contains("SAVEDATA.BIN", o); Assert.Contains("BACKUP.BIN", o);
    }

    [Fact] public void Diff_after_an_edit_shows_only_the_edited_field()
    {
        var d = Fixture.TempSlotFrom(Slot()); var o = Path.Combine(Path.GetTempPath(), "ff2r-dout-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, Run("set", d, "player.senki", "123", "--out", o, "--allow-unverified").code);
            var (c, so, _) = Run("diff", Path.Combine(d, "SAVEDATA.BIN"), Path.Combine(o, "SAVEDATA.BIN")); Assert.Equal(0, c);
            Assert.Contains("1 changed value(s): 1 mapped", so); Assert.Contains("5865 -> 123   [player.senki]", so); Assert.DoesNotContain("[player.sen]", so); Assert.Contains("0 byte(s) changed", so);
        } finally { Directory.Delete(d, true); if (Directory.Exists(o)) Directory.Delete(o, true); }
    }

    [Fact] public void Diff_rejects_a_dir_against_a_file_and_missing_files()
    {
        Assert.Equal(2, Run("diff", Slot(), Fixture.Mine("SAVEDATA00")).code); Assert.Equal(2, Run("diff", "/nonexistent/a.BIN", Fixture.Mine("SAVEDATA00")).code); Assert.Equal(2, Run("diff", Fixture.Mine("SAVEDATA00")).code);
    }

    [Fact] public void Items_lists_keys_and_counts_and_shows_names_only_when_known()
    {
        var (c, o, _) = Run("items", Slot()); Assert.Equal(0, c);
        Assert.Contains("185346688", o); Assert.Contains("0x0B0C2A80", o); Assert.Contains("unknown (0x0B0C2A80)", o); Assert.Contains("166 row(s); 9 with known names (11 names in items.json)", o);
        Assert.Contains("Herbal Medicine [likely]", o); Assert.Contains("unknown (0x65E72A7F) - unnamed, treated specially in cheat table", o); Assert.Contains("third-party and uncontrolled", o);
        var tmp = Path.GetTempFileName(); try
        {
            File.WriteAllText(tmp, "{\"items\":[{\"key\":185346688,\"name\":\"Test item\",\"confidence\":\"guessed\"}]}");
            var o2 = new StringWriter(); Assert.Equal(0, Program.Run(["items", Slot(), "--items", tmp, "--config", Path.Combine(Fixture.Root, "local_config.json")], o2, new StringWriter()));
            Assert.Contains("Test item [guessed]", o2.ToString()); Assert.Contains("1 with known names", o2.ToString());
        } finally { File.Delete(tmp); }
        Assert.Contains("1200 row(s)", Run("items", Slot(), "--all").outp);
    }
}


public class CliSystemTests
{
    static (int code, string outp, string err) Run(params string[] a)
    {
        var o = new StringWriter(); var e = new StringWriter();
        var code = Program.Run([.. a, "--config", Path.Combine(Fixture.Root, "local_config.json"), "--fields", Path.Combine(Fixture.Root, "docs", "fields.json"), "--system-fields", Fixture.SystemFields], o, e); return (code, o.ToString(), e.ToString());
    }

    [Fact] public void Info_get_diff_work_on_system_saves_and_set_refuses()
    {
        var (full, empty) = Fixture.SystemExtremes();
        var (c, o, _) = Run("info", full); Assert.Equal(0, c); Assert.Contains("READ-ONLY", o); Assert.Contains("section SystemData", o); Assert.Contains("sys.record.ending_cleared_flag", o); Assert.Contains("5 of 5 non-empty", o);
        Assert.Contains("0 of 5 non-empty", Run("info", empty).outp);
        var g = Run("get", full, "sys.record.ending_cleared_flag"); Assert.Equal(0, g.code); Assert.Equal(5, g.outp.Trim().Split('\n').Length); Assert.DoesNotContain("false", g.outp);
        Assert.Equal("180438954", Run("get", full, "sys.record.shop_point").outp.Trim());
        var (cs, _, e) = Run("set", full, "sys.record.shop_point", "5", "--out", Path.Combine(Path.GetTempPath(), "ff2r-nope"), "--allow-unverified"); Assert.Equal(2, cs); Assert.Contains("read-only", e); Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "ff2r-nope", "SAVEDATA.BIN")));
        var (cd, od, _) = Run("diff", empty, full); Assert.Equal(0, cd); Assert.Contains("changes per known field:", od); Assert.Contains("[sys.record.ghost_list_unlock_data] 375 change(s)", od);
        var all = Run("diff", empty, full, "--max", "100000").outp; Assert.Contains("ending_cleared_flag_/0  false -> true   [sys.record.ending_cleared_flag]", all); Assert.Contains("[sys.record.shop_point]", all);
        Assert.Contains("sys.record.shop_point", Run("fields").outp); Assert.Contains("system", Run("fields").outp);
    }
}

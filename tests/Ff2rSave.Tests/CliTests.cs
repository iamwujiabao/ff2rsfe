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
        Assert.Contains("integrity   OK", o); Assert.Contains("1000000000", o); Assert.Contains("mission.progress", o);
    }

    [Fact] public void Verify_ok_for_all_samples_and_fails_for_corrupt()
    {
        var (c, o, _) = Run("verify", Fixture.Samples); Assert.Equal(0, c); Assert.Contains("14/14 OK", o);
        var d = Slot(); try
        {
            var p = Path.Combine(d, "SAVEDATA.BIN"); var raw = File.ReadAllBytes(p); raw[0x21] ^= 1; File.WriteAllBytes(p, raw);
            var (c2, o2, _) = Run("verify", p); Assert.Equal(1, c2); Assert.Contains("FAIL", o2);
        } finally { Directory.Delete(d, true); }
    }

    [Fact] public void Get_set_on_slot_writes_both_files_with_backups()
    {
        var d = Slot(); try
        {
            Assert.Equal("5865", Run("get", Path.Combine(d, "SAVEDATA.BIN"), "player.senki").outp.Trim());
            var (c, o, e) = Run("set", d, "player.senki", "9999"); Assert.True(c == 0, e + o);
            Assert.Equal("9999", Run("get", Path.Combine(d, "BACKUP.BIN"), "player.senki").outp.Trim());
            Assert.Equal(2, Directory.GetFiles(d, "*.bak-*").Length);
            Assert.Equal(0, Run("verify", d).code);
        } finally { Directory.Delete(d, true); }
    }

    [Fact] public void Set_rejects_bad_value_and_collection_without_path_and_leaves_file_untouched()
    {
        var d = Slot(); try
        {
            var p = Path.Combine(d, "SAVEDATA.BIN"); var before = File.ReadAllBytes(p);
            Assert.Equal(2, Run("set", p, "player.senki", "-3").code);
            Assert.Equal(2, Run("set", p, "player.senki", "abc").code);
            Assert.Equal(2, Run("set", p, "item.num", "5").code);
            Assert.Equal(2, Run("set", p, "hdr.chapter", "5").code); // read-only
            Assert.True(File.ReadAllBytes(p).AsSpan().SequenceEqual(before)); Assert.Empty(Directory.GetFiles(d, "*.bak-*"));
        } finally { Directory.Delete(d, true); }
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

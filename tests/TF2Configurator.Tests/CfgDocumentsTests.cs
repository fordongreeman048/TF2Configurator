using TF2Configurator.Models;
using Xunit;

namespace TF2Configurator.Tests;

/// <summary>
/// The round-trip and surgical-edit guarantees that SmokeTest checks on a live machine,
/// ported to unit tests so they run on every build. A cfg's own conventions — line
/// endings, final newline, comments, blank lines — must survive untouched edits.
/// </summary>
public sealed class CfgDocumentsTests
{
    // ---- Round-trip: parse then render with no edits is the identity function -----------

    [Theory]
    [InlineData("shadows=off\r\neffects=low\r\n")]
    [InlineData("shadows=off\r\neffects=low")]
    [InlineData("shadows=off\neffects=low\n")]
    [InlineData("shadows=off\neffects=low")]
    [InlineData("shadows=off\reffects=low\r")]
    [InlineData("// header\r\n\r\nshadows=off\r\n\r\n// tail\r\n")]
    [InlineData("shadows=off // was high\r\n")]
    [InlineData("  shadows   =   off  \r\n")]
    [InlineData("shadows=off\r\nshadows=high\r\n")]
    [InlineData("shadows=off\r\n\r\n\r\n\r\neffects=low\r\n")]
    [InlineData("")]
    [InlineData("\r\n")]
    public void KeyValueCfg_parse_render_is_identity(string text) =>
        Assert.Equal(text, KeyValueCfg.Parse(text).Render());

    [Theory]
    [InlineData("cl_interp 0.0152\r\n")]
    [InlineData("bind \"f\" \"+attack\"\r\nalias foo \"bar baz\"\r\n")]
    [InlineData("name \"Some Player\"\r\n")]
    [InlineData("// header\r\n\r\ncl_interp 0.0152\r\n")]
    [InlineData("cl_interp 0.0152 // tweaked\r\n")]
    [InlineData("")]
    [InlineData("\n")]
    public void CommandCfg_parse_render_is_identity(string text) =>
        Assert.Equal(text, CommandCfg.Parse(text).Render());

    // ---- Surgical edits: changing one value touches exactly what it should --------------

    [Fact]
    public void KeyValueCfg_lf_stays_lf_when_a_value_changes()
    {
        var doc = KeyValueCfg.Parse("shadows=off\neffects=low\n");
        doc.Set("shadows", "high");
        Assert.Equal("shadows=high\neffects=low\n", doc.Render());
    }

    [Fact]
    public void KeyValueCfg_no_final_newline_is_not_invented()
    {
        var doc = KeyValueCfg.Parse("shadows=off\neffects=low");
        doc.Set("effects", "high");
        Assert.Equal("shadows=off\neffects=high", doc.Render());
    }

    [Fact]
    public void KeyValueCfg_appended_line_uses_the_files_own_terminator()
    {
        var doc = KeyValueCfg.Parse("shadows=off\neffects=low");
        doc.Set("water", "low");
        Assert.Equal("shadows=off\neffects=low\nwater=low", doc.Render());
    }

    [Fact]
    public void KeyValueCfg_trailing_comment_survives_a_value_change()
    {
        var doc = KeyValueCfg.Parse("shadows=off // keep me\r\n");
        doc.Set("shadows", "high");
        Assert.Equal("shadows=high // keep me\r\n", doc.Render());
    }

    [Fact]
    public void KeyValueCfg_new_file_gets_a_sane_final_newline()
    {
        var doc = KeyValueCfg.Parse("");
        doc.Set("shadows", "off");
        Assert.Equal("shadows=off\r\n", doc.Render());
    }

    // ---- Command cfg specifics ----------------------------------------------------------

    [Fact]
    public void CommandCfg_bind_and_alias_are_not_cvars()
    {
        var cmd = CommandCfg.Parse("bind \"f\" \"+attack\"\r\nalias foo \"bar baz\"\r\n");
        Assert.Null(cmd.Get("bind"));
        Assert.Null(cmd.Get("alias"));
    }

    [Fact]
    public void CommandCfg_quoted_value_is_unquoted_on_read_and_requoted_on_write()
    {
        var cmd = CommandCfg.Parse("name \"Some Player\"\r\n");
        Assert.Equal("Some Player", cmd.Get("name"));

        cmd.Set("name", "Other Player");
        Assert.Contains("name \"Other Player\"", cmd.Render());
    }

    [Fact]
    public void CommandCfg_known_lines_survive_an_unrelated_edit()
    {
        var cmd = CommandCfg.Parse("bind \"f\" \"+attack\"\r\nalias foo \"bar baz\"\r\ncl_interp 0.0152\r\n");
        cmd.Set("cl_interp", "0.02");
        var text = cmd.Render();
        Assert.Contains("bind \"f\" \"+attack\"", text);
        Assert.Contains("alias foo \"bar baz\"", text);
    }

    // ---- Duplicate keys: last value wins (what the engine actually does) ---------------

    [Fact]
    public void KeyValueCfg_duplicate_reads_the_effective_last_value()
    {
        var doc = KeyValueCfg.Parse("shadows=off\r\nshadows=high\r\n");
        Assert.Equal("high", doc.Get("shadows"));
    }

    [Fact]
    public void KeyValueCfg_set_edits_the_effective_last_duplicate()
    {
        var doc = KeyValueCfg.Parse("shadows=off\r\nshadows=high\r\n");
        doc.Set("shadows", "medium");
        Assert.Equal("shadows=off\r\nshadows=medium\r\n", doc.Render());
    }

    [Fact]
    public void KeyValueCfg_remove_drops_every_duplicate()
    {
        var doc = KeyValueCfg.Parse("shadows=off\r\nshadows=high\r\n");
        Assert.True(doc.Remove("shadows"));
        Assert.False(doc.Has("shadows"));
        Assert.Equal("", doc.Render());
    }

    [Fact]
    public void CommandCfg_duplicate_reads_and_edits_the_last_value()
    {
        var doc = CommandCfg.Parse("cl_interp 0.0152\r\ncl_interp 0.03\r\n");
        Assert.Equal("0.03", doc.Get("cl_interp"));

        doc.Set("cl_interp", "0.02");
        Assert.Equal("cl_interp 0.0152\r\ncl_interp 0.02\r\n", doc.Render());
    }

    [Fact]
    public void CommandCfg_remove_drops_every_duplicate()
    {
        var doc = CommandCfg.Parse("cl_interp 0.0152\r\ncl_interp 0.03\r\n");
        Assert.True(doc.Remove("cl_interp"));
        Assert.False(doc.Has("cl_interp"));
    }

    // ---- // inside quotes is literal text, not a comment --------------------------------

    [Fact]
    public void CommandCfg_comment_marker_inside_quotes_is_literal()
    {
        var doc = CommandCfg.Parse("name \"Some // Player\"\r\n");
        Assert.Equal("Some // Player", doc.Get("name"));
        Assert.Equal("name \"Some // Player\"\r\n", doc.Render());
    }

    [Fact]
    public void KeyValueCfg_comment_marker_inside_quotes_is_literal()
    {
        var doc = KeyValueCfg.Parse("download=\"a // b\"\r\n");
        Assert.Equal("\"a // b\"", doc.Get("download"));
        Assert.Equal("download=\"a // b\"\r\n", doc.Render());
    }

    [Fact]
    public void KeyValueCfg_real_trailing_comment_still_works()
    {
        var doc = KeyValueCfg.Parse("shadows=off // was high\r\n");
        Assert.Equal("off", doc.Get("shadows"));
        Assert.Equal("// was high", doc.Lines[0].TrailingComment);
    }
}

using System.Text;
using PurgeCad.Core.Purging;
using PurgeCad.Engines.AutoCad;
using Xunit;

namespace PurgeCad.Core.Tests;

public class AutoCadScriptTests
{
    [Fact]
    public void Script_purges_three_passes_and_saves_in_the_original_format()
    {
        var options = new PurgeOptions();
        var script = AutoCadScript.Build(options, @"C:\work\out.dwg", AutoCadScript.SaveFormat("AC1027"), dxf: false);
        var lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Equal(4, lines.Count(l => l == "_.-PURGE _B * _N"));
        Assert.Equal(4, lines.Count(l => l == "_.-PURGE _SH * _N"));
        Assert.DoesNotContain(lines, l => l.StartsWith("_.-PURGE _A"));
        Assert.DoesNotContain(lines, l => l.StartsWith("_.-PURGE _Z"));
        Assert.Equal(3, lines.Count(l => l == "(pcad-dgn)"));
        Assert.Contains(lines, l => l == "(pcad-lf)");
        Assert.Contains(lines, l => l == "(pcad-scales)");
        Assert.Equal("_.SAVEAS 2013 \"C:\\work\\out.dwg\"", lines[^1]);
        Assert.DoesNotContain(lines, l => l.StartsWith("_.AUDIT"));
    }

    [Fact]
    public void Script_only_contains_chosen_categories()
    {
        var options = new PurgeOptions();
        options.Categories.Clear();
        options.Categories.Add(PurgeCategory.Layers);
        var script = AutoCadScript.Build(options, "out.dxf", "2010", dxf: true);

        Assert.DoesNotContain("_B ", script);
        Assert.Contains("_.-PURGE _LA * _N", script);
        Assert.Contains("_.SAVEAS _DXF _V 2010 16 \"out.dxf\"", script);
    }

    [Theory]
    [InlineData("R24.3", "AC1032")]
    [InlineData("R21.0", "AC1027")]
    [InlineData("R18.2", "AC1024")]
    public void Newest_format_per_release(string release, string expected) =>
        Assert.Equal(expected, AutoCadScript.NewestFormat(release));

    [Fact]
    public void Log_counts_are_read_per_category()
    {
        const string log = "Deleting layer \"A\".\r\nDeleting layer \"B\".\r\n2 layers deleted.\r\n1 block deleted.\r\n" +
                           "3 blocks deleted.\r\nNo unreferenced linetypes found.\r\n1 text style deleted.\r\n";
        var removed = AutoCadScript.ParseRemoved(log, out var understood);

        Assert.True(understood);
        Assert.Equal(2, removed[PurgeCategory.Layers]);
        Assert.Equal(4, removed[PurgeCategory.Blocks]);
        Assert.Equal(1, removed[PurgeCategory.TextStyles]);
    }

    [Fact]
    public void Log_in_another_language_is_not_read_as_nothing_purged()
    {
        AutoCadScript.ParseRemoved("2 calques supprimés.", out var understood);
        Assert.False(understood);
    }

    [Fact]
    public void Utf16_log_is_decoded() =>
        Assert.Equal("1 block deleted.", AutoCadScript.DecodeLog(Encoding.Unicode.GetBytes("1 block deleted.")));

    [Fact]
    public void Dxf_version_is_read_from_the_header()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "  0\nSECTION\n  2\nHEADER\n  9\n$ACADVER\n  1\nAC1024\n  9\n$INSBASE\n");
        try { Assert.Equal("AC1024", AutoCadScript.ReadDxfVersion(path)); }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("Unable to initialize adlm.\nError Code: <-104>", true)]
    [InlineData("", true)]
    [InlineData("Redirect stdout\nAutoCAD Core Engine Console\nCommand: _.-PURGE\n3 layers deleted.", false)]
    public void Detects_console_that_did_not_start(string log, bool expected) =>
        Assert.Equal(expected, AutoCadScript.IsLicensingFailure(log));

    [Fact]
    public void Never_sends_options_older_releases_do_not_know()
    {
        var options = new PurgeOptions();
        foreach (var c in Enum.GetValues<PurgeCategory>()) options.Categories.Add(c);
        var script = AutoCadScript.Build(options, "out.dwg", "2010", dxf: false, releaseMajor: 18);
        Assert.DoesNotContain(" _O", script);
        Assert.DoesNotContain(" _DE ", script);
        Assert.DoesNotContain(" _SE ", script);
        Assert.Contains("_.-PURGE _B * _N", script);
        Assert.Equal(19, AutoCadScript.Major("R19.1"));
    }

    [Fact]
    public void Lisp_markers_are_counted()
    {
        const string log = "(pcad-say \"dgn\" pcad-n)\nPURGECAD dgn 48210\nPURGECAD layerfilters 7\nPURGECAD scales 0\n";
        var removed = AutoCadScript.ParseRemoved(log, out var understood);
        Assert.True(understood);
        Assert.Equal(48210, removed[PurgeCategory.DgnLinetypeComponents]);
        Assert.Equal(7, removed[PurgeCategory.LayerFilters]);
    }

    [Fact]
    public void Lisp_lines_have_balanced_parentheses()
    {
        var script = AutoCadScript.Build(new PurgeOptions(), "out.dwg", "2013", dxf: false);
        foreach (var line in script.Split('\n').Where(l => l.StartsWith('(')))
        {
            var depth = 0; var inString = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '\\' && inString) { i++; continue; }
                if (c == '"') inString = !inString;
                else if (!inString && c == '(') depth++;
                else if (!inString && c == ')') depth--;
                Assert.True(depth >= 0, line);
            }
            Assert.Equal(0, depth);
        }
    }

    [Fact]
    public void Plugin_script_loads_the_plugin_runs_it_then_one_purge_pass_and_saves()
    {
        var script = AutoCadScript.Build(new PurgeOptions(), "out.dwg", "2004", dxf: false, releaseMajor: 19,
            resultPath: @"C:\work\counts.txt", pluginPath: @"C:\Program Files\PurgeCAD\acad-plugin\net462\PurgeCad.AutoCad.Plugin.dll");
        var lines = script.Split('\n', StringSplitOptions.TrimEntries);
        var netload = Array.IndexOf(lines, "_.NETLOAD");
        Assert.True(netload > 0);
        // SECURELOAD off just for the load, restored right after
        Assert.Contains("SECURELOAD\" 0", lines[netload - 1]);
        Assert.Equal("\"C:\\Program Files\\PurgeCAD\\acad-plugin\\net462\\PurgeCad.AutoCad.Plugin.dll\"", lines[netload + 1]);
        Assert.Contains("pcad-secureload)", lines[netload + 2]);
        Assert.Equal(AutoCadScript.PluginCommand, lines[netload + 3]);
        // one -PURGE pass after the plugin, no AutoLISP clean-ups, then the save
        Assert.Equal(1, lines.Count(l => l == "_.-PURGE _B * _N"));
        Assert.DoesNotContain("(pcad-dgn)", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("(defun"));
        Assert.True(Array.FindIndex(lines, l => l.StartsWith("_.SAVEAS")) > netload);
    }

    [Fact]
    public void Plugin_job_lists_counts_file_and_categories_by_name()
    {
        var options = new PurgeOptions { Categories = [PurgeCategory.Linetypes, PurgeCategory.DgnLinetypeComponents] };
        var job = AutoCadScript.BuildJob(options, @"C:\work\counts.txt");
        Assert.Contains(@"counts=C:\work\counts.txt", job);
        Assert.Contains("categories=Linetypes,DgnLinetypeComponents", job);
    }

    [Fact]
    public void Plugin_counts_are_read_by_category_name()
    {
        const string log = "PURGECAD plugin 1\nPURGECAD Blocks 12\nPURGECAD Linetypes 230\nPURGECAD DgnLinetypeComponents 4100\n"
                           + "PURGECAD passes 6\nPURGECAD errors 0\n  DGN linetype left: X (in use)\n";
        var removed = AutoCadScript.ParseRemoved(log, out var understood);
        Assert.True(understood);
        Assert.Equal(12, removed[PurgeCategory.Blocks]);
        Assert.Equal(230, removed[PurgeCategory.Linetypes]);
        Assert.Equal(4100, removed[PurgeCategory.DgnLinetypeComponents]);
        Assert.Equal(3, removed.Count);
    }

    [Theory]
    [InlineData(18, null)]
    [InlineData(19, "net462")]
    [InlineData(24, "net462")]
    [InlineData(25, "net8.0-windows")]
    [InlineData(26, "net8.0-windows")]
    public void Plugin_build_follows_the_AutoCAD_runtime(int major, string? folder) =>
        Assert.Equal(folder, AutoCadPlugin.FolderFor(major));
}

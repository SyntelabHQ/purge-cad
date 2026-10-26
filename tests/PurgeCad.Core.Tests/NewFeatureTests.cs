using PurgeCad.Core.News;
using ACadSharp;
using ACadSharp.IO;
using ACadSharp.Tables;
using PurgeCad.Core.Batch;
using PurgeCad.Core.Purging;
using PurgeCad.Core.Scanning;
using PurgeCad.Core.Settings;
using PurgeCad.Engines.ACadSharp;
using PurgeCad.Engines.AutoCad;
using Xunit;

namespace PurgeCad.Core.Tests;

public class FallbackTests
{
    sealed class FakeEngine(string id, RiskLevel risk, Func<FilePurgeResult> result) : IPurgeEngine
    {
        public int Calls;
        public string Id => id;
        public string NameKey => $"engine.{id}.name";
        public RiskLevel Risk => risk;
        public IReadOnlyList<string> RiskKeys => [];
        public IReadOnlySet<PurgeCategory> SupportedCategories => new HashSet<PurgeCategory>();
        public int MaxParallelism => 1;
        public EngineAvailability CheckAvailability() => EngineAvailability.Available;
        public Task<FilePurgeResult> PurgeAsync(FilePurgeRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    static readonly FilePurgeRequest Request = new("in.dwg", "out.dwg", false, new PurgeOptions());

    [Fact]
    public async Task Second_engine_takes_the_files_the_first_cannot_open()
    {
        var first = new FakeEngine("autocad", RiskLevel.VeryLow, () => FilePurgeResult.Fail("engine.autocad.fail.cannotOpen"));
        var second = new FakeEngine("acadsharp", RiskLevel.Medium, () => new FilePurgeResult { Status = FileStatus.Purged });
        var result = await new FallbackEngine(first, second).PurgeAsync(Request, default);

        Assert.Equal(FileStatus.Purged, result.Status);
        Assert.Equal("acadsharp", result.EngineId);
        Assert.Equal("autocad", result.FallbackFrom);
    }

    [Theory]
    [InlineData(FileStatus.Purged, null)]
    [InlineData(FileStatus.NothingToPurge, null)]
    [InlineData(FileStatus.Skipped, "skip.locked")]
    [InlineData(FileStatus.Failed, "fail.unexpected")]
    public async Task No_fallback_for_results_the_second_engine_cannot_improve(FileStatus status, string? reason)
    {
        var first = new FakeEngine("autocad", RiskLevel.VeryLow, () => new FilePurgeResult { Status = status, ReasonKey = reason });
        var second = new FakeEngine("acadsharp", RiskLevel.Medium, () => new FilePurgeResult { Status = FileStatus.Purged });
        var result = await new FallbackEngine(first, second).PurgeAsync(Request, default);

        Assert.Equal(status, result.Status);
        Assert.Equal(0, second.Calls);
        Assert.Null(result.FallbackFrom);
    }

    [Fact]
    public async Task When_both_fail_the_first_reason_stays_with_the_second_in_the_detail()
    {
        var first = new FakeEngine("autocad", RiskLevel.VeryLow, () => new FilePurgeResult { Status = FileStatus.Skipped, ReasonKey = "skip.versionTooNew" });
        var second = new FakeEngine("acadsharp", RiskLevel.Medium, () => FilePurgeResult.Skip("skip.unknownObjects"));
        var result = await new FallbackEngine(first, second).PurgeAsync(Request, default);

        Assert.Equal("skip.versionTooNew", result.ReasonKey);
        Assert.Contains("acadsharp: skip.unknownObjects", result.Detail);
    }

    [Fact]
    public void Automatic_gets_a_fallback_when_two_engines_are_available()
    {
        var registry = new EngineRegistry()
            .Add(new FakeEngine("acadsharp", RiskLevel.Medium, () => new FilePurgeResult()))
            .Add(new FakeEngine("autocad", RiskLevel.VeryLow, () => new FilePurgeResult()));
        var engine = Assert.IsType<FallbackEngine>(registry.ResolveForRun(EngineRegistry.AutomaticId));
        Assert.Equal("autocad", engine.Primary.Id);
        Assert.Equal("acadsharp", engine.Backup.Id);
        Assert.IsNotType<FallbackEngine>(registry.ResolveForRun("acadsharp"));
    }
}

public class SaveVersionTests
{
    [Theory]
    [InlineData("2013", "AC1027")]
    [InlineData("AC1032", "AC1032")]
    [InlineData("ac1018", "AC1018")]
    [InlineData("2001", null)]
    public void Versions_parse_by_year_or_code(string text, string? code) => Assert.Equal(code, SaveVersions.Parse(text));

    [Fact]
    public void Converting_is_only_for_another_version()
    {
        var options = new PurgeOptions { SaveVersion = "AC1027" };
        Assert.True(options.Converts("AC1032"));
        Assert.False(options.Converts("AC1027"));
        Assert.False(new PurgeOptions().Converts("AC1032"));
        Assert.Equal("AC1027", options.TargetVersion("AC1032"));
    }

    [Fact]
    public void Convert_only_purges_nothing()
    {
        var options = PurgeOptions.ConvertOnly("AC1024", new PurgeOptions { AllowUnknownObjects = true });
        Assert.Empty(options.Categories);
        Assert.Equal("AC1024", options.SaveVersion);
        Assert.True(options.AllowUnknownObjects);
        Assert.True(options.NothingToDo("AC1024"));
        Assert.False(options.NothingToDo("AC1032"));
        Assert.False(new PurgeOptions().NothingToDo("AC1032"));
    }

    [Fact]
    public async Task Convert_converts_and_leaves_drawings_already_in_that_version()
    {
        var folder = Directory.CreateTempSubdirectory("pcad-conv-").FullName;
        var source = Path.Combine(folder, "in.dxf");
        DxfWriter.Write(source, new CadDocument(), false);
        try
        {
            var original = DxfReader.Read(source).Header.Version.ToString();
            await using var engine = new ACadSharpEngine();

            var same = await engine.PurgeAsync(new FilePurgeRequest(source, Path.Combine(folder, "same.dxf"), DryRun: false,
                PurgeOptions.ConvertOnly(original)), default);
            Assert.Equal(FileStatus.NothingToPurge, same.Status);
            Assert.False(File.Exists(Path.Combine(folder, "same.dxf")));

            var output = Path.Combine(folder, "out.dxf");
            var converted = await engine.PurgeAsync(new FilePurgeRequest(source, output, DryRun: false,
                PurgeOptions.ConvertOnly("AC1018")), default);
            Assert.Equal(FileStatus.Purged, converted.Status);
            Assert.Equal("AC1018", converted.SavedAs);
            Assert.Equal(0, converted.TotalRemoved);
            Assert.Equal(ACadVersion.AC1018, DxfReader.Read(output).Header.Version);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task ACadSharp_saves_in_the_asked_version_even_with_nothing_to_purge()
    {
        var folder = Directory.CreateTempSubdirectory("pcad-ver-").FullName;
        var source = Path.Combine(folder, "in.dxf");
        var output = Path.Combine(folder, "out.dxf");
        DxfWriter.Write(source, new CadDocument(), false);
        try
        {
            var options = new PurgeOptions { SaveVersion = "AC1015" };
            await using var engine = new ACadSharpEngine();
            var result = await engine.PurgeAsync(new FilePurgeRequest(source, output, DryRun: false, options), default);

            Assert.Equal(FileStatus.Purged, result.Status);
            Assert.Equal("AC1015", result.SavedAs);
            Assert.Equal(ACadVersion.AC1015, DxfReader.Read(output).Header.Version);
        }
        finally
        {
            File.Delete(EngineLogs.PathFor(source, ".acadsharp.log"));
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ACadSharp_skips_DWG_2007_as_a_target()
    {
        var folder = Directory.CreateTempSubdirectory("pcad-ver-").FullName;
        var source = Path.Combine(folder, "in.dwg");
        DwgWriter.Write(source, new CadDocument(ACadVersion.AC1018));
        try
        {
            await using var engine = new ACadSharpEngine();
            var result = await engine.PurgeAsync(new FilePurgeRequest(source, Path.Combine(folder, "out.dwg"), false,
                new PurgeOptions { SaveVersion = "AC1021" }), default);
            Assert.Equal(FileStatus.Skipped, result.Status);
            Assert.Equal("engine.acadsharp.saveVersion2007", result.ReasonKey);
        }
        finally
        {
            File.Delete(EngineLogs.PathFor(source, ".acadsharp.log"));
            Directory.Delete(folder, recursive: true);
        }
    }
}

public class OptionsAndScanTests
{
    [Fact]
    public void Missing_references_are_off_by_default_and_reach_the_plugin_job()
    {
        var options = new PurgeOptions();
        Assert.False(options.Includes(PurgeCategory.MissingReferences));
        options.Categories.Add(PurgeCategory.MissingReferences);
        var job = AutoCadScript.BuildJob(options, @"C:\w\counts.txt", @"D:\P\plan.dwg");
        Assert.Contains("MissingReferences", job);
        Assert.Contains(@"source=D:\P\plan.dwg", job);
        Assert.Contains(PurgeCategory.MissingReferences, AutoCadScript.Supported);
        Assert.Equal(2, AutoCadScript.ParseRemoved("PURGECAD MissingReferences 2", out _)[PurgeCategory.MissingReferences]);
    }

    [Fact]
    public void Recent_folders_keep_the_newest_first_without_duplicates()
    {
        var s = new AppSettings();
        for (var i = 0; i < 10; i++) s.RememberFolder($"/p/{i}");
        s.RememberFolder("/p/5/");
        Assert.Equal(AppSettings.MaxRecentFolders, s.RecentFolders.Count);
        Assert.Equal("/p/5", s.RecentFolders[0]);
        Assert.Single(s.RecentFolders, f => f == "/p/5");
    }

    [Fact]
    public void A_profile_restores_the_options_it_saved()
    {
        var s = new AppSettings();
        s.Batch.EngineId = "acadsharp";
        s.Batch.Mode = OutputMode.ReportOnly;
        s.Batch.Purge.SaveVersion = "AC1027";
        s.Batch.Purge.Categories.Remove(PurgeCategory.Layers);
        s.SaveProfile("Arhivă");

        s.Batch.EngineId = "auto";
        s.Batch.Mode = OutputMode.CopyToFolder;
        s.Batch.Purge = new PurgeOptions();
        s.FindProfile("arhivă")!.ApplyTo(s);

        Assert.Equal("acadsharp", s.Batch.EngineId);
        Assert.Equal(OutputMode.ReportOnly, s.Batch.Mode);
        Assert.Equal("AC1027", s.Batch.Purge.SaveVersion);
        Assert.False(s.Batch.Purge.Includes(PurgeCategory.Layers));

        s.SaveProfile("ARHIVĂ");
        Assert.Single(s.Profiles);
    }

    [Fact]
    public void Chosen_drawings_are_listed_relative_to_the_folder_they_share()
    {
        var root = Directory.CreateTempSubdirectory("pcad-files-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "a"));
            Directory.CreateDirectory(Path.Combine(root, "b"));
            var one = Path.Combine(root, "a", "one.dwg");
            var two = Path.Combine(root, "b", "two.dxf");
            var other = Path.Combine(root, "a", "notes.txt");
            foreach (var f in new[] { one, two, other }) File.WriteAllText(f, "x");
            File.WriteAllText(Path.Combine(root, "a", "one.bak"), "x");
            File.WriteAllText(Path.Combine(root, "a", "skip.dwg"), "x");

            var scan = FolderScanner.FromFiles([one, two, other, one]);
            Assert.Equal(root, scan.Root);
            Assert.Equal([Path.Combine("a", "one.dwg"), Path.Combine("b", "two.dxf")], scan.Drawings.Select(d => d.RelativePath));
            Assert.Single(scan.Auxiliary);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void News_are_filtered_by_version_language_and_date_newest_first()
    {
        const string json = """
            { "messages": [
              { "id": "a", "date": "2026-10-01", "title": { "en": "Hello", "ro": "Salut" }, "text": "Text", "link": "https://syntelab.com" },
              { "id": "b", "date": "2026-10-20", "title": "Only 1.0", "maxVersion": "1.0.9" },
              { "id": "c", "date": "2026-10-21", "title": "Old", "until": "2026-10-02" },
              { "id": "d", "date": "2026-10-22", "title": "German", "languages": ["de"] },
              { "id": "e", "date": "2026-10-23", "title": "Warn", "level": "warning", "minVersion": "1.1" },
              { "title": "no id" }
            ] }
            """;
        var ro = NewsFeed.Parse(json, "ro", new Version(1, 1, 0), new DateOnly(2026, 10, 26));
        Assert.Equal(["e", "a"], ro.Select(n => n.Id));
        Assert.True(ro[0].IsWarning);
        Assert.Equal("Salut", ro[1].Title);
        Assert.Equal("Text", ro[1].Text);
        Assert.Equal("Hello", NewsFeed.Parse(json, "fr", new Version(1, 1, 0), new DateOnly(2026, 10, 26))[1].Title);
        Assert.Empty(NewsFeed.Parse("{}", "en", new Version(1, 1, 0), new DateOnly(2026, 10, 26)));
    }

    [Fact]
    public void Release_notes_skip_drafts_and_become_plain_text()
    {
        const string json = """
            [ { "tag_name": "v1.1.0", "published_at": "2026-10-27T10:00:00Z", "body": "## New\n* **Convert** tab\n- `--convert`", "html_url": "https://x/1.1.0" },
              { "tag_name": "v1.2.0", "draft": true, "body": "x" } ]
            """;
        var notes = NewsFeed.ParseReleases(json);
        Assert.Single(notes);
        Assert.Equal(new DateOnly(2026, 10, 27), notes[0].Date);
        Assert.Equal("New\n• Convert tab\n• --convert", notes[0].Notes);
    }

    [Fact]
    public void Engine_logs_are_found_only_when_written_in_this_run()
    {
        var source = Path.Combine(Path.GetTempPath(), $"pcad-log-{Guid.NewGuid():N}.dwg");
        var log = EngineLogs.ForEngine(source, "acadsharp");
        Assert.EndsWith(".acadsharp.log", log);
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        File.WriteAllText(log, "x");
        try
        {
            Assert.Equal(log, EngineLogs.Written(source, "acadsharp", DateTime.UtcNow.AddMinutes(-1)));
            Assert.Null(EngineLogs.Written(source, "acadsharp", DateTime.UtcNow.AddMinutes(1)));
            Assert.Null(EngineLogs.Written(source, "autocad", DateTime.UtcNow.AddMinutes(-1)));
        }
        finally { File.Delete(log); }
    }
}

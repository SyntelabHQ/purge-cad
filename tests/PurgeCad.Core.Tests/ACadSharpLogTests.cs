using ACadSharp;
using ACadSharp.IO;
using ACadSharp.Tables;
using PurgeCad.Core.Purging;
using PurgeCad.Engines.ACadSharp;
using Xunit;

namespace PurgeCad.Core.Tests;

public class ACadSharpLogTests
{
    [Fact]
    public async Task Purge_writes_a_log_with_what_was_removed()
    {
        var folder = Directory.CreateTempSubdirectory("pcad-log-").FullName;
        var source = Path.Combine(folder, $"log-test-{Guid.NewGuid():N}.dxf");
        var doc = new CadDocument();
        doc.Layers.Add(new Layer("NotUsed"));
        DxfWriter.Write(source, doc, false);

        var log = EngineLogs.PathFor(source, ".acadsharp.log");
        try
        {
            var options = new PurgeOptions();
            await using var engine = new ACadSharpEngine();
            var result = await engine.PurgeAsync(new FilePurgeRequest(source, Path.Combine(folder, "out.dxf"), DryRun: true, options), default);

            Assert.Equal(FileStatus.Purged, result.Status);
            Assert.True(File.Exists(log));
            var text = File.ReadAllText(log);
            Assert.Contains("engine: ACadSharp · status: Purged", text);
            Assert.Contains("Layer: NotUsed", text);
        }
        finally
        {
            File.Delete(log);
            Directory.Delete(folder, recursive: true);
        }
    }
}

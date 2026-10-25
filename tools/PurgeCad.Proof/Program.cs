using System.Diagnostics;
using System.Text;
using ACadSharp.IO;
using PurgeCad.Core.Purging;
using PurgeCad.Engines.ACadSharp;

// Runs the ACadSharp engine on every DWG / DXF of a folder, writes the purged copies to the output folder,
// and a report (report.md + report.csv) with what was removed, timings, reader notices and the verification.
// Originals are only read.

if (args.Length < 2)
{
    Console.WriteLine("Usage: PurgeCad.Proof <input folder> <output folder>");
    return 1;
}

var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);

var files = Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories)
    .Where(f => f.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
    .Where(f => !f.StartsWith(output, StringComparison.OrdinalIgnoreCase))
    .OrderBy(f => f)
    .ToList();

var engine = new ACadSharpEngine();
var options = new PurgeOptions { AllowUnknownObjects = true }; // proof: process everything, report unknowns
var md = new StringBuilder();
var csv = new StringBuilder("file,version,size_before,size_after,status,reason,detail,removed,passes,unknown_entities,reader_notices,not_written,roundtrip_ok,read_ms,purge_ms,write_ms,total_ms\n");

md.AppendLine("# ACadSharp proof report");
md.AppendLine();
md.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm} · {files.Count} files · ACadSharp {typeof(ACadSharp.CadDocument).Assembly.GetName().Version}");
md.AppendLine();

foreach (var file in files)
{
    var relative = Path.GetRelativePath(input, file);
    var target = Path.Combine(output, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    Console.Write($"{relative} … ");
    var watch = Stopwatch.StartNew();

    // A. Round trip without purging: does ACadSharp alone keep everything?
    var roundtrip = RoundTrip(file, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + Path.GetExtension(file)));

    // B. Purge with the engine (writes to target, verifies by reading it back)
    var d = engine.PurgeWithDiagnostics(new FilePurgeRequest(file, target, DryRun: false, options));
    var r = d.Result!;
    var sizeBefore = new FileInfo(file).Length;
    long? sizeAfter = File.Exists(target) ? new FileInfo(target).Length : null;
    Console.WriteLine($"{r.Status} ({r.TotalRemoved} removed, {watch.Elapsed.TotalSeconds:0.0} s)");

    md.AppendLine($"## {relative}");
    md.AppendLine();
    md.AppendLine($"- Version: {d.Version} · size {sizeBefore:N0} → {(sizeAfter?.ToString("N0") ?? "—")} bytes");
    md.AppendLine($"- Status: **{r.Status}** {r.ReasonKey} {r.Detail}");
    md.AppendLine($"- Removed: {r.TotalRemoved} in {d.Passes} passes ({string.Join(", ", r.Removed.Select(kv => $"{kv.Key} {kv.Value}"))})");
    md.AppendLine($"- Unknown entities: {d.UnknownEntities} · reader notices (not supported / errors): {d.UnsupportedNotices}");
    md.AppendLine($"- Round trip without purge: {roundtrip}");
    md.AppendLine($"- Time: read {d.ReadMs} ms · purge {d.PurgeMs} ms · write {d.WriteMs} ms · total {d.TotalMs} ms");
    if (d.Before is not null && d.Actual is not null)
        md.AppendLine($"- Entities: {d.Before.TotalEntities:N0} before, {d.Actual.TotalEntities:N0} after (expected {d.Expected!.TotalEntities:N0})");
    if (d.Removed.Count > 0)
    {
        md.AppendLine("- Removed items:");
        foreach (var item in d.Removed.Take(200)) md.AppendLine($"  - {item}");
        if (d.Removed.Count > 200) md.AppendLine($"  - … and {d.Removed.Count - 200} more");
    }
    if (d.NotWritten.Count > 0)
    {
        md.AppendLine("- **Not written by ACadSharp (lost in the output):**");
        foreach (var kv in d.NotWritten.OrderByDescending(kv => kv.Value)) md.AppendLine($"  - {kv.Key} ×{kv.Value}");
    }
    else if (r.Status == FileStatus.Purged)
    {
        md.AppendLine("- Not written by ACadSharp: nothing");
    }
    var notices = d.ReadNotifications.Where(n => n.Type != NotificationType.None).GroupBy(n => (n.Type, n.Message)).ToList();
    if (notices.Count > 0)
    {
        md.AppendLine("- Reader notices:");
        foreach (var g in notices.Take(30)) md.AppendLine($"  - {g.Key.Type}: {g.Key.Message} ×{g.Count()}");
    }
    md.AppendLine();

    csv.AppendLine(string.Join(",", Q(relative), d.Version, sizeBefore, sizeAfter, r.Status, r.ReasonKey, Q(r.Detail),
        Q(string.Join("; ", r.Removed.Select(kv => $"{kv.Key} {kv.Value}"))), d.Passes, d.UnknownEntities, d.UnsupportedNotices,
        Q(string.Join("; ", d.NotWritten.Select(kv => $"{kv.Key} x{kv.Value}"))), Q(roundtrip), d.ReadMs, d.PurgeMs, d.WriteMs, d.TotalMs));
}

File.WriteAllText(Path.Combine(output, "report.md"), md.ToString());
File.WriteAllText(Path.Combine(output, "report.csv"), csv.ToString(), new UTF8Encoding(true));
Console.WriteLine($"Report: {Path.Combine(output, "report.md")}");
return 0;

static string RoundTrip(string file, string temp)
{
    try
    {
        var isDxf = file.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase);
        var doc = isDxf ? DxfReader.Read(file) : DwgReader.Read(file, new DwgReaderConfiguration { KeepUnknownEntities = true, KeepUnknownNonGraphicalObjects = true });
        var before = DocumentSnapshot.Of(doc);
        if (isDxf) DxfWriter.Write(temp, doc, false); else DwgWriter.Write(temp, doc, new DwgWriterConfiguration { WriteDynamicBlockData = true, WriteShapes = true }, null);
        var after = DocumentSnapshot.Of(isDxf ? DxfReader.Read(temp) : DwgReader.Read(temp));
        return after.SameAs(before, out var diff) ? "OK" : "DIFFERENT: " + diff;
    }
    catch (Exception e)
    {
        return "ERROR: " + e.GetType().Name + ": " + e.Message;
    }
    finally
    {
        try { File.Delete(temp); } catch { }
    }
}

static string Q(string? s) => s is null ? "" : "\"" + s.Replace("\"", "\"\"") + "\"";

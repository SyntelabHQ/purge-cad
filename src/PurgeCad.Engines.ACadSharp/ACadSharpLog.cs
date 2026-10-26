using System.Text;
using PurgeCad.Core.Purging;

namespace PurgeCad.Engines.ACadSharp;

/// <summary>
/// Log of one ACadSharp run, next to the AutoCAD logs ("plan.dwg.acadsharp.log"): the result, what was removed,
/// what the library reported while reading and writing, and the verification.
/// </summary>
static class ACadSharpLog
{
    const int MaxNoticeKinds = 40;

    public static void Write(FilePurgeRequest request, PurgeDiagnostics diag, FilePurgeResult result)
    {
        try
        {
            var path = EngineLogs.PathFor(request.SourcePath, ".acadsharp.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Build(request, diag, result), new UTF8Encoding(true));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    static string Build(FilePurgeRequest request, PurgeDiagnostics diag, FilePurgeResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PurgeCAD {DateTime.Now:yyyy-MM-dd HH:mm:ss} · engine: ACadSharp · status: {result.Status}"
                      + (result.ReasonKey is null ? "" : $" ({result.ReasonKey})"));
        sb.AppendLine($"File: {request.SourcePath}");
        sb.AppendLine(request.DryRun ? "Mode: report only (nothing saved)" : $"Output: {request.OutputPath}");
        sb.AppendLine($"Format: {diag.Version ?? "-"}{(diag.SavedAs is { } to ? " → " + to : "")} · passes: {diag.Passes} · unknown entities: {diag.UnknownEntities}"
                      + $" · reader notices it cannot handle: {diag.UnsupportedNotices}");
        sb.AppendLine($"Time: read {diag.ReadMs} ms · purge {diag.PurgeMs} ms · write {diag.WriteMs} ms");
        if (!string.IsNullOrEmpty(result.Detail)) sb.AppendLine($"Detail: {result.Detail}");

        sb.AppendLine();
        sb.AppendLine($"----- removed ({diag.Removed.Count}) -----");
        foreach (var line in diag.Removed) sb.AppendLine(line);

        if (diag.NotWritten.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("----- not written by ACadSharp (lost in the output) -----");
            foreach (var (message, count) in diag.NotWritten) sb.AppendLine($"{message} ×{count}");
        }

        if (diag.Expected is not null && diag.Actual is not null)
        {
            sb.AppendLine();
            sb.AppendLine("----- verification (re-read of the saved file) -----");
            sb.AppendLine(diag.Actual.SameAs(diag.Expected, out var difference) ? "OK: same entities and tables as expected." : difference);
            foreach (var (table, count) in diag.Actual.Tables)
                sb.AppendLine($"{table}: {diag.Before?.Tables.GetValueOrDefault(table)} → {count}");
        }

        Notices(sb, "reader notices", diag.ReadNotifications);
        Notices(sb, "writer notices", diag.WriteNotifications);
        return sb.ToString();
    }

    /// <summary>Notices grouped by text (a drawing can produce thousands of the same one).</summary>
    static void Notices(StringBuilder sb, string title, List<Notice> notices)
    {
        if (notices.Count == 0) return;
        var groups = notices.GroupBy(n => (n.Type, n.Message)).OrderByDescending(g => g.Count()).ToList();
        sb.AppendLine();
        sb.AppendLine($"----- {title} ({notices.Count}) -----");
        foreach (var g in groups.Take(MaxNoticeKinds)) sb.AppendLine($"[{g.Key.Type}] {g.Key.Message} ×{g.Count()}");
        if (groups.Count > MaxNoticeKinds) sb.AppendLine($"… and {groups.Count - MaxNoticeKinds} more kinds");
    }
}

using System.Globalization;
using System.Text;
using PurgeCad.Core.Batch;
using PurgeCad.Core.Branding;

namespace PurgeCad.Core.Reporting;

/// <summary>Writes the run report as CSV (UTF-8 with BOM so Excel shows every script correctly).</summary>
public static class CsvReport
{
    /// <param name="translate">Turns localization keys (status, reasons, categories) into text in the UI language.</param>
    public static void Write(string path, BatchSummary summary, Func<string, string> translate, char separator = ',')
    {
        var sb = new StringBuilder();
        void Row(params string?[] cells) =>
            sb.AppendLine(string.Join(separator, cells.Select(c => Escape(c ?? "", separator))));

        Row(translate("report.col.file"), translate("report.col.status"), translate("report.col.reason"),
            translate("report.col.sizeBefore"), translate("report.col.sizeAfter"), translate("report.col.removed"),
            translate("report.col.engine"), translate("report.col.version"), translate("report.col.seconds"),
            translate("report.col.detail"));

        foreach (var f in summary.Files)
        {
            var removed = string.Join("; ", f.Removed.Where(r => r.Value > 0)
                .Select(r => $"{translate("category." + r.Key)}: {r.Value}"));
            Row(f.File.RelativePath,
                translate("status." + f.Status),
                f.ReasonKey is null ? null : translate(f.ReasonKey),
                f.SizeBefore.ToString(CultureInfo.InvariantCulture),
                f.SizeAfter?.ToString(CultureInfo.InvariantCulture),
                removed,
                f.FallbackFrom is null ? f.EngineId : $"{f.EngineId} ({f.FallbackFrom} → {f.EngineId})",
                f.SavedAs is null ? f.Version : $"{f.Version} → {f.SavedAs}",
                f.Duration.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture),
                f.Detail);
        }

        sb.AppendLine();
        sb.AppendLine(Escape(Brand.AttributionNotice.Replace('\n', ' '), separator));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    static string Escape(string value, char separator) =>
        value.IndexOfAny([separator, '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}

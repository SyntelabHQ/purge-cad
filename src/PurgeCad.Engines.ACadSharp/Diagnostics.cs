using ACadSharp;
using ACadSharp.IO;
using PurgeCad.Core.Purging;

namespace PurgeCad.Engines.ACadSharp;

public sealed record Notice(NotificationType Type, string Message);

/// <summary>Everything the proof tool reports for one file.</summary>
public sealed class PurgeDiagnostics
{
    public FilePurgeResult? Result { get; set; }
    public string? Version { get; set; }
    public int Passes { get; set; }
    public int UnknownEntities { get; set; }
    public int UnsupportedNotices { get; set; }
    public long ReadMs { get; set; }
    public long PurgeMs { get; set; }
    public long WriteMs { get; set; }
    public long TotalMs { get; set; }
    public List<string> Removed { get; } = new();

    /// <summary>Object types the writer left out (message → count): data lost in the output.</summary>
    public Dictionary<string, int> NotWritten { get; set; } = new();
    public List<Notice> ReadNotifications { get; } = new();
    public List<Notice> WriteNotifications { get; } = new();
    public List<Notice> VerifyNotifications { get; } = new();
    public DocumentSnapshot? Before { get; set; }
    public DocumentSnapshot? Expected { get; set; }
    public DocumentSnapshot? Actual { get; set; }
}

/// <summary>Counts used to check that writing did not lose anything: entities per block record and table sizes.</summary>
public sealed class DocumentSnapshot
{
    public SortedDictionary<string, int> EntitiesPerBlock { get; } = new(StringComparer.OrdinalIgnoreCase);
    public SortedDictionary<string, int> Tables { get; } = new();

    public int TotalEntities => EntitiesPerBlock.Values.Sum();

    public static DocumentSnapshot Of(CadDocument doc)
    {
        var s = new DocumentSnapshot();
        foreach (var record in doc.BlockRecords)
        {
            // Anonymous blocks may be renumbered on save (*U12 → *U3); count them together.
            var key = record.IsAnonymous ? "*anonymous" : record.Name;
            s.EntitiesPerBlock[key] = s.EntitiesPerBlock.GetValueOrDefault(key) + record.Entities.Count();
        }
        s.Tables["Blocks"] = doc.BlockRecords.Count;
        s.Tables["Layers"] = doc.Layers.Count;
        s.Tables["Linetypes"] = doc.LineTypes.Count;
        s.Tables["TextStyles"] = doc.TextStyles.Count;
        s.Tables["DimStyles"] = doc.DimensionStyles.Count;
        s.Tables["AppIds"] = doc.AppIds.Count;
        s.Tables["MLineStyles"] = doc.MLineStyles.Count();
        s.Tables["Layouts"] = doc.Layouts.Count();
        return s;
    }

    /// <summary>Same entities per block and same table sizes; otherwise a short description of the first differences.</summary>
    public bool SameAs(DocumentSnapshot other, out string difference)
    {
        var diffs = new List<string>();
        foreach (var key in EntitiesPerBlock.Keys.Union(other.EntitiesPerBlock.Keys))
        {
            var a = EntitiesPerBlock.GetValueOrDefault(key);
            var b = other.EntitiesPerBlock.GetValueOrDefault(key);
            if (a != b) diffs.Add($"{key}: {a} → {b} entities");
        }
        foreach (var key in Tables.Keys)
            if (Tables[key] != other.Tables.GetValueOrDefault(key)) diffs.Add($"{key}: {Tables[key]} → {other.Tables.GetValueOrDefault(key)}");
        difference = string.Join("; ", diffs.Take(8));
        return diffs.Count == 0;
    }
}

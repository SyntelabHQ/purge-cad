using System.Diagnostics;
using System.Text;
using ACadSharp;
using ACadSharp.Blocks;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using PurgeCad.Core.Purging;

namespace PurgeCad.Engines.ACadSharp;

/// <summary>
/// Purge engine on ACadSharp. Medium risk: the library rewrites the whole file, and objects it does not know may be lost.
/// Safeguards: unknown objects → skip (unless allowed), conservative reference check, write to temp, re-read and compare.
/// </summary>
public sealed class ACadSharpEngine : IPurgeEngine
{
    const int MaxPasses = 50;

    public string Id => "acadsharp";
    public string NameKey => "engine.acadsharp.name";
    public RiskLevel Risk => RiskLevel.Medium;

    public IReadOnlyList<string> RiskKeys { get; } =
    [
        "engine.acadsharp.risk.rewrite",
        "engine.acadsharp.risk.unknownObjects",
        "engine.acadsharp.risk.dwg2007",
    ];

    /// <summary>
    /// What this engine purges for now. Kept to the symbol tables whose usage can be checked reliably;
    /// more categories are added after they pass the proof test.
    /// </summary>
    public IReadOnlySet<PurgeCategory> SupportedCategories { get; } = new HashSet<PurgeCategory>
    {
        PurgeCategory.Blocks, PurgeCategory.Layers, PurgeCategory.Linetypes, PurgeCategory.TextStyles,
        PurgeCategory.DimensionStyles, PurgeCategory.MultilineStyles, PurgeCategory.RegisteredApps,
    };

    /// <summary>Pure .NET, CPU-bound: one file per core is fine; the batch runner caps at 4.</summary>
    public int MaxParallelism => Math.Max(1, Environment.ProcessorCount);

    public EngineAvailability CheckAvailability() => EngineAvailability.Available;

    public Task<FilePurgeResult> PurgeAsync(FilePurgeRequest request, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var diagnostics = new PurgeDiagnostics();
            var result = Purge(request, cancellationToken, diagnostics);
            ACadSharpLog.Write(request, diagnostics, result);
            return result;
        }, cancellationToken);

    /// <summary>Full result with diagnostics, used by the proof tool.</summary>
    public PurgeDiagnostics PurgeWithDiagnostics(FilePurgeRequest request, CancellationToken cancellationToken = default)
    {
        var diagnostics = new PurgeDiagnostics();
        diagnostics.Result = Purge(request, cancellationToken, diagnostics);
        return diagnostics;
    }

    FilePurgeResult Purge(FilePurgeRequest request, CancellationToken ct, PurgeDiagnostics? diag = null)
    {
        diag ??= new PurgeDiagnostics();
        var watch = Stopwatch.StartNew();
        var isDxf = request.SourcePath.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase);
        var binaryDxf = isDxf && IsBinaryDxf(request.SourcePath);

        // 1. Read
        CadDocument doc;
        try
        {
            doc = Read(request.SourcePath, isDxf, diag.ReadNotifications);
        }
        catch (Exception e)
        {
            return FilePurgeResult.Fail("fail.corrupt", e.Message);
        }
        diag.ReadMs = watch.ElapsedMilliseconds;
        var version = doc.Header.Version;
        diag.Version = version.ToString();
        ct.ThrowIfCancellationRequested();
        // Convert tab, drawing already in that version: nothing to write
        if (request.Options.NothingToDo(diag.Version))
            return new FilePurgeResult { Status = FileStatus.NothingToPurge, Version = diag.Version };

        // 2. Safety pre-checks
        diag.UnknownEntities = CountUnknownEntities(doc);
        diag.UnsupportedNotices = diag.ReadNotifications.Count(n =>
            n.Type is NotificationType.NotImplemented or NotificationType.NotSupported or NotificationType.Error);

        if ((diag.UnknownEntities > 0 || diag.UnsupportedNotices > 0) && !request.Options.AllowUnknownObjects)
            return Skip("skip.unknownObjects", $"{diag.UnknownEntities} unknown entities, {diag.UnsupportedNotices} reader notices");

        // Save in another version when asked. ACadSharp cannot write DWG 2007 (AC1021).
        ACadVersion? target = request.Options.SaveVersion is { } wanted && Enum.TryParse<ACadVersion>(wanted, out var t) && t != version
            ? t : null;
        if (!isDxf && target == ACadVersion.AC1021)
            return Skip("engine.acadsharp.saveVersion2007");

        if (!isDxf && version == ACadVersion.AC1021 && target is null && !request.Options.Upgrade2007To2010)
            return Skip("skip.dwg2007");

        // 3. Purge, repeated until nothing more goes (nested blocks free their layers / styles on the next pass)
        var before = Snapshot(doc);
        var removed = new Dictionary<PurgeCategory, int>();
        for (var pass = 1; pass <= MaxPasses; pass++)
        {
            ct.ThrowIfCancellationRequested();
            diag.Passes = pass;
            var refs = ReferenceCollector.Collect(doc);
            var n = 0;
            var o = request.Options;
            if (o.Includes(PurgeCategory.Blocks)) n += Add(removed, PurgeCategory.Blocks, PurgeBlocks(doc, refs, diag));
            if (o.Includes(PurgeCategory.Layers)) n += Add(removed, PurgeCategory.Layers, PurgeLayers(doc, refs, diag));
            if (o.Includes(PurgeCategory.Linetypes)) n += Add(removed, PurgeCategory.Linetypes, PurgeLineTypes(doc, refs, diag));
            if (o.Includes(PurgeCategory.TextStyles)) n += Add(removed, PurgeCategory.TextStyles, PurgeTextStyles(doc, refs, diag));
            if (o.Includes(PurgeCategory.DimensionStyles)) n += Add(removed, PurgeCategory.DimensionStyles, PurgeDimStyles(doc, refs, diag));
            if (o.Includes(PurgeCategory.MultilineStyles)) n += Add(removed, PurgeCategory.MultilineStyles, PurgeMLineStyles(doc, refs, diag));
            if (o.Includes(PurgeCategory.RegisteredApps)) n += Add(removed, PurgeCategory.RegisteredApps, PurgeAppIds(doc, refs, diag));
            if (n == 0) break;
        }
        diag.PurgeMs = watch.ElapsedMilliseconds - diag.ReadMs;

        diag.Before = before;
        var total = removed.Values.Sum();
        var savedAs = target?.ToString();
        diag.SavedAs = savedAs;
        // converted to another version: written even when nothing was purged
        if (total == 0 && target is null)
            return new FilePurgeResult { Status = FileStatus.NothingToPurge, Removed = removed, Version = diag.Version };

        if (request.DryRun)
            return new FilePurgeResult { Status = FileStatus.Purged, Removed = removed, Version = diag.Version, SavedAs = savedAs };

        // 4. Write in the original version (or the one asked for; 2007 → 2010 only when allowed), then verify by reading it back
        if (target is { } to) doc.Header.Version = to;
        else if (!isDxf && version == ACadVersion.AC1021) doc.Header.Version = ACadVersion.AC1024;
        var expected = Snapshot(doc);
        try
        {
            Write(doc, request.OutputPath, isDxf, binaryDxf, diag.WriteNotifications);
            diag.WriteMs = watch.ElapsedMilliseconds - diag.ReadMs - diag.PurgeMs;

            // Objects ACadSharp cannot write (visual styles, associativity, DGN line style data, …) would be lost.
            diag.NotWritten = diag.WriteNotifications
                .Where(n => n.Type == NotificationType.NotImplemented)
                .GroupBy(n => n.Message)
                .ToDictionary(g => g.Key, g => g.Count());
            if (diag.NotWritten.Count > 0 && !request.Options.AllowUnknownObjects)
            {
                TryDelete(request.OutputPath);
                return Skip("skip.unknownObjects", string.Join("; ", diag.NotWritten.Select(kv => $"{kv.Key} ×{kv.Value}")));
            }

            var check = Read(request.OutputPath, isDxf, diag.VerifyNotifications);
            var actual = Snapshot(check);
            diag.Before = before;
            diag.Expected = expected;
            diag.Actual = actual;
            if (!actual.SameAs(expected, out var difference))
            {
                TryDelete(request.OutputPath);
                return FilePurgeResult.Fail("fail.verify", difference);
            }
        }
        catch (Exception e)
        {
            TryDelete(request.OutputPath);
            return FilePurgeResult.Fail("fail.unexpected", e.Message);
        }
        diag.TotalMs = watch.ElapsedMilliseconds;

        return new FilePurgeResult { Status = FileStatus.Purged, Removed = removed, Version = diag.Version, SavedAs = savedAs };

        FilePurgeResult Skip(string key, string? detail = null) =>
            new() { Status = FileStatus.Skipped, ReasonKey = key, Detail = detail, Version = diag.Version };
    }

    // ---------------- Purge per table ----------------

    static int PurgeBlocks(CadDocument doc, ReferenceCollector refs, PurgeDiagnostics diag)
    {
        var count = 0;
        foreach (var record in doc.BlockRecords.ToList())
        {
            if (record.Layout is not null) continue;                       // model / paper spaces
            if (record.IsAnonymous || record.IsDynamic) continue;           // hatches, dimensions, dynamic blocks: not touched
            if (record.Name.StartsWith('*') || record.Name.Contains('|')) continue; // special, xref-dependent
            var flags = record.Flags;
            if (flags.HasFlag(BlockTypeFlags.XRef) || flags.HasFlag(BlockTypeFlags.XRefOverlay) || flags.HasFlag(BlockTypeFlags.XRefDependent))
                continue;
            if (refs.IsReferenced(record) || doc.BlockRecords.GetReferences(record.Name).Any()) continue;
            if (doc.BlockRecords.Remove(record.Name) is not null) { count++; diag.Removed.Add($"Block: {record.Name}"); }
        }
        return count;
    }

    static int PurgeLayers(CadDocument doc, ReferenceCollector refs, PurgeDiagnostics diag)
    {
        var current = doc.Header.CurrentLayerName;
        var count = 0;
        foreach (var layer in doc.Layers.ToList())
        {
            if (Is(layer.Name, "0") || Is(layer.Name, "Defpoints") || Is(layer.Name, current) || layer.Name.Contains('|')) continue;
            if (refs.IsReferenced(layer) || doc.Layers.GetReferences(layer.Name).Any()) continue;
            if (doc.Layers.Remove(layer.Name) is not null) { count++; diag.Removed.Add($"Layer: {layer.Name}"); }
        }
        return count;
    }

    static int PurgeLineTypes(CadDocument doc, ReferenceCollector refs, PurgeDiagnostics diag)
    {
        var current = doc.Header.CurrentLineTypeName;
        var count = 0;
        foreach (var lt in doc.LineTypes.ToList())
        {
            if (Is(lt.Name, "ByLayer") || Is(lt.Name, "ByBlock") || Is(lt.Name, "Continuous") || Is(lt.Name, current) || lt.Name.Contains('|')) continue;
            if (refs.IsReferenced(lt) || doc.LineTypes.GetReferences(lt.Name).Any()) continue;
            if (doc.LineTypes.Remove(lt.Name) is not null) { count++; diag.Removed.Add($"Linetype: {lt.Name}"); }
        }
        return count;
    }

    static int PurgeTextStyles(CadDocument doc, ReferenceCollector refs, PurgeDiagnostics diag)
    {
        var current = doc.Header.CurrentTextStyleName;
        var count = 0;
        foreach (var style in doc.TextStyles.ToList())
        {
            // Unnamed styles are shape files used by linetypes / shapes: never removed here.
            if (string.IsNullOrEmpty(style.Name) || Is(style.Name, "Standard") || Is(style.Name, current) || style.Name.Contains('|')) continue;
            if (refs.IsReferenced(style) || doc.TextStyles.GetReferences(style.Name).Any()) continue;
            if (doc.TextStyles.Remove(style.Name) is not null) { count++; diag.Removed.Add($"Text style: {style.Name}"); }
        }
        return count;
    }

    static int PurgeDimStyles(CadDocument doc, ReferenceCollector refs, PurgeDiagnostics diag)
    {
        var current = doc.Header.CurrentDimensionStyleName;
        var count = 0;
        foreach (var style in doc.DimensionStyles.ToList())
        {
            if (Is(style.Name, "Standard") || Is(style.Name, "ISO-25") || Is(style.Name, "Annotative") || Is(style.Name, current)
                || style.Name.Contains('|') || style.Name.Contains('$')) continue; // "$" = child styles (STYLE$2, …)
            if (refs.IsReferenced(style) || doc.DimensionStyles.GetReferences(style.Name).Any()) continue;
            if (HasChildStyles(doc, style.Name)) continue;
            if (doc.DimensionStyles.Remove(style.Name) is not null) { count++; diag.Removed.Add($"Dimension style: {style.Name}"); }
        }
        return count;
    }

    static bool HasChildStyles(CadDocument doc, string name) =>
        doc.DimensionStyles.Any(s => s.Name.StartsWith(name + "$", StringComparison.OrdinalIgnoreCase));

    static int PurgeMLineStyles(CadDocument doc, ReferenceCollector refs, PurgeDiagnostics diag)
    {
        var current = doc.Header.CurrentMLineStyleName;
        var count = 0;
        foreach (var style in doc.MLineStyles.ToList())
        {
            if (Is(style.Name, "Standard") || Is(style.Name, current)) continue;
            if (refs.IsReferenced(style) || doc.MLineStyles.GetReferences(style.Name).Any()) continue;
            if (doc.MLineStyles.Remove(style.Name)) { count++; diag.Removed.Add($"Multiline style: {style.Name}"); }
        }
        return count;
    }

    static int PurgeAppIds(CadDocument doc, ReferenceCollector refs, PurgeDiagnostics diag)
    {
        var count = 0;
        foreach (var app in doc.AppIds.ToList())
        {
            // ACAD* applications belong to AutoCAD itself (annotative, dynamic blocks, …): always kept.
            if (app.Name.StartsWith("ACAD", StringComparison.OrdinalIgnoreCase) || app.Name.StartsWith("AcDb", StringComparison.OrdinalIgnoreCase)
                || app.Name.StartsWith("AcadAnnot", StringComparison.OrdinalIgnoreCase)) continue;
            if (refs.IsReferenced(app) || doc.AppIds.GetReferences(app.Name).Any()) continue;
            if (doc.AppIds.Remove(app.Name) is not null) { count++; diag.Removed.Add($"Registered app: {app.Name}"); }
        }
        return count;
    }

    // ---------------- Helpers ----------------

    static bool Is(string? a, string? b) => a is not null && b is not null && a.Equals(b, StringComparison.OrdinalIgnoreCase);

    static int Add(Dictionary<PurgeCategory, int> removed, PurgeCategory category, int n)
    {
        if (n > 0) removed[category] = removed.GetValueOrDefault(category) + n;
        return n;
    }

    static int CountUnknownEntities(CadDocument doc) =>
        doc.BlockRecords.Sum(r => r.Entities.Count(e => e is UnknownEntity or ProxyEntity));

    static CadDocument Read(string path, bool isDxf, List<Notice> notices)
    {
        NotificationEventHandler handler = (_, e) => notices.Add(new Notice(e.NotificationType, e.Message));
        if (isDxf) return DxfReader.Read(path, handler);
        var config = new DwgReaderConfiguration
        {
            KeepUnknownEntities = true,
            KeepUnknownNonGraphicalObjects = true,
            Failsafe = false,
        };
        return DwgReader.Read(path, config, handler);
    }

    static void Write(CadDocument doc, string path, bool isDxf, bool binaryDxf, List<Notice> notices)
    {
        NotificationEventHandler handler = (_, e) => notices.Add(new Notice(e.NotificationType, e.Message));
        if (isDxf) DxfWriter.Write(path, doc, binaryDxf, new DxfWriterConfiguration(), handler);
        else DwgWriter.Write(path, doc, new DwgWriterConfiguration
        {
            // ACadSharp leaves these out by default; without them dynamic blocks lose their parameters and shapes vanish.
            WriteDynamicBlockData = true,
            WriteShapes = true,
        }, handler);
    }

    static bool IsBinaryDxf(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[18];
            return stream.Read(buffer, 0, buffer.Length) == buffer.Length
                && Encoding.ASCII.GetString(buffer) == "AutoCAD Binary DXF";
        }
        catch (IOException) { return false; }
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    internal static DocumentSnapshot Snapshot(CadDocument doc) => DocumentSnapshot.Of(doc);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

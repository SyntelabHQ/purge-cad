using System.Runtime.InteropServices;
using PurgeCad.Core.Batch;
using PurgeCad.Core.Branding;
using PurgeCad.Core.Purging;
using PurgeCad.Core.Reporting;
using PurgeCad.Core.Scanning;

namespace PurgeCad.App.Services;

/// <summary>
/// PurgeCAD without a window, for scripts and scheduled tasks:
/// <code>PurgeCAD.exe --run "D:\Projects\Tower" --mode copy --out "D:\Purged" --engine auto --save-as 2013</code>
/// Options not given come from the profile named with --profile, otherwise from what the window used last.
/// Nothing is saved to the settings. Exit code: 0 all done, 1 some files failed, 2 wrong arguments, 3 no drawings.
/// On Windows run it with "start /wait" (or from a .bat file) to wait for it and see its exit code.
/// </summary>
public static class CommandLine
{
    const int Ok = 0, SomeFailed = 1, BadArguments = 2, NothingFound = 3;

    public static bool IsCommand(string[] args) =>
        args.Any(a => a is "--run" or "--help" or "-h" or "/?" or "--version");

    public static int Run(string[] args)
    {
        AttachToConsole();
        try
        {
            return RunAsync(args).GetAwaiter().GetResult();
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine("PurgeCAD: " + e.Message);
            Console.Error.WriteLine("PurgeCAD --help shows the options.");
            return BadArguments;
        }
    }

    static async Task<int> RunAsync(string[] args)
    {
        if (args.Contains("--version"))
        {
            Console.WriteLine($"{Brand.AppName} {typeof(CommandLine).Assembly.GetName().Version?.ToString(3)}");
            return Ok;
        }
        if (args.Any(a => a is "--help" or "-h" or "/?"))
        {
            Console.WriteLine(Help);
            return Ok;
        }

        var host = AppHost.Current;
        host.ApplyLanguage();
        var settings = host.Settings;
        var paths = new List<string>();
        string? reportPath = null;
        var yes = false;
        bool? subfolders = null;

        // a profile first, so the other options can change what it sets
        if (Value(args, "--profile") is { } profileName)
        {
            var profile = settings.FindProfile(profileName)
                          ?? throw new ArgumentException($"no profile named \"{profileName}\" (saved: {string.Join(", ", settings.Profiles.Select(p => p.Name))})");
            profile.ApplyTo(settings);
        }

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value");
            switch (a)
            {
                case "--run": break;
                case "--profile": i++; break;
                case "--engine":
                    var engine = Next().ToLowerInvariant();
                    if (engine != EngineRegistry.AutomaticId && host.Engines.Find(engine) is null)
                        throw new ArgumentException($"unknown engine \"{engine}\" (auto, {string.Join(", ", host.Engines.Engines.Select(e => e.Id))})");
                    settings.Batch.EngineId = engine;
                    break;
                case "--mode":
                    settings.Batch.Mode = Next().ToLowerInvariant() switch
                    {
                        "copy" => OutputMode.CopyToFolder,
                        "inplace" => OutputMode.InPlaceWithBackup,
                        "replace" => OutputMode.ReplaceOriginals,
                        "report" => OutputMode.ReportOnly,
                        var m => throw new ArgumentException($"unknown mode \"{m}\" (copy, inplace, replace, report)"),
                    };
                    break;
                case "--out": settings.Batch.OutputFolder = Path.GetFullPath(Next()); break;
                case "--save-as":
                    var version = Next();
                    settings.Batch.Purge.SaveVersion = version.Equals("original", StringComparison.OrdinalIgnoreCase) ? null
                        : SaveVersions.Parse(version) ?? throw new ArgumentException(
                            $"unknown version \"{version}\" (original, {string.Join(", ", SaveVersions.All.Select(v => v.Name))})");
                    break;
                case "--convert":
                    var to = Next();
                    settings.Batch.Purge = PurgeOptions.ConvertOnly(SaveVersions.Parse(to) ?? throw new ArgumentException(
                        $"unknown version \"{to}\" ({string.Join(", ", SaveVersions.All.Select(v => v.Name))})"), settings.Batch.Purge);
                    break;
                case "--parallel":
                    settings.Batch.Parallelism = int.TryParse(Next(), out var n) && n is >= 1 and <= 4 ? n
                        : throw new ArgumentException("--parallel takes 1 to 4");
                    break;
                case "--subfolders": subfolders = true; break;
                case "--no-subfolders": subfolders = false; break;
                case "--missing-refs": settings.Batch.Purge.Categories.Add(PurgeCategory.MissingReferences); break;
                case "--report": reportPath = Path.GetFullPath(Next()); break;
                case "--yes": yes = true; break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"unknown option {a}");
                    paths.Add(Path.GetFullPath(a.Trim('"')));
                    break;
            }
        }

        if (paths.Count == 0) throw new ArgumentException("give a folder or drawings to purge");
        if (paths.FirstOrDefault(p => !File.Exists(p) && !Directory.Exists(p)) is { } missing)
            throw new ArgumentException($"not found: {missing}");
        if (settings.Batch.Mode == OutputMode.CopyToFolder && string.IsNullOrWhiteSpace(settings.Batch.OutputFolder))
            throw new ArgumentException("copy mode needs --out <folder>");
        if (settings.Batch.Mode == OutputMode.ReplaceOriginals && !yes)
            throw new ArgumentException("--mode replace overwrites the originals without a backup; add --yes to confirm");

        var engine1 = host.Engines.ResolveForRun(settings.Batch.EngineId)
                      ?? throw new ArgumentException($"the engine \"{settings.Batch.EngineId}\" is not available here");

        // what to purge: a folder (with its subfolders unless told otherwise), or the drawings given
        var scan = paths.Count == 1 && Directory.Exists(paths[0])
            ? await FolderScanner.ScanAsync(new ScanOptions(paths[0], subfolders ?? settings.IncludeSubfolders,
                settings.IncludeDwg, settings.IncludeDxf, IncludeAuxiliary: false,
                settings.Batch.Mode == OutputMode.CopyToFolder ? [settings.Batch.OutputFolder!] : null))
            : FolderScanner.FromFiles(paths.SelectMany(p => Directory.Exists(p)
                ? Directory.EnumerateFiles(p, "*", new EnumerationOptions { RecurseSubdirectories = subfolders ?? false, IgnoreInaccessible = true })
                : [p]));
        if (scan.Drawings.Count == 0)
        {
            Console.WriteLine("PurgeCAD: no drawings found.");
            return NothingFound;
        }

        var engineName = Loc.Instance[engine1.NameKey];
        Console.WriteLine($"{Brand.AppName}: {scan.Drawings.Count} drawings in {scan.Root}");
        Console.WriteLine($"  engine {engineName} · mode {settings.Batch.Mode}"
                          + (settings.Batch.Mode == OutputMode.CopyToFolder ? $" → {settings.Batch.OutputFolder}" : "")
                          + (settings.Batch.Purge.SaveVersion is { } sv ? $" · save as {SaveVersions.NameOf(sv)}" : ""));

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); Console.WriteLine("Stopping after the files already started..."); };

        var done = 0;
        var lockConsole = new object();
        var summary = await BatchRunner.RunAsync(scan, new PurgeOperation(engine1), settings.Batch, null, report =>
        {
            lock (lockConsole)
            {
                done++;
                Console.WriteLine($"[{done}/{scan.Drawings.Count}] {Loc.Instance["status." + report.Status],-12} {report.File.RelativePath}  {Line(report)}");
            }
        }, stop.Token);

        Console.WriteLine();
        Console.WriteLine($"Purged {summary.Count(FileStatus.Purged)} · unchanged {summary.Count(FileStatus.NothingToPurge)}"
                          + $" · skipped {summary.Count(FileStatus.Skipped)} · failed {summary.Count(FileStatus.Failed)}"
                          + $" · saved {Loc.Instance.Size(Math.Max(0, summary.BytesSaved))}{(summary.IsEstimate ? " (estimate)" : "")}"
                          + (summary.BackupFolder is { } b ? $"\nBackup: {b}" : ""));
        if (reportPath is not null)
        {
            CsvReport.Write(reportPath, summary, key => Loc.Instance[key]);
            Console.WriteLine($"Report: {reportPath}");
        }
        return summary.Cancelled ? SomeFailed : summary.Count(FileStatus.Failed) > 0 ? SomeFailed : Ok;
    }

    static string Line(FileReport r)
    {
        var parts = new List<string>();
        if (r.SizeAfter is { } after) parts.Add($"{Loc.Instance.Size(r.SizeBefore)} → {Loc.Instance.Size(after)}");
        if (r.Removed.Values.Sum() is var n && n > 0) parts.Add($"{n} removed");
        if (r.SavedAs is { } v) parts.Add("saved as " + SaveVersions.NameOf(v));
        if (r.FallbackFrom is not null && r.EngineId is { } e) parts.Add("by " + Loc.Instance[$"engine.{e}.name"]);
        if (r.ReasonKey is { } key) parts.Add(Loc.Instance.Format(key, ("owner", r.Detail ?? "?")));
        return string.Join(" · ", parts);
    }

    static string? Value(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    const string Help = """
        PurgeCAD without a window.

          PurgeCAD --run <folder | drawings...> [options]

        Options (not given: the profile's, otherwise what the window used last):
          --engine auto|autocad|acadsharp   which engine purges (auto: AutoCAD, ACadSharp where AutoCAD cannot)
          --mode copy|inplace|report|replace
                                            copy to --out, in place with a backup, report only (nothing saved),
                                            or replace the originals without a backup (needs --yes)
          --out <folder>                    where copies go (copy mode)
          --save-as original|2018|2013|2010|2007|2004|2000
                                            save in another DWG / DXF version
          --convert 2018|2013|2010|2007|2004|2000
                                            only save in that version, nothing purged (the Convert tab)
          --profile <name>                  use a profile saved in the window
          --subfolders | --no-subfolders    include the folder's subfolders or not
          --missing-refs                    also detach xrefs / images / PDFs whose file is gone (AutoCAD)
          --parallel 1-4                    files at the same time
          --report <file.csv>               write the report
          --yes                             confirm --mode replace

        Exit code: 0 done, 1 some files failed, 2 wrong arguments, 3 no drawings found.
        On Windows: start /wait PurgeCAD.exe --run ... (to wait and get the exit code).
        """;

    /// <summary>A Windows app has no console of its own: write to the one PurgeCAD was started from, if any.</summary>
    static void AttachToConsole()
    {
        if (!OperatingSystem.IsWindows() || !AttachConsole(-1)) return;
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        Console.WriteLine();
    }

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);
}

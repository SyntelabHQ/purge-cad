using System.Diagnostics;
using System.Text;
using PurgeCad.Core.Purging;

namespace PurgeCad.Engines.AutoCad;

/// <summary>
/// Purge engine on AutoCAD's accoreconsole.exe (full AutoCAD, Windows). Very low risk: AutoCAD itself reads,
/// purges and writes the drawing, so nothing it does not understand is lost.
///
/// Per file:
///   1. copy the source into a private work folder (AutoCAD never opens the original, so no .dwl appears next to it);
///   2. run accoreconsole /i work\in.dwg /s work\run.scr — the script loads the PurgeCAD plugin (NETLOAD), which purges
///      like PURGE repeated until nothing is left, plus the DGN linetype data, layer filters and scales; then one -PURGE
///      pass and SAVEAS work\out.dwg in the original format. Without the plugin (or if AutoCAD did not run it) a plain
///      script does the purge: -PURGE passes with AutoLISP clean-ups;
///   3. move work\out.dwg to the requested output path and delete the work folder.
///
/// The console's output is kept in %TEMP%\PurgeCAD\logs\&lt;drawing&gt;.log, so a failure can be diagnosed.
/// </summary>
public sealed class AutoCadEngine(Func<string?> manualConsolePath) : IPurgeEngine
{
    public string Id => "autocad";
    public string NameKey => "engine.autocad.name";
    public RiskLevel Risk => RiskLevel.VeryLow;

    public IReadOnlyList<string> RiskKeys { get; } =
    [
        "engine.autocad.risk.newerFiles",
        "engine.autocad.risk.windowsOnly",
        "engine.autocad.risk.categories",
    ];

    public IReadOnlySet<PurgeCategory> SupportedCategories => AutoCadScript.Supported;

    /// <summary>Each accoreconsole takes memory and starts AutoCAD's core; 4 at most.</summary>
    public int MaxParallelism => 4;


    public EngineAvailability CheckAvailability()
    {
        if (!OperatingSystem.IsWindows()) return EngineAvailability.Unavailable("engine.autocad.unavailable.os");
        if (AutoCadLocator.Best(manualConsolePath()) is null)
            return EngineAvailability.Unavailable("engine.autocad.unavailable.notFound");
        return EngineAvailability.Available;
    }

    public async Task<FilePurgeResult> PurgeAsync(FilePurgeRequest request, CancellationToken cancellationToken)
    {
        if (AutoCadLocator.Best(manualConsolePath()) is not { } install)
            return FilePurgeResult.Skip("engine.autocad.unavailable.notFound");

        var extension = Path.GetExtension(request.SourcePath).ToLowerInvariant();
        var dxf = extension == ".dxf";
        var version = dxf ? AutoCadScript.ReadDxfVersion(request.SourcePath) : DrawingVersion.ReadDwg(request.SourcePath);
        if (dxf && version is null)
            return FilePurgeResult.Skip("engine.autocad.binaryDxf");
        if (version is not null && string.CompareOrdinal(version, AutoCadScript.NewestFormat(install.Release)) > 0)
            return new FilePurgeResult { Status = FileStatus.Skipped, ReasonKey = "skip.versionTooNew", Version = version };
        // A version to save in that this AutoCAD does not have yet (2018 asked from AutoCAD 2013).
        if (request.Options.SaveVersion is { } wanted && string.CompareOrdinal(wanted, AutoCadScript.NewestFormat(install.Release)) > 0)
            return new FilePurgeResult { Status = FileStatus.Skipped, ReasonKey = "engine.autocad.saveVersionTooNew", Version = version };
        // Convert tab, drawing already in that version: AutoCAD is not started at all
        if (request.Options.NothingToDo(version))
            return new FilePurgeResult { Status = FileStatus.NothingToPurge, Version = version };
        var converts = request.Options.Converts(version);
        var savedAs = converts ? request.Options.SaveVersion : null;

        var work = Path.Combine(Path.GetTempPath(), "PurgeCAD", "acad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var logFile = LogPathFor(request.SourcePath);
        try
        {
            var input = Path.Combine(work, "in" + extension);
            var output = Path.Combine(work, "out" + extension);
            var script = Path.Combine(work, "run.scr");
            var counts = Path.Combine(work, "counts.txt");
            var job = Path.Combine(work, "job.txt");
            File.Copy(request.SourcePath, input);
            File.SetAttributes(input, FileAttributes.Normal);

            var major = AutoCadScript.Major(install.Release);
            async Task<(string Script, ConsoleRun Run)> Attempt(string? plugin)
            {
                foreach (var stale in new[] { output, counts }) if (File.Exists(stale)) File.Delete(stale);
                if (plugin is not null)
                    await File.WriteAllTextAsync(job, AutoCadScript.BuildJob(request.Options, counts, request.SourcePath), new UTF8Encoding(false), cancellationToken);
                var text = AutoCadScript.Build(request.Options, output, AutoCadScript.SaveFormat(request.Options.TargetVersion(version), major), dxf, major, counts, plugin);
                // Plain ANSI-safe text when the paths allow it (every AutoCAD reads that); UTF-8 with BOM only for
                // paths with other characters, which AutoCAD 2021+ reads.
                var ascii = text.All(c => c < 128);
                await File.WriteAllTextAsync(script, text,
                    ascii ? new UTF8Encoding(false) : new UTF8Encoding(true), cancellationToken);

                var jobPath = plugin is null ? null : job;
                var run = await RunConsoleAsync(install.ConsolePath, input, script, jobPath, logFile, text, request.Progress, cancellationToken);
                if (run.Outcome == RunOutcome.Exited && !HasOutput(output) && AutoCadScript.IsLicensingFailure(run.Log))
                {
                    // AutoCAD's licensing sometimes fails when consoles start close together; one quiet retry.
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    run = await RunConsoleAsync(install.ConsolePath, input, script, jobPath, logFile, text, request.Progress, cancellationToken);
                }
                // What the plugin / AutoLISP clean-ups removed (written to a file: the console does not show LISP output).
                if (File.Exists(counts))
                    run = run with { Log = run.Log + "\n----- PurgeCAD clean-up counts -----\n" + File.ReadAllText(counts) };
                return (text, run);
            }

            // The plugin does the full purge; when it is missing, or AutoCAD did not load or run it, the plain
            // script (-PURGE and AutoLISP) runs instead, on the untouched copy.
            var pluginPath = AutoCadPlugin.PathFor(major);
            var (scriptText, run) = await Attempt(pluginPath);
            var counted = run.Log;
            if (pluginPath is not null && run.Outcome == RunOutcome.Exited && !run.Log.Contains(AutoCadScript.PluginRanMarker))
            {
                var first = run.Log;
                (scriptText, run) = await Attempt(null);
                counted = run.Log;
                run = run with { Log = "----- the plugin did not run; output of that attempt -----\n" + first
                                       + "\n----- plain script -----\n" + run.Log };
            }
            SaveLog(logFile, scriptText, run);

            if (run.Outcome == RunOutcome.CannotOpen)
                return FilePurgeResult.Fail("engine.autocad.fail.cannotOpen", Tail(run.Log, logFile));
            if (run.Outcome == RunOutcome.Stuck)
                return FilePurgeResult.Fail("engine.autocad.fail.stuck", Tail(run.Log, logFile));
            if (!HasOutput(output) && AutoCadScript.IsLicensingFailure(run.Log))
                return FilePurgeResult.Fail("engine.autocad.fail.license", Tail(run.Log, logFile));

            var removed = AutoCadScript.ParseRemoved(counted, out var understood);

            if (!HasOutput(output))
                return FilePurgeResult.Fail("engine.autocad.fail.noOutput", Tail(run.Log, logFile));

            // converted to another version: written even when nothing was purged
            if (understood && removed.Values.Sum() == 0 && !converts)
                return new FilePurgeResult { Status = FileStatus.NothingToPurge, Version = version };

            if (request.DryRun)
                return new FilePurgeResult { Status = FileStatus.Purged, Removed = removed, Version = version, SavedAs = savedAs };

            File.Move(output, request.OutputPath, overwrite: true);
            return new FilePurgeResult { Status = FileStatus.Purged, Removed = removed, Version = version, SavedAs = savedAs };
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    enum RunOutcome { Exited, Stuck, CannotOpen }

    readonly record struct ConsoleRun(RunOutcome Outcome, string Log, int? ExitCode);

    static bool HasOutput(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

    /// <summary>
    /// accoreconsole prints something for every command. This long without any output means it waits for an answer
    /// it will never get (a prompt the script did not expect, or a dialog), so it is stopped.
    /// </summary>
    static readonly TimeSpan SilenceLimit = TimeSpan.FromMinutes(2);
    static readonly TimeSpan PluginSilenceLimit = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Consoles start one at a time, a few seconds apart. Several accoreconsole processes starting at the same
    /// moment can trip AutoCAD's licensing ("Unable to initialize adlm", error -104).
    /// </summary>
    const string PluginPass = "PurgeCAD: pass";
    const int PluginSteps = 4;

    static readonly SemaphoreSlim StartGate = new(1, 1);
    static readonly TimeSpan StartSpacing = TimeSpan.FromSeconds(4);

    static async Task<ConsoleRun> RunConsoleAsync(string console, string input, string script, string? job, string logFile,
        string scriptText, IProgress<double>? progress, CancellationToken token)
    {
        var start = new ProcessStartInfo(console)
        {
            // AutoCAD expects to start from its own folder; a foreign working folder can break its licensing.
            WorkingDirectory = Path.GetDirectoryName(console)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("/i");
        start.ArgumentList.Add(input);
        start.ArgumentList.Add("/s");
        start.ArgumentList.Add(script);
        // the plugin finds its job file through this variable (no prompt, so nothing can leave the console waiting)
        if (job is not null) start.Environment[AutoCadScript.PluginJobVariable] = job;
        // the plugin works silently through big drawings for a while; a prompt waiting for input is what the limit is for
        var silenceLimit = job is null ? SilenceLimit : PluginSilenceLimit;

        Process process;
        await StartGate.WaitAsync(token);
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("accoreconsole did not start.");
        }
        catch
        {
            StartGate.Release();
            throw;
        }
        // Keep the gate closed while this console initialises (or until it exits early).
        _ = Task.WhenAny(process.WaitForExitAsync(CancellationToken.None), Task.Delay(StartSpacing))
            .ContinueWith(_ => StartGate.Release(), TaskScheduler.Default);

        using (process)
        {
            // Nothing is typed into the console: once the script is done, or if a prompt asks for more,
            // it reads end-of-input instead of waiting for a keyboard that does not exist.
            try { process.StandardInput.Close(); } catch (IOException) { }

            void Kill()
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            }

            await using var onCancel = token.Register(Kill);

            var buffer = new MemoryStream();
            var lastActivity = Stopwatch.StartNew();
            var stuck = false;
            var cannotOpen = false;
            var openTail = "";

            // The log file is written while AutoCAD runs, so it can be read even when the console hangs.
            var live = OpenLiveLog(logFile, scriptText);
            var liveClosed = false;
            // Progress: the console echoes each command it runs; "-PURGE" (language-independent) marks the steps,
            // and the plugin prints "PurgeCAD: pass n" (counted as a few steps: it does most of the work).
            var steps = Math.Max(1, CountOf(scriptText, "-PURGE") + (job is null ? 0 : PluginSteps));
            var stepsSeen = 0;
            var tail = "";
            var read = Task.Run(async () =>
            {
                var chunk = new byte[8192];
                var chars = new char[8192];
                var stream = process.StandardOutput.BaseStream;
                Decoder? decoder = null;
                int n;
                while ((n = await stream.ReadAsync(chunk, CancellationToken.None)) > 0)
                {
                    lock (buffer) buffer.Write(chunk, 0, n);
                    lastActivity.Restart();
                    // accoreconsole writes UTF-16 when redirected (every other byte 0); older builds write ANSI.
                    decoder ??= (n >= 2 && chunk[1] == 0 ? Encoding.Unicode : Encoding.UTF8).GetDecoder();
                    var text = new string(chars, 0, decoder.GetChars(chunk, 0, n, chars, 0)).Replace("\0", "");

                    // count "-PURGE" echoes, also when one is split between two reads
                    var window = tail + text;
                    stepsSeen += CountOf(window, "-PURGE") - CountOf(tail, "-PURGE")
                                 + CountOf(window, PluginPass) - CountOf(tail, PluginPass);
                    tail = window.Length > 16 ? window[^16..] : window;

                    // AutoCAD could not open the drawing and went on with an empty one: stop it before it saves that.
                    var openWindow = openTail + text;
                    openTail = openWindow.Length > 64 ? openWindow[^64..] : openWindow;
                    if (!cannotOpen && AutoCadScript.IsOpenFailure(openWindow))
                    {
                        cannotOpen = true;
                        Kill();
                    }
                    progress?.Report(Math.Min(0.97, (double)stepsSeen / steps));

                    if (live is null) continue;
                    lock (buffer)
                    try
                    {
                        if (liveClosed) continue;
                        live.Write(text);
                        live.Flush();
                    }
                    catch (IOException) { }
                }
            }, CancellationToken.None);
            var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);

            var exited = process.WaitForExitAsync(CancellationToken.None);
            while (!exited.IsCompleted)
            {
                await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None));
                if (!exited.IsCompleted && lastActivity.Elapsed > silenceLimit)
                {
                    stuck = true;
                    Kill();
                    break;
                }
            }

            await exited;
            await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(5)));
            lock (buffer) { liveClosed = true; live?.Dispose(); }
            var stderr = errors.IsCompleted ? await errors : "";
            token.ThrowIfCancellationRequested();

            byte[] bytes;
            lock (buffer) bytes = buffer.ToArray();
            var log = AutoCadScript.DecodeLog(bytes);
            if (!string.IsNullOrWhiteSpace(stderr)) log += "\n[stderr]\n" + stderr;
            var outcome = cannotOpen || AutoCadScript.IsOpenFailure(log) ? RunOutcome.CannotOpen
                : stuck ? RunOutcome.Stuck : RunOutcome.Exited;
            return new ConsoleRun(outcome, log,
                process.HasExited ? process.ExitCode : null);
        }
    }

    static string LogPathFor(string source) => EngineLogs.PathFor(source);

    static int CountOf(string text, string what)
    {
        var count = 0;
        for (var i = text.IndexOf(what, StringComparison.Ordinal); i >= 0; i = text.IndexOf(what, i + what.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    static StreamWriter? OpenLiveLog(string path, string script)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(true));
            writer.Write($"PurgeCAD {DateTime.Now:yyyy-MM-dd HH:mm:ss} · running\n----- script -----\n{script}----- accoreconsole output (live) -----\n");
            writer.Flush();
            return writer;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    static void SaveLog(string path, string script, ConsoleRun run)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,
                $"PurgeCAD {DateTime.Now:yyyy-MM-dd HH:mm:ss} · outcome: {run.Outcome} · exit code: {run.ExitCode?.ToString() ?? "-"}\n" +
                "----- script -----\n" + script +
                "----- accoreconsole output -----\n" + run.Log,
                new UTF8Encoding(true));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    static string Tail(string log, string logFile) =>
        string.Join("\n", log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(8))
        + "\n\nLog: " + logFile;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

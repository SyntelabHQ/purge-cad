using System.Text;
using System.Text.RegularExpressions;
using PurgeCad.Core.Purging;

namespace PurgeCad.Engines.AutoCad;

/// <summary>
/// Builds the accoreconsole script and reads its log. Commands use the "_." prefix and "_" options so they work
/// in every AutoCAD language. Pure functions, covered by unit tests.
/// </summary>
public static class AutoCadScript
{
    /// <summary>
    /// -PURGE option per category, with the first AutoCAD release (major R number) that offers it.
    /// A keyword AutoCAD does not know stops the whole script and leaves the console waiting, so an option is only
    /// sent to releases known to have it.
    /// </summary>
    static readonly (PurgeCategory Category, string Option, bool Named, int Since)[] PurgeOptions =
    [
        (PurgeCategory.Blocks, "_B", true, 16),
        (PurgeCategory.Layers, "_LA", true, 16),
        (PurgeCategory.Linetypes, "_LT", true, 16),
        (PurgeCategory.TextStyles, "_ST", true, 16),
        (PurgeCategory.DimensionStyles, "_D", true, 16),
        (PurgeCategory.TableStyles, "_T", true, 16),
        (PurgeCategory.MultileaderStyles, "_MU", true, 17),
        (PurgeCategory.MultilineStyles, "_M", true, 16),
        (PurgeCategory.Materials, "_MA", true, 17),
        (PurgeCategory.ViewStyles, "_DE", true, 19),
        (PurgeCategory.ViewStyles, "_SE", true, 19),
        (PurgeCategory.RegisteredApps, "_R", true, 16),
        (PurgeCategory.ZeroLengthGeometry, "_Z", false, 18),
        (PurgeCategory.EmptyTexts, "_E", false, 18),
    ];

    /// <summary>
    /// When every named category is chosen, these -PURGE types are added too (what the user calls "purge everything").
    /// "-PURGE All" is not used: in some releases it also deletes zero-length geometry, which is opt-in here.
    /// </summary>
    static readonly PurgeCategory[] AllNamed =
    [
        PurgeCategory.Blocks, PurgeCategory.Layers, PurgeCategory.Linetypes, PurgeCategory.TextStyles,
        PurgeCategory.DimensionStyles, PurgeCategory.TableStyles, PurgeCategory.MultileaderStyles,
        PurgeCategory.MultilineStyles, PurgeCategory.Materials, PurgeCategory.ViewStyles, PurgeCategory.RegisteredApps,
    ];

    static readonly (string Option, int Since)[] Extras =
    [
        ("_G", 17),   // groups (empty / unnamed)
        ("_P", 16),   // plot styles
        ("_SH", 16),  // shape files
        ("_V", 17),   // visual styles
    ];

    /// <summary>
    /// What -PURGE cannot reach, done in AutoLISP (core AutoLISP only, no ActiveX, so it runs in accoreconsole).
    /// Each routine prints "PURGECAD &lt;what&gt; &lt;count&gt;" for the log reader. One expression per line,
    /// and every routine checks before it touches anything: a LISP error would stop the script.
    ///
    ///  - DGN line style data (ACAD_DGNLINESTYLECOMP): the components that no linetype in use reaches are
    ///    erased (in use: layers, entities, block contents, dimension and multiline styles, CELTYPE, xref
    ///    linetypes). Linetypes not in use lose their DGN data, which otherwise keeps them from purging.
    ///    The walk starts at each linetype in use (and its extension dictionary) and follows dictionary entries,
    ///    xrecord pointers and extension dictionaries; it never walks through the containers that hold all
    ///    components (otherwise every component would look used). Components that linetypes still in use need
    ///    are kept, so the drawing looks the same. Alternated with -PURGE: purging linetypes frees components,
    ///    erasing components frees linetypes.
    ///  - Layer filters: the layer table's ACAD_LAYERFILTERS and AcLyDictionary (layer states are kept).
    ///  - Annotation scales: -SCALELISTEDIT Reset (deletes the scales nothing uses, restores the default list),
    ///    only when the command exists (getcname), so an unknown command cannot derail the script.
    /// </summary>
    static readonly string[] LispHelpers =
    [
        // accoreconsole does not show (princ) output in its log, so counts go to a file the engine reads back
        "(defun pcad-say (what n / f) (if (and pcad-out (setq f (open pcad-out \"a\"))) (progn (write-line (strcat \"PURGECAD \" what \" \" (itoa n)) f) (close f))) (princ))",
        // entries of a dictionary (enames after the AcDbDictionary marker)
        "(defun pcad-entries (e / r on) (foreach p (entget e) (if (and (= (car p) 100) (= (cdr p) \"AcDbDictionary\")) (setq on T)) (if (and on (member (car p) '(350 360)) (= (type (cdr p)) 'ENAME)) (setq r (cons (cdr p) r)))) r)",
        // extension dictionary of an object
        "(defun pcad-xd (e / d x) (setq d (entget e)) (while d (if (and (= (caar d) 102) (= (cdar d) \"{ACAD_XDICTIONARY\")) (setq x (cdadr d) d nil) (setq d (cdr d)))) x)",
        // owner of an object: the first 330 outside the {ACAD_REACTORS ...} groups
        "(defun pcad-owner (e / in o) (foreach p (entget e) (cond ((= (car p) 102) (setq in (= (substr (cdr p) 1 1) \"{\"))) ((and (not in) (not o) (= (car p) 330)) (setq o (cdr p))))) o)",
        // what an object points to: its extension dictionary, dictionary entries, xrecord pointers, and the
        // hard/soft pointers (340-369) of any other object (more than strictly needed: keeps more, never less)
        "(defun pcad-refs (e / d typ r on in x) (setq d (entget e) typ (cdr (assoc 0 d))) (if (setq x (pcad-xd e)) (setq r (list x))) (foreach p d (cond ((= (car p) 102) (setq in (= (substr (cdr p) 1 1) \"{\"))) ((and (= (car p) 100) (member (cdr p) '(\"AcDbDictionary\" \"AcDbXrecord\"))) (setq on T)) ((and (not in) (= (type (cdr p)) 'ENAME) (or (and on (<= 330 (car p) 369)) (and (not on) (not (member typ '(\"DICTIONARY\" \"XRECORD\"))) (<= 340 (car p) 369)))) (setq r (cons (cdr p) r))))) r)",
        // all objects under a dictionary: components in pcad-comp, sub-dictionaries in pcad-cont
        "(defun pcad-collect (e) (foreach c (pcad-entries e) (if (entget c) (if (= (cdr (assoc 0 (entget c))) \"DICTIONARY\") (progn (setq pcad-cont (cons c pcad-cont)) (pcad-collect c)) (setq pcad-comp (cons c pcad-comp))))))",
        // linetype names really in use (upper case): layers, entities in the drawing and in block definitions,
        // dimension styles, multiline styles, the current linetype, and every xref-dependent linetype
        "(defun pcad-add (n u) (if (and n (not (member (setq n (strcase n)) u))) (cons n u) u))",
        "(defun pcad-used-lt (/ u d e b x) (setq u (list \"BYLAYER\" \"BYBLOCK\" \"CONTINUOUS\") u (pcad-add (getvar \"CELTYPE\") u)) (setq d (tblnext \"LAYER\" T)) (while d (setq u (pcad-add (cdr (assoc 6 d)) u) d (tblnext \"LAYER\"))) (setq d (tblnext \"LTYPE\" T)) (while d (if (= 16 (logand 16 (cdr (assoc 70 d)))) (setq u (pcad-add (cdr (assoc 2 d)) u))) (setq d (tblnext \"LTYPE\"))) (setq e (entnext)) (while e (setq u (pcad-add (cdr (assoc 6 (entget e))) u) e (entnext e))) (setq b (tblnext \"BLOCK\" T)) (while b (setq e (cdr (assoc -2 b))) (while e (setq u (pcad-add (cdr (assoc 6 (entget e))) u) e (entnext e))) (setq b (tblnext \"BLOCK\"))) (setq d (tblnext \"DIMSTYLE\" T)) (while d (foreach p (entget (tblobjname \"DIMSTYLE\" (cdr (assoc 2 d)))) (if (and (member (car p) '(345 346 347)) (= (type (cdr p)) 'ENAME) (entget (cdr p))) (setq u (pcad-add (cdr (assoc 2 (entget (cdr p)))) u)))) (setq d (tblnext \"DIMSTYLE\"))) (if (setq x (dictsearch (namedobjdict) \"ACAD_MLINESTYLE\")) (foreach m (pcad-entries (cdr (assoc -1 x))) (foreach p (entget m) (if (= (car p) 6) (setq u (pcad-add (cdr p) u)))))) u)",
        // keys of a dictionary
        "(defun pcad-keys (d / r) (foreach p (entget d) (if (= (car p) 3) (setq r (cons (cdr p) r)))) r)",
        // DGN clean-up. The DGN data and the DGN linetypes hold each other, so PURGE alone never frees them.
        //  1. components no linetype IN USE reaches are erased (the walk starts only at linetypes in use);
        //  2. linetypes not in use lose their extension dictionary content (their DGN data), so PURGE can take them.
        "(defun pcad-dgn (/ nod root q seen e n m used lt x) (setq n 0 m 0 nod (namedobjdict) used (pcad-used-lt) root (cdr (assoc -1 (dictsearch nod \"ACAD_DGNLINESTYLECOMP\")))) (if root (progn (setq pcad-comp nil pcad-cont (list nod root)) (pcad-collect root) (setq seen pcad-cont) (setq lt (tblnext \"LTYPE\" T)) (while lt (if (member (strcase (cdr (assoc 2 lt))) used) (setq q (cons (tblobjname \"LTYPE\" (cdr (assoc 2 lt))) q))) (setq lt (tblnext \"LTYPE\"))) (while q (setq e (car q) q (cdr q)) (if (and e (not (member e seen)) (entget e)) (progn (setq seen (cons e seen)) (foreach x (pcad-refs e) (setq q (cons x q)))))) (foreach e pcad-comp (if (and (not (member e seen)) (entget e)) (progn (entdel e) (setq n (1+ n))))))) (setq lt (tblnext \"LTYPE\" T)) (while lt (if (and (not (member (strcase (cdr (assoc 2 lt))) used)) (setq x (pcad-xd (tblobjname \"LTYPE\" (cdr (assoc 2 lt)))))) (foreach k (pcad-keys x) (if (setq e (cdr (assoc -1 (dictsearch x k)))) (progn (dictremove x k) (pcad-del e) (setq m (1+ m)))))) (setq lt (tblnext \"LTYPE\"))) (setq pcad-comp nil pcad-cont nil) (pcad-say \"dgn\" n) (pcad-say \"dgnlinetypes\" m))",
        // erase a dictionary and what it contains
        "(defun pcad-del (e) (if (and e (entget e)) (progn (if (= (cdr (assoc 0 (entget e))) \"DICTIONARY\") (foreach c (pcad-entries e) (pcad-del c))) (entdel e))))",
        "(defun pcad-lf (/ x n e) (setq n 0 x (pcad-xd (pcad-owner (tblobjname \"LAYER\" \"0\")))) (if x (foreach k '(\"ACAD_LAYERFILTERS\" \"AcLyDictionary\") (if (setq e (cdr (assoc -1 (dictsearch x k)))) (progn (setq n (+ n (max 1 (length (pcad-entries e))))) (dictremove x k) (pcad-del e))))) (pcad-say \"layerfilters\" n))",
        // diagnostics for the log: linetypes still in the drawing that nothing uses (they should have been purged)
        "(defun pcad-left (/ used lt n f) (setq used (pcad-used-lt) n 0 lt (tblnext \"LTYPE\" T)) (while lt (if (not (member (strcase (cdr (assoc 2 lt))) used)) (progn (setq n (1+ n)) (if (and pcad-out (< n 40) (setq f (open pcad-out \"a\"))) (progn (write-line (strcat \"  unused linetype left: \" (cdr (assoc 2 lt)) (if (pcad-xd (tblobjname \"LTYPE\" (cdr (assoc 2 lt)))) \" (has extension data)\" \"\")) f) (close f))))) (setq lt (tblnext \"LTYPE\"))) (pcad-say \"ltleft\" n))",
        "(defun pcad-count-scales (/ s) (if (setq s (dictsearch (namedobjdict) \"ACAD_SCALELIST\")) (length (pcad-entries (cdr (assoc -1 s)))) 0))",
        "(defun pcad-scales (/ n) (setq n (pcad-count-scales)) (if (getcname \"-SCALELISTEDIT\") (command \"_.-SCALELISTEDIT\" \"_R\" \"_Y\" \"_E\")) (pcad-say \"scales\" (max 0 (- n (pcad-count-scales)))))",
    ];

    public static IReadOnlySet<PurgeCategory> Supported { get; } = PurgeOptions.Select(p => p.Category)
        .Concat([PurgeCategory.DgnLinetypeComponents, PurgeCategory.LayerFilters, PurgeCategory.AnnotationScales,
                 PurgeCategory.MissingReferences]) // the plugin only
        .ToHashSet();

    /// <summary>Major release number from "R19.1" (99 when unknown, meaning "recent").</summary>
    public static int Major(string release) =>
        int.TryParse(new string(release.TrimStart('R').TakeWhile(char.IsDigit).ToArray()), out var m) ? m : 99;

    /// <summary>
    /// Script: optional AUDIT, layer filters, then the purge four times with the DGN clean-up between passes
    /// (nested items free up after each pass), unused annotation scales, then -SAVEAS in the original format.
    /// With every named category chosen, groups, plot styles, shapes and visual styles are purged too.
    ///
    /// With <paramref name="pluginPath"/> the PurgeCAD plugin does the purge instead (NETLOAD, then PURGECADRUN,
    /// which reads the job file named by the PURGECAD_JOB environment variable), followed by one plain -PURGE pass
    /// for what the plugin does not cover (groups, plot styles, shapes, visual styles). SECURELOAD is turned off
    /// only for the NETLOAD and restored right after (it does not exist before AutoCAD 2014 SP1: getvar gives nil).
    /// </summary>
    public static string Build(PurgeOptions options, string outputPath, string saveFormat, bool dxf, int releaseMajor = 99,
        string? resultPath = null, string? pluginPath = null)
    {
        var sb = new StringBuilder();
        if (resultPath is not null)
            sb.AppendLine($"(setq pcad-out \"{resultPath.Replace('\\', '/')}\")");
        if (options.AuditFirst) sb.AppendLine("_.AUDIT _Y");

        var plugin = pluginPath is not null;
        if (plugin)
        {
            sb.AppendLine("(setq pcad-secureload (getvar \"SECURELOAD\"))");
            sb.AppendLine("(if pcad-secureload (setvar \"SECURELOAD\" 0))");
            sb.AppendLine("_.NETLOAD");
            sb.AppendLine($"\"{pluginPath}\"");
            sb.AppendLine("(if pcad-secureload (setvar \"SECURELOAD\" pcad-secureload))");
            sb.AppendLine(PluginCommand);
        }

        // the plugin already did the DGN data, layer filters and scales
        var dgn = !plugin && options.Includes(PurgeCategory.DgnLinetypeComponents);
        var filters = !plugin && options.Includes(PurgeCategory.LayerFilters);
        var scales = !plugin && options.Includes(PurgeCategory.AnnotationScales);
        if (dgn || filters || scales)
            foreach (var helper in LispHelpers) sb.AppendLine(helper);
        if (filters) sb.AppendLine("(pcad-lf)");

        // Purge and DGN clean-up alternate: each frees items for the other (nested blocks, DGN linetypes).
        var everything = AllNamed.All(options.Includes);
        for (var pass = 0; pass < (plugin ? 1 : 4); pass++)
        {
            foreach (var (category, option, named, since) in PurgeOptions)
            {
                if (!options.Includes(category) || releaseMajor < since) continue;
                // Named types ask for names (*) and whether to verify each one (No).
                sb.AppendLine(named ? $"_.-PURGE {option} * _N" : $"_.-PURGE {option}");
            }
            if (everything)
                foreach (var (option, since) in Extras)
                    if (releaseMajor >= since) sb.AppendLine($"_.-PURGE {option} * _N");
            if (dgn && pass < 3) sb.AppendLine("(pcad-dgn)");
        }
        if (scales) sb.AppendLine("(pcad-scales)");
        if (dgn) sb.AppendLine("(pcad-left)");

        // SAVEAS (accoreconsole has no dialogs, so it asks on the command line; AutoCAD 2013's console has no
        // -SAVEAS): format, then (DXF only) version and precision, then the file name.
        sb.AppendLine(dxf
            ? $"_.SAVEAS _DXF _V {saveFormat} 16 \"{outputPath}\""
            : $"_.SAVEAS {saveFormat} \"{outputPath}\"");
        return sb.ToString();
    }

    /// <summary>The plugin's command (PurgeCad.AutoCad.Plugin).</summary>
    public const string PluginCommand = "PURGECADRUN";

    /// <summary>The environment variable that gives the plugin the job file's path.</summary>
    public const string PluginJobVariable = "PURGECAD_JOB";

    /// <summary>The plugin writes this line to the counts file when its command ran.</summary>
    public const string PluginRanMarker = "PURGECAD plugin 1";

    /// <summary>
    /// Job file for the plugin: where to write the counts, the categories to purge, and the original drawing's path
    /// (AutoCAD opens a copy in a work folder; references with relative paths are looked for next to the original).
    /// </summary>
    public static string BuildJob(PurgeOptions options, string countsPath, string? sourcePath = null) =>
        $"counts={countsPath}\ncategories={string.Join(",", options.Categories.Order().Select(c => c.ToString()))}\n"
        + (sourcePath is null ? "" : $"source={sourcePath}\n");

    /// <summary>
    /// The -SAVEAS format keyword for a version code ("AC1027" → "2013"). Drawings older than R14 are saved as 2000;
    /// an unknown code is saved in the newest format the AutoCAD release offers.
    /// </summary>
    public static string SaveFormat(string? versionCode, int releaseMajor = 99) => versionCode switch
    {
        "AC1014" => "R14",
        "AC1015" => "2000",
        "AC1018" => "2004",
        "AC1021" => "2007",
        "AC1024" => "2010",
        "AC1027" => "2013",
        "AC1032" => "2018",
        _ when versionCode is not null && string.CompareOrdinal(versionCode, "AC1014") < 0 => "2000",
        _ => releaseMajor switch
        {
            >= 22 => "2018",
            >= 19 => "2013",
            18 => "2010",
            17 => "2007",
            _ => "2004",
        },
    };

    /// <summary>Newest drawing format an AutoCAD release can open ("R24.3" → "AC1032").</summary>
    public static string NewestFormat(string release)
    {
        var major = int.TryParse(new string(release.TrimStart('R').TakeWhile(char.IsDigit).ToArray()), out var m) ? m : 99;
        return major switch
        {
            >= 22 => "AC1032",
            >= 19 => "AC1027",
            18 => "AC1024",
            17 => "AC1021",
            _ => "AC1018",
        };
    }

    static readonly Regex Marker = new(@"PURGECAD (\w+) (\d+)");

    static readonly Regex Deleted = new(@"(\d+)\s+([A-Za-z][A-Za-z \-]*?)\s+deleted", RegexOptions.IgnoreCase);

    /// <summary>
    /// Counts what -PURGE reported as deleted ("3 layers deleted."). AutoCAD in another language prints other
    /// words; then <paramref name="understood"/> is false and the counts are unknown, not zero.
    /// </summary>
    public static Dictionary<PurgeCategory, int> ParseRemoved(string log, out bool understood)
    {
        var removed = new Dictionary<PurgeCategory, int>();
        understood = log.Contains("deleted", StringComparison.OrdinalIgnoreCase)
                     || log.Contains("No unreferenced", StringComparison.OrdinalIgnoreCase);
        foreach (Match m in Marker.Matches(log))
        {
            understood = true;
            var key = m.Groups[1].Value;
            PurgeCategory? category = key switch
            {
                "dgn" => PurgeCategory.DgnLinetypeComponents,
                "dgnlinetypes" => PurgeCategory.DgnLinetypeComponents,
                "layerfilters" => PurgeCategory.LayerFilters,
                "scales" => PurgeCategory.AnnotationScales,
                // the plugin writes PurgeCAD's own category names
                _ when char.IsLetter(key[0]) && Enum.TryParse<PurgeCategory>(key, out var named) => named,
                _ => null,
            };
            if (category is { } c)
                removed[c] = removed.GetValueOrDefault(c) + int.Parse(m.Groups[2].Value);
        }
        foreach (Match m in Deleted.Matches(log))
        {
            if (CategoryOf(m.Groups[2].Value) is not { } category) continue;
            removed[category] = removed.GetValueOrDefault(category) + int.Parse(m.Groups[1].Value);
        }
        return removed;
    }

    static PurgeCategory? CategoryOf(string words)
    {
        var w = words.ToLowerInvariant();
        if (w.Contains("multileader")) return PurgeCategory.MultileaderStyles;
        if (w.Contains("mline") || w.Contains("multiline")) return PurgeCategory.MultilineStyles;
        if (w.Contains("dimension") || w.Contains("dimstyle")) return PurgeCategory.DimensionStyles;
        if (w.Contains("table")) return PurgeCategory.TableStyles;
        if (w.Contains("text style") || w.Contains("textstyle")) return PurgeCategory.TextStyles;
        if (w.Contains("view style")) return PurgeCategory.ViewStyles;
        if (w.Contains("block")) return PurgeCategory.Blocks;
        if (w.Contains("layer")) return PurgeCategory.Layers;
        if (w.Contains("linetype")) return PurgeCategory.Linetypes;
        if (w.Contains("material")) return PurgeCategory.Materials;
        if (w.Contains("regapp") || w.Contains("registered")) return PurgeCategory.RegisteredApps;
        if (w.Contains("zero")) return PurgeCategory.ZeroLengthGeometry;
        if (w.Contains("text")) return PurgeCategory.EmptyTexts;
        return null;
    }

    /// <summary>
    /// What AutoCAD prints when it cannot open the input (damaged file, or written by a program AutoCAD does not
    /// accept). The console then goes on with an empty drawing, so the run must stop: saving it would give an empty file.
    /// </summary>
    static readonly string[] OpenFailureMarkers =
    [
        "Drawing file is not valid",
        "Invalid or incomplete DXF input",
        "drawing discarded",
        "is not a valid drawing",
    ];

    public static bool IsOpenFailure(string log) =>
        OpenFailureMarkers.Any(m => log.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when AutoCAD did not get as far as running the script: a licensing error (adlm / licence) or a console
    /// that ended almost without output (for example after its licensing dialog was closed).
    /// </summary>
    public static bool IsLicensingFailure(string log) =>
        log.Contains("adlm", StringComparison.OrdinalIgnoreCase)
        || log.Contains("licens", StringComparison.OrdinalIgnoreCase)
        || log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length < 3;

    /// <summary>accoreconsole writes UTF-16 when redirected; older builds write ANSI. Decode either.</summary>
    public static string DecodeLog(byte[] bytes)
    {
        if (bytes.Length >= 2)
        {
            var zeros = 0;
            for (var i = 1; i < Math.Min(bytes.Length, 400); i += 2) if (bytes[i] == 0) zeros++;
            if (zeros > Math.Min(bytes.Length, 400) / 4) return Encoding.Unicode.GetString(bytes).Replace("\0", "");
        }
        return Encoding.UTF8.GetString(bytes).Replace("\0", "");
    }

    /// <summary>$ACADVER from an ASCII DXF header, or null (binary DXF or not found).</summary>
    public static string? ReadDxfVersion(string path)
    {
        try
        {
            using var reader = new StreamReader(path, Encoding.Latin1);
            string? previous = null;
            for (var i = 0; i < 4000 && reader.ReadLine() is { } line; i++)
            {
                if (previous?.Trim() == "$ACADVER")
                {
                    // "  9 / $ACADVER /   1 / AC1027": this line is the group code, the next one the value.
                    var value = reader.ReadLine()?.Trim();
                    return value is not null && value.StartsWith("AC", StringComparison.Ordinal) ? value : null;
                }
                previous = line;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }
}

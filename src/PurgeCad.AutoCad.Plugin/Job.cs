using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PurgeCad.AutoCad.Plugin
{
    /// <summary>
    /// What to purge, as PurgeCAD wrote it (UTF-8, one "key=value" per line):
    /// <code>
    /// counts=C:\...\counts.txt
    /// categories=Blocks,Layers,Linetypes,...
    /// </code>
    /// Category names are PurgeCAD's own (PurgeCad.Core PurgeCategory). Without a job file nothing is purged.
    /// </summary>
    sealed class Job
    {
        public string CountsPath { get; private set; }
        public HashSet<string> Categories { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool Has(string category) => Categories.Contains(category);

        public static Job Read(string path)
        {
            var job = new Job();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return job;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();
                if (key == "counts") job.CountsPath = value;
                else if (key == "categories")
                    foreach (var c in value.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0))
                        job.Categories.Add(c);
            }
            return job;
        }
    }

    /// <summary>Counts per category, written as "PURGECAD &lt;Category&gt; &lt;n&gt;" lines that PurgeCAD reads back.</summary>
    sealed class Report
    {
        readonly Dictionary<string, int> removed = new Dictionary<string, int>();
        public int Errors, Passes;
        public List<string> Notes { get; } = new List<string>();

        public void Add(string category, int n = 1)
        {
            int v;
            removed.TryGetValue(category, out v);
            removed[category] = v + n;
        }

        public int Total => removed.Values.Sum();

        public void WriteTo(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var sb = new StringBuilder();
            // "plugin" tells PurgeCAD the command ran (otherwise it falls back to the plain script)
            sb.AppendLine("PURGECAD plugin 1");
            foreach (var pair in removed.Where(p => p.Value > 0)) sb.AppendLine("PURGECAD " + pair.Key + " " + pair.Value);
            sb.AppendLine("PURGECAD passes " + Passes);
            sb.AppendLine("PURGECAD errors " + Errors);
            foreach (var note in Notes) sb.AppendLine("  " + note);
            try { File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

using System;
using Autodesk.AutoCAD.Runtime;
using CoreApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(PurgeCad.AutoCad.Plugin.PurgeCommand))]

namespace PurgeCad.AutoCad.Plugin
{
    /// <summary>
    /// PURGECADRUN: purges the open drawing as the PurgeCAD job file asks. The job file's path comes from the
    /// PURGECAD_JOB environment variable, which PurgeCAD sets when it starts accoreconsole. The command never
    /// prompts (a prompt would leave the console waiting) and never saves: the script that called it saves.
    /// </summary>
    public sealed class PurgeCommand
    {
        public const string JobVariable = "PURGECAD_JOB";

        [CommandMethod("PURGECADRUN", CommandFlags.Modal)]
        public void Run()
        {
            var doc = CoreApplication.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var editor = doc.Editor;

            var job = Job.Read(Environment.GetEnvironmentVariable(JobVariable));
            var report = new Report();
            try
            {
                using (doc.LockDocument())
                    new Purger(doc.Database, job, report, message => editor.WriteMessage("\nPurgeCAD: " + message)).Run();
            }
            catch (System.Exception e)
            {
                report.Errors++;
                report.Notes.Add("stopped: " + e.GetType().Name + ": " + e.Message);
            }

            report.WriteTo(job.CountsPath);
            editor.WriteMessage("\nPurgeCAD: done, " + report.Total + " items removed in " + report.Passes + " passes.\n");
        }
    }
}

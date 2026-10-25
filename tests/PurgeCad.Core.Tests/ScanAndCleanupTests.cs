using PurgeCad.Core.Cleanup;
using PurgeCad.Core.Scanning;

namespace PurgeCad.Core.Tests;

public sealed class ScanAndCleanupTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "purgecad-tests-" + Guid.NewGuid().ToString("N"));

    public ScanAndCleanupTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        Directory.CreateDirectory(Path.Combine(_root, "_purgecad-backup"));
        Touch("a.dwg"); Touch("b.DXF"); Touch("sub/c.dwg");
        Touch("a.dwl"); Touch("a.dwl2"); Touch("old.bak"); Touch("x.sv$"); Touch("orphan.dwl");
        Touch("_purgecad-backup/ignored.dwg");
        Touch("notes.txt");
    }

    void Touch(string relative) => File.WriteAllText(Path.Combine(_root, relative), "x");

    [Fact]
    public void FindsDrawingsAndAuxiliaryFilesButNotOwnFolders()
    {
        var scan = FolderScanner.Scan(new ScanOptions(_root));
        Assert.Equal(3, scan.Drawings.Count);
        Assert.Equal(5, scan.Auxiliary.Count);
        Assert.DoesNotContain(scan.Drawings, d => d.Path.Contains("_purgecad-backup"));
    }

    [Fact]
    public void WithoutSubfoldersOnlyTopLevel()
    {
        var scan = FolderScanner.Scan(new ScanOptions(_root, IncludeSubfolders: false));
        Assert.Equal(2, scan.Drawings.Count);
    }

    [Fact]
    public void CleanupPlanFollowsOptions()
    {
        var scan = FolderScanner.Scan(new ScanOptions(_root));
        var plan = AuxiliaryCleaner.Plan(scan, new AuxiliaryCleanupOptions { Backups = true });
        Assert.Single(plan.ToRemove);
        Assert.EndsWith("old.bak", plan.ToRemove.Single().File.Path);
    }

    [Fact]
    public void LockFilesOfClosedOrMissingDrawingsCanGo()
    {
        var scan = FolderScanner.Scan(new ScanOptions(_root));
        var plan = AuxiliaryCleaner.Plan(scan, new AuxiliaryCleanupOptions { LockFiles = true });
        Assert.Equal(3, plan.Count); // a.dwl, a.dwl2 (a.dwg not open), orphan.dwl
    }

    [Fact]
    public void LockFileOfOpenDrawingIsKept()
    {
        if (!OperatingSystem.IsWindows()) return; // exclusive locks are advisory on Linux / macOS
        using var open = new FileStream(Path.Combine(_root, "a.dwg"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var scan = FolderScanner.Scan(new ScanOptions(_root));
        var plan = AuxiliaryCleaner.Plan(scan, new AuxiliaryCleanupOptions { LockFiles = true });
        Assert.Equal(1, plan.Count); // only orphan.dwl
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}

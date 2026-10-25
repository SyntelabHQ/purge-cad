namespace PurgeCad.Core.Batch;

/// <summary>File helpers that never leave a half-written drawing behind.</summary>
public static class SafeFile
{
    public const string TempSuffix = ".purgecad.tmp";

    /// <summary>Temp path next to <paramref name="target"/>, on the same disk so the final move is atomic.</summary>
    public static string TempPathFor(string target) => target + TempSuffix;

    /// <summary>True when nobody else has the file open (exclusive open succeeds). Read access only, so read-only files pass.</summary>
    public static bool CanOpenExclusively(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>Moves a finished temp file over the target in one step.</summary>
    public static void Commit(string tempPath, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(tempPath, target, overwrite: true);
    }

    public static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Copies a file keeping its relative path under <paramref name="destinationRoot"/>.</summary>
    public static string CopyKeepingStructure(string source, string relativePath, string destinationRoot)
    {
        var target = Path.Combine(destinationRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        return target;
    }
}

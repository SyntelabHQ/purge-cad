using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PurgeCad.Core.Cleanup;

public enum RemovalKind { RecycleBin, MovedToFolder, Deleted, Skipped, Failed }

public sealed record RemovalResult(RemovalKind Kind, string? Where = null, string? ReasonKey = null, string? Detail = null)
{
    public static RemovalResult Skipped(string reasonKey) => new(RemovalKind.Skipped, ReasonKey: reasonKey);
    public static RemovalResult Failed(string detail) => new(RemovalKind.Failed, ReasonKey: "cleanup.fail", Detail: detail);
}

/// <summary>
/// Removes files the safest way the OS allows: Recycle Bin / Trash; on network drives (no bin) a dated folder
/// under the scanned root; permanent delete only when the user asked for it.
/// </summary>
public abstract class FileRemover
{
    protected FileRemover(string root) => Root = root;

    protected string Root { get; }

    /// <summary>Fallback folder when there is no recycle bin.</summary>
    protected string FallbackFolder { get; } = DateTime.Now.ToString("yyyy-MM-dd_HHmm");

    public static FileRemover Create(string root, bool permanent)
    {
        if (permanent) return new PermanentRemover(root);
        if (OperatingSystem.IsWindows()) return new WindowsRecycleBin(root);
        if (OperatingSystem.IsMacOS()) return new MacTrash(root);
        return new FreedesktopTrash(root);
    }

    public RemovalResult Remove(string path, string relativePath)
    {
        try
        {
            return RemoveCore(path, relativePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return RemovalResult.Failed(e.Message);
        }
    }

    protected abstract RemovalResult RemoveCore(string path, string relativePath);

    /// <summary>Moves the file to &lt;root&gt;/_purgecad-deleted/&lt;date&gt;/&lt;relative path&gt;.</summary>
    protected RemovalResult MoveToFallback(string path, string relativePath)
    {
        var target = Path.Combine(Root, "_purgecad-deleted", FallbackFolder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(path, target, overwrite: true);
        return new RemovalResult(RemovalKind.MovedToFolder, target);
    }

    static string UniqueName(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        for (var i = 2; File.Exists(candidate) || Directory.Exists(candidate); i++)
            candidate = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(fileName)} ({i}){Path.GetExtension(fileName)}");
        return candidate;
    }

    sealed class PermanentRemover(string root) : FileRemover(root)
    {
        protected override RemovalResult RemoveCore(string path, string relativePath)
        {
            File.SetAttributes(path, FileAttributes.Normal); // hidden / read-only .dwl files
            File.Delete(path);
            return new RemovalResult(RemovalKind.Deleted);
        }
    }

    [SupportedOSPlatform("windows")]
    sealed class WindowsRecycleBin(string root) : FileRemover(root)
    {
        protected override RemovalResult RemoveCore(string path, string relativePath)
        {
            if (IsNetworkPath(path)) return MoveToFallback(path, relativePath);

            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0\0", // double-null-terminated list
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
            };
            var code = SHFileOperation(ref op);
            if (code != 0 || op.fAnyOperationsAborted)
                return RemovalResult.Failed($"SHFileOperation error 0x{code:X}");
            return new RemovalResult(RemovalKind.RecycleBin);
        }

        const uint FO_DELETE = 3;
        const ushort FOF_SILENT = 0x0004, FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040, FOF_NOERRORUI = 0x0400;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string? pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string? lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        static bool IsNetworkPath(string path)
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return true;
            var drive = Path.GetPathRoot(path);
            return drive is not null && new DriveInfo(drive).DriveType == DriveType.Network;
        }
    }

    sealed class MacTrash(string root) : FileRemover(root)
    {
        protected override RemovalResult RemoveCore(string path, string relativePath)
        {
            var trash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
            try
            {
                var target = UniqueName(trash, Path.GetFileName(path));
                File.Move(path, target);
                return new RemovalResult(RemovalKind.RecycleBin, target);
            }
            catch (IOException)
            {
                // Different volume (network share, external disk without trash): keep the file in a dated folder.
                return MoveToFallback(path, relativePath);
            }
        }
    }

    /// <summary>freedesktop.org Trash specification (GNOME, KDE, …): files/ + info/*.trashinfo.</summary>
    sealed class FreedesktopTrash(string root) : FileRemover(root)
    {
        protected override RemovalResult RemoveCore(string path, string relativePath)
        {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrEmpty(dataHome))
                dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

            var files = Path.Combine(dataHome, "Trash", "files");
            var info = Path.Combine(dataHome, "Trash", "info");
            Directory.CreateDirectory(files);
            Directory.CreateDirectory(info);

            var target = UniqueName(files, Path.GetFileName(path));
            var infoPath = Path.Combine(info, Path.GetFileName(target) + ".trashinfo");
            File.WriteAllText(infoPath,
                "[Trash Info]\n" +
                $"Path={Uri.EscapeDataString(Path.GetFullPath(path)).Replace("%2F", "/")}\n" +
                $"DeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n");
            try
            {
                File.Move(path, target);
                return new RemovalResult(RemovalKind.RecycleBin, target);
            }
            catch (IOException)
            {
                File.Delete(infoPath);
                return MoveToFallback(path, relativePath);
            }
        }
    }
}

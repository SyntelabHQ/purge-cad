using System.Text;

namespace PurgeCad.Core.Purging;

/// <summary>Reads the format version of a DWG ("AC1027") from its first 6 bytes, without opening it with a CAD library.</summary>
public static class DrawingVersion
{
    public static string? ReadDwg(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[6];
            if (stream.Read(buffer, 0, 6) < 6) return null;
            var code = Encoding.ASCII.GetString(buffer);
            return code.StartsWith("AC", StringComparison.Ordinal) ? code : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

}

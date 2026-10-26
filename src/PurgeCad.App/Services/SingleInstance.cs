using System.IO.Pipes;
using System.Text;

namespace PurgeCad.App.Services;

/// <summary>
/// One PurgeCAD window per user. Explorer starts PurgeCAD once per selected file; the first one opens the window,
/// the others hand their paths to it through a named pipe and close.
/// </summary>
public static class SingleInstance
{
    static readonly string Name = "PurgeCAD-" + Environment.UserName;
    static Mutex? _mutex;

    /// <summary>True when this process is the first one (it keeps the claim until it exits).</summary>
    public static bool Claim()
    {
        _mutex = new Mutex(initiallyOwned: true, Name + "-instance", out var createdNew);
        if (!createdNew) { _mutex.Dispose(); _mutex = null; }
        return createdNew;
    }

    /// <summary>Hands the paths to the first instance. False when it does not answer (then this one opens a window).</summary>
    public static bool Send(IReadOnlyList<string> paths)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out);
                pipe.Connect(1000);
                var bytes = Encoding.UTF8.GetBytes(string.Join("\n", paths));
                pipe.Write(bytes);
                pipe.Flush();
                return true;
            }
            catch (Exception e) when (e is TimeoutException or IOException) { Thread.Sleep(200); }
        }
        return false;
    }

    /// <summary>Receives paths from later instances, for as long as the app runs.</summary>
    public static void Listen(Action<IReadOnlyList<string>> received)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte);
                    pipe.WaitForConnection();
                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    var paths = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    received(paths); // empty: just bring the window to the front
                }
                catch (IOException) { Thread.Sleep(100); }
            }
        }) { IsBackground = true, Name = "PurgeCAD single instance" };
        thread.Start();
    }
}

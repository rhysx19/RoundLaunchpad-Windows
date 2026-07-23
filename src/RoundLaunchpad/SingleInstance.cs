using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace RoundLaunchpad;

/// <summary>Ensures only one instance runs; a second launch exits quietly.</summary>
internal static class SingleInstance
{
    private const string MutexName = "Local\\RoundLaunchpad.SingleInstance";
    private const string PipeName = "RoundLaunchpad.Activate";
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(200);
                using var w = new StreamWriter(client) { AutoFlush = true };
                w.WriteLine("activate");
            }
            catch
            {
                // first instance may be shutting down
            }
            return false;
        }

        StartServer();
        return true;
    }

    public static void Release()
    {
        try { _mutex?.ReleaseMutex(); } catch { /* ignore */ }
        _mutex?.Dispose();
        _mutex = null;
    }

    private static void StartServer()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    server.WaitForConnection();
                    using var r = new StreamReader(server);
                    _ = r.ReadLine();
                    // Second instance just exits; primary already has tray. No-op activate is fine.
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }
        })
        {
            IsBackground = true,
            Name = "RoundLaunchpad.SingleInstance"
        };
        thread.Start();
    }
}

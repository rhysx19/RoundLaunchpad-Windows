using System.Windows;

namespace RoundLaunchpad;

public partial class App : Application
{
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance: second launch just pings the first.
        if (!SingleInstance.TryAcquire())
        {
            Shutdown();
            return;
        }

        _controller = new AppController();
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        SingleInstance.Release();
        base.OnExit(e);
    }
}

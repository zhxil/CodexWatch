using Avalonia;

namespace CodexQuotaTray.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // All copies of the application share one instance in this Windows session.
        // Acquire before Avalonia creates any windows or tray icons.
        using var instance = new System.Threading.Mutex(false, @"Local\CodexWatch.Taskbar.SingleInstance");
        bool acquired;
        try
        {
            acquired = instance.WaitOne(0);
        }
        catch (System.Threading.AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired) return;
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}

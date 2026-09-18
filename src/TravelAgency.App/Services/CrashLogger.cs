using System.Text;

namespace TravelAgency.App.Services;

public static class CrashLogger
{
    private static readonly object Gate = new();

    public static string LogPath { get; } = Path.Combine(AppContext.BaseDirectory, "crash.log");

    public static void Attach()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("Unhandled", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Write("Unobserved", e.Exception);
    }

    public static void Write(string source, Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] {source}: {ex.GetType().FullName}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
            if (ex.InnerException is not null)
                sb.AppendLine($"  INNER: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
            sb.AppendLine();
            lock (Gate)
            {
                File.AppendAllText(LogPath, sb.ToString());
            }
        }
        catch
        {
        }
    }
}
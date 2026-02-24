using System.Text;

static class Logger
{
    public static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "ac_screenshot_client.log");
    static readonly object Lock = new();
    public static Action<string>? LogToUi;
    public static bool DebugMode { get; set; }

    public static void Log(string msg)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}";
        lock (Lock)
        {
            try { File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8); } catch { }
        }
        try { LogToUi?.Invoke(line); } catch { }
    }

    public static void Debug(string msg)
    {
        if (!DebugMode) return;
        Log("[DBG] " + msg);
    }
}

public sealed class ClientLogger
{
    private readonly string _logPath;
    private readonly object _lock = new();
    public event Action<string>? OnLog;

    public ClientLogger(string logPath = "logs/msi-client.log")
    {
        _logPath = logPath;
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
    }

    public void Info(string msg) => Write("INFO", msg);
    public void Warn(string msg) => Write("WARN", msg);
    public void Error(string msg, Exception? ex = null)
        => Write("ERROR", ex != null ? $"{msg} | {ex.Message}" : msg);

    private void Write(string level, string msg)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {msg}";
        lock (_lock)
        {
            try { File.AppendAllText(_logPath, line + Environment.NewLine); }
            catch
            {

            }
        }
        OnLog?.Invoke(line);
    }
}

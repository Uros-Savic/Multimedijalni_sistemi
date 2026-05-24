namespace MSI.Client;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (s, e) =>
        {
            LogError($"[THREAD] {e.Exception}");
            MessageBox.Show(
                $"Greska:\n{e.Exception.Message}\n\nLog: logs/msi-client.log",
                "MSI Greska", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            LogError($"[UNHANDLED] {e.ExceptionObject}");

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            LogError($"[STARTUP] {ex}");
            MessageBox.Show(
                $"Greska pri pokretanju:\n{ex.Message}\n\nLog: logs/msi-client.log",
                "MSI Greska", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void LogError(string msg)
    {
        try
        {
            Directory.CreateDirectory("logs");
            File.AppendAllText("logs/msi-client.log",
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}");
        }
        catch { }
    }
}

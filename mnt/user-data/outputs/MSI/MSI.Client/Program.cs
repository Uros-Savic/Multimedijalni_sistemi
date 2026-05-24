namespace MSI.Client;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (s, e) =>
        {
            try
            {
                string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [FATAL] {e.Exception}";
                Directory.CreateDirectory("logs");
                File.AppendAllText("logs/msi-client.log", msg + Environment.NewLine);
            }
            catch { }
            MessageBox.Show(
                $"Neocekivana greska:\n{e.Exception.Message}\n\nDetalji su zapisani u logs/msi-client.log",
                "Greska", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [UNHANDLED] {e.ExceptionObject}";
                Directory.CreateDirectory("logs");
                File.AppendAllText("logs/msi-client.log", msg + Environment.NewLine);
            }
            catch { }
        };

        Application.Run(new MainForm());
    }
}

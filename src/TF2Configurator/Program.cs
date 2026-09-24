using TF2Configurator.Forms;

namespace TF2Configurator;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Surface crashes instead of dying silently.
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);

        Application.Run(new MainForm());
    }

    private static void ReportCrash(Exception? ex)
    {
        var details = ex?.ToString() ?? "No details available.";
        var log = TryWriteLog(details);

        MessageBox.Show(
            "TF2 Configurator hit an unexpected error.\r\n\r\n" +
            details +
            "\r\n\r\nYour config files were not modified by this error. " +
            "Backups of every save this app makes are kept in:\r\n" +
            Services.SafeWriter.BackupRoot +
            (log is null ? "" : "\r\n\r\nThis error was also written to:\r\n" + log),
            "Unexpected error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    /// <summary>
    /// Writes the crash details to a log file so they survive closing the dialog. Returns the
    /// path, or null when even logging failed — then the dialog is all there is, and claiming a
    /// log exists would just be wrong.
    /// </summary>
    private static string? TryWriteLog(string details)
    {
        try
        {
            var path = Path.Combine(Services.CatalogService.CacheDir, "crash.log");
            Directory.CreateDirectory(Services.CatalogService.CacheDir);
            File.AppendAllText(path,
                $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====={Environment.NewLine}" +
                details + Environment.NewLine + Environment.NewLine);
            return path;
        }
        catch
        {
            return null;
        }
    }
}

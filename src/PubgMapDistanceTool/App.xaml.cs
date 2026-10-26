using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PubgMapDistanceTool;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    private static void App_DispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PubgMapDistanceTool");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "last-error.log"),
                $"{DateTime.Now:O}{Environment.NewLine}{e.Exception}");
        }
        catch
        {
            // The exception remains unhandled; logging must not mask the original failure.
        }
    }
}

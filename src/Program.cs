using System.Windows;

namespace DeskSpaces;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup.log"), $"entry {DateTime.Now:O}{Environment.NewLine}");
            var app = new App();
            app.InitializeComponent();
            app.Run();
            return 0;
        }
        catch (Exception error)
        {
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup-error.log"), error.ToString());
            }
            catch { }
            if (args.Contains("--watchdog", StringComparer.OrdinalIgnoreCase))
                return 1;
            MessageBox.Show(error.ToString(), "DeskSpaces 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
}

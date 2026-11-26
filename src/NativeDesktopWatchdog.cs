using System.Runtime.InteropServices;
using System.Text;

namespace DeskSpaces;

internal static class NativeDesktopWatchdog
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);

    public static int Run(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--restore-now") { RestoreIcons(); return 0; }
        if (args.Length < 2 || !int.TryParse(args[0], out var processId)) return 2;
        if (args.Length == 3 && args[2] == "--restore-now") { RestoreIcons(); return 0; }
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            process.WaitForExit();
        }
        catch { return 0; }
        try
        {
            var path = args[1];
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                using var document = System.Text.Json.JsonDocument.Parse(json);
                if (!document.RootElement.GetProperty("OriginalIconsVisible").GetBoolean()) return 0;
            }
            RestoreIcons();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "watchdog-restored.txt"), DateTime.Now.ToString("O"), Encoding.UTF8);
        }
        catch (Exception error)
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "watchdog-error.txt"), error.ToString(), Encoding.UTF8); } catch { }
            return 1;
        }
        return 0;
    }

    private static void RestoreIcons()
    {
        var progman = FindWindow("Progman", null);
        var view = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (view == IntPtr.Zero)
        {
            var worker = IntPtr.Zero;
            while (true)
            {
                var next = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null);
                if (next == IntPtr.Zero) break;
                worker = next;
                view = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view != IntPtr.Zero) break;
            }
        }
        var list = view == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
        if (list == IntPtr.Zero) throw new InvalidOperationException("恢复时找不到系统图标层。");
        ShowWindow(list, 5);
        if (!IsWindowVisible(list)) throw new InvalidOperationException("系统图标层恢复验证失败。");
    }
}

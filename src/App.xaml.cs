using System.IO;
using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace DeskSpaces;

public partial class App : System.Windows.Application
{
    private readonly LayoutStore _store = new(Path.Combine(AppContext.BaseDirectory, "Data"));
    private LayoutDocument _layout = LayoutStore.CreateDefault();
    private readonly DesktopCanvas _canvas = new();
    private SwitcherWindow? _switcher;
    private Forms.NotifyIcon? _tray;
    private NativeDesktop? _desktop;
    private SettingsWindow? _settings;
    private Mutex? _singleInstance;
    private bool _nativeMode;
    private bool _closing;
    private bool _switching;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--watchdog", StringComparer.OrdinalIgnoreCase))
        {
            var exitCode = NativeDesktopWatchdog.Run(e.Args.Skip(1).ToArray());
            Shutdown(exitCode);
            return;
        }
        _singleInstance = new Mutex(true, @"Local\DeskSpaces-D5E0D3FE-AEE0-4DB2-A34C-D3CCAE1AA4F5", out var first);
        if (!first)
        {
            MessageBox.Show("DeskSpaces 已在运行，请使用任务栏右下角的托盘图标。", "DeskSpaces", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }
        SessionEnding += (_, _) => RestoreSafely();
        DispatcherUnhandledException += (_, args) =>
        {
            RestoreSafely();
            MessageBox.Show($"DeskSpaces 已恢复原桌面。错误：{args.Exception.Message}", "DeskSpaces", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };
        try
        {
            _layout = _store.Load();
            _desktop = new NativeDesktop();
            _desktop.DesktopChanged += OnDesktopChanged;
            _desktop.Start();
            _canvas.Initialize(_layout, _store, () => _desktop.CurrentDesktopId);
            _canvas.Show();
            _switcher = new SwitcherWindow();
            _switcher.Initialize(_desktop, SwitchDesktop, OpenSettings, RestoreAndExit);
            _switcher.Show();
            BuildTray();
        }
        catch (Exception error)
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup-error.log"), error.ToString()); } catch { }
            RestoreSafely();
            MessageBox.Show($"DeskSpaces 无法启动：{error.Message}", "DeskSpaces", MessageBoxButton.OK, MessageBoxImage.Error);
            RestoreAndExit();
        }
    }

    private void OnDesktopChanged()
    {
        Dispatcher.Invoke(() =>
        {
            _canvas.SwitchDesktop(_desktop!.CurrentDesktopId);
            _switcher?.RefreshDesktops();
        });
    }

    private void SwitchDesktop(int direction)
    {
        if (_switching) return;
        _switching = true;
        try { _desktop?.SwitchRelative(direction); }
        finally { _switching = false; }
    }

    private void OpenSettings()
    {
        if (_settings is not null) { _settings.Activate(); return; }
        _settings = new SettingsWindow(_nativeMode, ApplyNativeMode, RestoreAndExit);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
    }

    private void ApplyNativeMode(bool enabled)
    {
        if (enabled && !_nativeMode)
        {
            var result = MessageBox.Show("接管模式会暂时隐藏 Windows 原图标，但不会移动或删除真实文件。\n\nDeskSpaces 将先启动独立恢复守护；主程序崩溃后会尝试恢复。是否继续？", "启用系统桌面接管", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }
        try
        {
            if (enabled)
            {
                _desktop!.Attach(_canvas);
                _nativeMode = true;
            }
            else
            {
                RestoreSafely();
                _nativeMode = false;
            }
        }
        catch (Exception error)
        {
            RestoreSafely();
            _nativeMode = false;
            MessageBox.Show($"未启用桌面接管，已恢复原状态。\n\n{error.Message}", "DeskSpaces", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RestoreSafely()
    {
        try { _desktop?.Detach(_canvas); }
        catch (Exception error)
        {
            try
            {
                var restore = Path.Combine(AppContext.BaseDirectory, "restore-icons.ps1");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{restore}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            catch { }
            MessageBox.Show($"自动恢复失败：{error.Message}\n请使用软件目录中的“恢复桌面.cmd”。", "DeskSpaces", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BuildTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("设置与恢复", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add("恢复原桌面并退出", null, (_, _) => Dispatcher.Invoke(RestoreAndExit));
        _tray = new Forms.NotifyIcon
        {
            Text = "DeskSpaces - 原生桌面图标工作区",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
    }

    private void RestoreAndExit()
    {
        if (_closing) return;
        _closing = true;
        RestoreSafely();
        try { _desktop?.Dispose(); } catch { }
        try { _canvas.SaveNow(); } catch { }
        try { _switcher?.Close(); } catch { }
        try { _settings?.Close(); } catch { }
        try { _canvas.Close(); } catch { }
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        Shutdown(0);
    }
}

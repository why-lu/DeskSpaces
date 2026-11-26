using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DeskSpaces;

public sealed record NativeDesktopInfo(Guid Id, string Name, int Index);
public sealed record DesktopFile(string Path, string Name, bool IsDirectory);

public sealed class NativeDesktop : IDisposable
{
    private const string DesktopRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private IntPtr _originalOwner;
    private IntPtr _iconList;
    private IntPtr _canvasHandle;
    private long _originalStyle;
    private long _originalExStyle;
    private RECT _originalRectangle;
    private bool _originalIconsVisible;
    private bool _attached;
    private bool _disposed;
    private string? _watchdogState;

    public Guid CurrentDesktopId { get; private set; }
    public IReadOnlyList<NativeDesktopInfo> Desktops { get; private set; } = [];
    public event Action? DesktopChanged;

    public void Start()
    {
        Refresh();
        _timer.Tick += (_, _) =>
        {
            try
            {
                var current = ReadCurrentDesktop();
                var desktops = ReadDesktopList();
                var changed = current != CurrentDesktopId || !desktops.Select(item => item.Id).SequenceEqual(Desktops.Select(item => item.Id)) ||
                              !desktops.Select(item => item.Name).SequenceEqual(Desktops.Select(item => item.Name));
                if (!changed) return;
                CurrentDesktopId = current;
                Desktops = desktops;
                DesktopChanged?.Invoke();
            }
            catch { /* 系统 Explorer 切换期间保留最后一次有效布局。 */ }
        };
        _timer.Start();
    }

    public void Refresh()
    {
        CurrentDesktopId = ReadCurrentDesktop();
        Desktops = ReadDesktopList();
    }

    public IReadOnlyList<DesktopFile> ReadDesktopFiles()
    {
        var result = new Dictionary<string, DesktopFile>(StringComparer.OrdinalIgnoreCase);
        var personal = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        foreach (var directory in new[] { personal, common }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.System) || attributes.HasFlag(FileAttributes.Hidden)) continue;
                var name = Path.GetFileName(path);
                if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                result[path] = new DesktopFile(path, name, attributes.HasFlag(FileAttributes.Directory));
            }
        }
        return result.Values.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void Attach(Window window)
    {
        if (_attached) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) throw new InvalidOperationException("图标画布尚未创建。");
        FindDesktopIconList(out var list, out var owner);
        if (owner == IntPtr.Zero || list == IntPtr.Zero) throw new InvalidOperationException("找不到 Windows 系统图标层。");
        _originalOwner = owner;
        _iconList = list;
        _canvasHandle = handle;
        _originalIconsVisible = IsWindowVisible(list);
        _originalStyle = GetWindowLongPtr(handle, -16).ToInt64();
        _originalExStyle = GetWindowLongPtr(handle, -20).ToInt64();
        GetWindowRect(handle, out _originalRectangle);
        StartWatchdog();
        _attached = true;
        try
        {
            // 先解除父子关系，切换 WS_CHILD 后再 SetParent；不能挂到 SysListView32 之下。
            var previousParent = SetParent(handle, IntPtr.Zero);
            if (previousParent == IntPtr.Zero && Marshal.GetLastWin32Error() != 0) throw LastError("无法准备桌面画布");
            var style = (_originalStyle & ~0x80000000L) | 0x40000000L; // WS_CHILD，去掉 WS_POPUP。
            SetWindowLongPtr(handle, -16, new IntPtr(style));
            SetWindowLongPtr(handle, -20, new IntPtr(_originalExStyle & ~0x00000080L)); // 去掉 WS_EX_TOOLWINDOW。
            if (SetParent(handle, owner) == IntPtr.Zero) throw LastError("无法嵌入系统桌面");
            GetClientRect(owner, out var rectangle);
            if (!SetWindowPos(handle, IntPtr.Zero, 0, 0, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top, 0x0040))
                throw LastError("无法调整图标画布");
            if (GetParent(handle) != owner) throw new InvalidOperationException("桌面嵌入验证失败。");
            ShowWindow(list, 0);
            if (IsWindowVisible(list)) throw new InvalidOperationException("系统原图标未能隐藏，已取消接管。");
        }
        catch
        {
            Detach(window);
            throw;
        }
    }

    public void Detach(Window window)
    {
        if (!_attached) return;
        Exception? failure = null;
        try
        {
            if (_canvasHandle != IntPtr.Zero && IsWindow(_canvasHandle))
            {
                SetParent(_canvasHandle, IntPtr.Zero);
                SetWindowLongPtr(_canvasHandle, -16, new IntPtr(_originalStyle));
                SetWindowLongPtr(_canvasHandle, -20, new IntPtr(_originalExStyle));
                SetWindowPos(_canvasHandle, IntPtr.Zero, _originalRectangle.Left, _originalRectangle.Top,
                    _originalRectangle.Right - _originalRectangle.Left, _originalRectangle.Bottom - _originalRectangle.Top, 0x0040);
            }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            RestoreIconVisibility();
            _attached = false;
            _iconList = IntPtr.Zero;
            _originalOwner = IntPtr.Zero;
            _canvasHandle = IntPtr.Zero;
        }
        if (failure is not null) throw failure;
    }

    private void RestoreIconVisibility()
    {
        if (_iconList != IntPtr.Zero && IsWindow(_iconList)) ShowWindow(_iconList, _originalIconsVisible ? 5 : 0);
        else
        {
            FindDesktopIconList(out _, out var current);
            if (current != IntPtr.Zero && _originalIconsVisible) ShowWindow(current, 5);
        }
        if (_watchdogState is not null)
        {
            try { File.WriteAllText(_watchdogState, JsonSerializer.Serialize(new { OriginalIconsVisible = false, Restored = true })); } catch { }
        }
    }

    private void StartWatchdog()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(data);
        _watchdogState = Path.Combine(data, "watchdog.json");
        File.WriteAllText(_watchdogState, JsonSerializer.Serialize(new { OriginalIconsVisible = _originalIconsVisible, Restored = false }));
        var watchdog = Path.Combine(AppContext.BaseDirectory, "watchdog.ps1");
        if (!File.Exists(watchdog)) throw new InvalidOperationException("找不到独立恢复守护脚本。");
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{watchdog}\" -ProcessId {Environment.ProcessId} -StatePath \"{_watchdogState}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (process is null || process.HasExited) throw new InvalidOperationException("恢复守护未能启动，已取消接管。");
    }

    public void SwitchRelative(int direction)
    {
        Refresh();
        if (Desktops.Count < 2) return;
        var index = Desktops.FirstOrDefault(item => item.Id == CurrentDesktopId)?.Index ?? 0;
        var next = (index + direction + Desktops.Count) % Desktops.Count;
        if (next == index) return;
        NativeDesktopSwitch.SwitchTo(Desktops[next].Id, Desktops.Count);
        Refresh();
        if (CurrentDesktopId != Desktops[next].Id)
            throw new InvalidOperationException("Windows 未完成桌面切换，请检查虚拟桌面适配状态。");
    }

    private static Guid ReadCurrentDesktop()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DesktopRegistryPath) ?? throw new InvalidOperationException("无法读取系统当前桌面。");
        var bytes = key.GetValue("CurrentVirtualDesktop") as byte[];
        if (bytes?.Length != 16) throw new InvalidOperationException("系统当前桌面标识无效。");
        return new Guid(bytes);
    }

    private static List<NativeDesktopInfo> ReadDesktopList()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DesktopRegistryPath) ?? throw new InvalidOperationException("无法读取系统桌面列表。");
        var bytes = key.GetValue("VirtualDesktopIDs") as byte[] ?? Array.Empty<byte>();
        var result = new List<NativeDesktopInfo>();
        for (var index = 0; index < bytes.Length / 16; index++)
        {
            var idBytes = new byte[16];
            Array.Copy(bytes, index * 16, idBytes, 0, 16);
            var id = new Guid(idBytes);
            using var desktopKey = Registry.CurrentUser.OpenSubKey($@"{DesktopRegistryPath}\Desktops\{id:B}");
            var name = desktopKey?.GetValue("Name") as string;
            result.Add(new NativeDesktopInfo(id, string.IsNullOrWhiteSpace(name) ? $"桌面 {index + 1}" : name, index));
        }
        return result;
    }

    private static void FindDesktopIconList(out IntPtr owner, out IntPtr list)
    {
        var progman = FindWindow("Progman", null);
        var view = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        owner = progman;
        if (view == IntPtr.Zero)
        {
            var worker = IntPtr.Zero;
            while (true)
            {
                var next = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null);
                if (next == IntPtr.Zero) break;
                worker = next;
                view = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view != IntPtr.Zero) { owner = worker; break; }
            }
        }
        list = view == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
        // owner 是承载图标层的 Progman / WorkerW 容器，不是图标列表自身。
    }

    public void Dispose()
    {
        if (_disposed) return;
        _timer.Stop();
        if (_attached) RestoreIconVisibility();
        _disposed = true;
    }

    private static Win32Exception LastError(string message) => new(Marshal.GetLastWin32Error(), message);

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out RECT rectangle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr window, out RECT rectangle);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
}

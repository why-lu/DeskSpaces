using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DeskSpaces;

public partial class DesktopCanvas : Window
{
    private readonly Dictionary<Guid, Border> _views = [];
    private readonly List<ClipboardEntry> _clipboard = [];
    private Guid? _selectedId;
    private readonly Dictionary<Guid, ImageSource> _icons = [];
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private LayoutDocument _layout = new();
    private LayoutStore? _store;
    private Func<Guid>? _getDesktop;
    private WorkspaceLayout? _workspace;
    private System.Windows.Point _dragOrigin;
    private System.Windows.Point _entryOrigin;
    private Border? _dragging;
    private bool _moved;
    private bool _cut;

    public DesktopCanvas()
    {
        InitializeComponent();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        Closed += (_, _) => SaveNow();
    }

    public void Initialize(LayoutDocument layout, LayoutStore store, Func<Guid> getDesktop)
    {
        _layout = layout;
        _store = store;
        _getDesktop = getDesktop;
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _layout.Workspaces.ToList().ForEach(ImportDesktopFiles);
                Render();
                SaveNow();
            }, DispatcherPriority.Loaded);
        };
        SizeChanged += (_, _) => { if (IsLoaded) Render(); };
        SwitchDesktop(getDesktop());
    }

    public void SwitchDesktop(Guid desktopId)
    {
        CancelDrag();
        SaveNow();
        if (_layout.Workspaces.Count == 0)
            _layout.Workspaces.Add(new WorkspaceLayout { Id = desktopId, Name = "桌面 1" });
        if (_layout.Workspaces.Count == 1 && _layout.Workspaces[0].Id != desktopId && _layout.Workspaces[0].Entries.Count == 0)
            _layout.Workspaces.Clear();
        _layout.ActiveWorkspaceId = desktopId;
        _workspace = _layout.Workspaces.FirstOrDefault(item => item.Id == desktopId);
        if (_workspace is null)
        {
            _workspace = new WorkspaceLayout { Id = desktopId, Name = $"桌面 {_layout.Workspaces.Count + 1}" };
            _layout.Workspaces.Add(_workspace);
            if (IsLoaded) ImportDesktopFiles(_workspace);
            if (IsLoaded) SaveNow();
        }
        Render();
    }

    public void SaveNow()
    {
        if (_store is null || _layout.Workspaces.Count == 0) return;
        try { _store.Save(_layout); }
        catch (Exception error) { Title = $"DeskSpaces - 保存失败：{error.Message}"; }
    }

    private void ImportDesktopFiles(WorkspaceLayout workspace)
    {
        _workspace = workspace;
        using var native = new NativeDesktop();
        var files = native.ReadDesktopFiles();
        var existing = _workspace.Entries.Select(item => item.TargetPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var column = 0;
        var row = 0;
        foreach (var file in files)
        {
            if (existing.Contains(file.Path)) continue;
            var entry = new DesktopEntry
            {
                TargetPath = file.Path,
                DisplayName = file.Name,
                Kind = file.IsDirectory ? "Folder" : "File",
                X = 18 + column * 86,
                Y = 70 + row * 92,
            };
            entry.X = Math.Min(entry.X, CanvasWidth);
            entry.Y = Math.Min(entry.Y, CanvasHeight);
            entry.Normalize(CanvasWidth, CanvasHeight);
            _workspace.Entries.Add(entry);
            row++;
            if (row >= 7) { row = 0; column++; }
        }
    }

    private void Render()
    {
        CancelDrag();
        IconCanvas.Children.Clear();
        _views.Clear();
        _selectedId = null;
        DesktopLabel.Text = _workspace?.Name ?? "当前桌面";
        if (_workspace is null) return;
        var width = CanvasWidth;
        var height = CanvasHeight;
        foreach (var entry in _workspace.Entries)
        {
            entry.Denormalize(width, height);
            var view = CreateView(entry);
            _views[entry.Id] = view;
            IconCanvas.Children.Add(view);
            Canvas.SetLeft(view, entry.X);
            Canvas.SetTop(view, entry.Y);
        }
    }

    private double CanvasWidth => Math.Max(1, IconCanvas.ActualWidth > 0 ? IconCanvas.ActualWidth - 78 : ActualWidth - 86);
    private double CanvasHeight => Math.Max(1, IconCanvas.ActualHeight > 0 ? IconCanvas.ActualHeight - 84 : ActualHeight - 92);

    private void CancelDrag()
    {
        if (_dragging is not null && _dragging.IsMouseCaptured) _dragging.ReleaseMouseCapture();
        _dragging = null;
        _moved = false;
    }

    private Border CreateView(DesktopEntry entry)
    {
        var image = new System.Windows.Controls.Image { Width = 38, Height = 38, Stretch = Stretch.Uniform, Source = GetIcon(entry) };
        var label = new TextBlock
        {
            Text = entry.DisplayName,
            Foreground = Brushes.White,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 74,
            MaxHeight = 35,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 5, 0, 0),
        };
        var content = new StackPanel { Children = { image, label } };
        var border = new Border
        {
            Width = 78,
            Height = 84,
            Child = content,
            Background = Brushes.Transparent,
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(5),
            Tag = entry.Id,
            Cursor = Cursors.Hand,
            ToolTip = entry.TargetPath,
        };
        border.MouseLeftButtonDown += OnEntryMouseDown;
        border.MouseMove += OnEntryMouseMove;
        border.MouseLeftButtonUp += OnEntryMouseUp;
        border.MouseRightButtonUp += (_, _) => ShowEntryMenu(entry, border);
        border.AllowDrop = true;
        return border;
    }

    private ImageSource? GetIcon(DesktopEntry entry)
    {
        if (_icons.TryGetValue(entry.Id, out var cached)) return cached;
        try
        {
            var path = entry.CustomIconPath ?? entry.TargetPath;
            if (!File.Exists(path) && !Directory.Exists(path)) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            _icons[entry.Id] = source;
            return source;
        }
        catch { return null; }
    }

    private void OnEntryMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not Guid id) return;
        if (e.ClickCount == 2)
        {
            OpenEntry(id);
            e.Handled = true;
            return;
        }
        _selectedId = id;
        UpdateSelection();
        _dragging = border;
        _dragOrigin = e.GetPosition(IconCanvas);
        _entryOrigin = new System.Windows.Point(Canvas.GetLeft(border), Canvas.GetTop(border));
        _moved = false;
        border.CaptureMouse();
        e.Handled = true;
    }

    private void OnEntryMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragging is null || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(IconCanvas);
        if (!_moved && (current - _dragOrigin).Length < 4) return;
        _moved = true;
        var x = Math.Clamp(_entryOrigin.X + current.X - _dragOrigin.X, 0, CanvasWidth);
        var y = Math.Clamp(_entryOrigin.Y + current.Y - _dragOrigin.Y, 0, CanvasHeight);
        Canvas.SetLeft(_dragging, x);
        Canvas.SetTop(_dragging, y);
    }

    private void OnEntryMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging is null) return;
        _dragging.ReleaseMouseCapture();
        if (_moved && _workspace is not null && _dragging.Tag is Guid id)
        {
            var entry = _workspace.Entries.SingleOrDefault(item => item.Id == id);
            if (entry is null) { CancelDrag(); return; }
            entry.X = Canvas.GetLeft(_dragging);
            entry.Y = Canvas.GetTop(_dragging);
            entry.Normalize(CanvasWidth, CanvasHeight);
            _saveTimer.Stop();
            _saveTimer.Start();
        }
        _dragging = null;
        _moved = false;
    }

    private void UpdateSelection()
    {
        foreach (var pair in _views)
            pair.Value.Background = pair.Key == _selectedId
                ? new SolidColorBrush(Color.FromArgb(75, 109, 159, 219))
                : Brushes.Transparent;
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_workspace is null) return;
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopySelected(false);
            e.Handled = true;
        }
        else if (e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopySelected(true);
            e.Handled = true;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && _getDesktop is not null)
        {
            Paste();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _selectedId is Guid id)
        {
            RemoveEntry(id);
            e.Handled = true;
        }
    }

    private void CopySelected(bool cut)
    {
        _clipboard.Clear();
        if (_selectedId is Guid id && _workspace is not null)
        {
            var entry = _workspace.Entries.Single(item => item.Id == id);
            _clipboard.Add(new ClipboardEntry(_workspace.Id, id, entry.Clone()));
        }
        _cut = cut;
    }

    private void Paste()
    {
        if (_workspace is null) return;
        foreach (var item in _clipboard)
        {
            if (_workspace.Id == item.SourceWorkspaceId && _cut) continue;
            var copy = item.Entry.Clone();
            copy.Denormalize(CanvasWidth, CanvasHeight);
            copy.X = Math.Clamp(copy.X + 20, 0, CanvasWidth);
            copy.Y = Math.Clamp(copy.Y + 20, 0, CanvasHeight);
            copy.Normalize(CanvasWidth, CanvasHeight);
            _workspace.Entries.Add(copy);
            if (_cut)
            {
                var source = _layout.Workspaces.FirstOrDefault(workspace => workspace.Id == item.SourceWorkspaceId);
                source?.Entries.RemoveAll(entry => entry.Id == item.OriginalEntryId);
            }
        }
        _cut = false;
        SaveNow();
        Render();
    }

    private void RemoveEntry(Guid id)
    {
        if (_workspace is null) return;
        if (MessageBox.Show("仅从当前工作区移除图标入口，不删除真实文件。继续吗？", "DeskSpaces", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _workspace.Entries.RemoveAll(item => item.Id == id);
        SaveNow();
        Render();
    }

    private void ShowEntryMenu(DesktopEntry entry, Border view)
    {
        _selectedId = entry.Id;
        UpdateSelection();
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "打开" };
        open.Click += (_, _) => OpenEntry(entry.Id);
        var copy = new MenuItem { Header = "复制入口" };
        copy.Click += (_, _) => { _selectedId = entry.Id; CopySelected(false); };
        var location = new MenuItem { Header = "打开文件所在位置" };
        location.Click += (_, _) => OpenLocation(entry);
        var remove = new MenuItem { Header = "从当前工作区移除（不删除文件）" };
        remove.Click += (_, _) => RemoveEntry(entry.Id);
        menu.Items.Add(open);
        menu.Items.Add(copy);
        menu.Items.Add(location);
        menu.Items.Add(new Separator());
        menu.Items.Add(remove);
        menu.IsOpen = true;
    }

    private void OpenEntry(Guid id)
    {
        var entry = _workspace?.Entries.FirstOrDefault(item => item.Id == id);
        if (entry is null) return;
        try
        {
            if (!File.Exists(entry.TargetPath) && !Directory.Exists(entry.TargetPath))
                throw new FileNotFoundException("目标文件或文件夹不存在。", entry.TargetPath);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = entry.TargetPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(entry.TargetPath) ?? "",
            });
        }
        catch (Exception error) { MessageBox.Show($"无法打开目标：{error.Message}", "DeskSpaces"); }
    }

    private static void OpenLocation(DesktopEntry entry)
    {
        try
        {
            var directory = Directory.Exists(entry.TargetPath) ? entry.TargetPath : Path.GetDirectoryName(entry.TargetPath);
            if (directory is not null) System.Diagnostics.Process.Start("explorer.exe", $"\"{directory}\"");
        }
        catch (Exception error) { MessageBox.Show(error.Message, "DeskSpaces"); }
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        _selectedId = null;
        UpdateSelection();
        Focus();
    }
}

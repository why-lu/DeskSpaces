using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace DeskSpaces;

public partial class SwitcherWindow : Window
{
    private NativeDesktop? _desktop;
    private Action<int>? _switch;
    private Action? _settings;
    private Action? _exit;

    public SwitcherWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PositionAtBottom();
    }

    public void Initialize(NativeDesktop desktop, Action<int> switchDesktop, Action settings, Action exit)
    {
        _desktop = desktop;
        _switch = switchDesktop;
        _settings = settings;
        _exit = exit;
        RefreshDesktops();
    }

    public void RefreshDesktops()
    {
        if (_desktop is null) return;
        DesktopButtons.Children.Clear();
        foreach (var desktop in _desktop.Desktops)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = desktop.Name,
                Background = desktop.Id == _desktop.CurrentDesktopId ? new SolidColorBrush(Color.FromRgb(69, 110, 164)) : Brushes.Transparent,
                Margin = new Thickness(2, 0, 0, 0),
                Tag = desktop.Index,
            };
            button.Click += (_, _) =>
            {
                var current = _desktop.Desktops.FirstOrDefault(item => item.Id == _desktop.CurrentDesktopId)?.Index ?? 0;
                var direction = desktop.Index > current ? 1 : -1;
                for (var index = current; index != desktop.Index; index += direction) _switch?.Invoke(direction);
            };
            DesktopButtons.Children.Add(button);
        }
    }

    private void PositionAtBottom()
    {
        var area = Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
        var source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(area.Left, area.Top));
        var bottomRight = transform.Transform(new Point(area.Right, area.Bottom));
        Left = topLeft.X + (bottomRight.X - topLeft.X - Width) / 2;
        Top = bottomRight.Y - Height - 14;
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("请使用 Ctrl + Win + D 新建系统桌面。DeskSpaces 会在切换后自动建立独立图标布局。", "DeskSpaces", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnSettings(object sender, RoutedEventArgs e) => _settings?.Invoke();
    private void OnExit(object sender, RoutedEventArgs e) => _exit?.Invoke();
}

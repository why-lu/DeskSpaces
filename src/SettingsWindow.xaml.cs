using System.Windows;

namespace DeskSpaces;

public partial class SettingsWindow : Window
{
    private readonly Action<bool> _apply;
    private readonly Action _exit;

    public SettingsWindow(bool nativeMode, Action<bool> apply, Action exit)
    {
        InitializeComponent();
        _apply = apply;
        _exit = exit;
        NativeModeCheck.IsChecked = nativeMode;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        _apply(NativeModeCheck.IsChecked == true);
        Close();
    }

    private void OnExit(object sender, RoutedEventArgs e)
    {
        Close();
        _exit();
    }
}

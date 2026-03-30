using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QMan.App;

internal static class TextInputDialog
{
    public static string? Show(Window owner, string title, string prompt, string initial = "")
    {
        var win = new Window
        {
            Title = title,
            Width = 420,
            Height = 168,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush?)Application.Current.TryFindResource("BrushSurface")
                ?? new SolidColorBrush(Color.FromRgb(0x12, 0x15, 0x1E)),
            Foreground = (Brush?)Application.Current.TryFindResource("BrushText")
                ?? Brushes.WhiteSmoke,
            FontFamily = new FontFamily("Segoe UI")
        };

        var box = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 8) };
        var ok = new Button { Content = "확인", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "취소", Width = 80, IsCancel = true };

        string? result = null;
        ok.Click += (_, _) =>
        {
            result = box.Text;
            win.DialogResult = true;
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { ok, cancel }
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var label = new TextBlock
        {
            Text = prompt,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = (Brush?)Application.Current.TryFindResource("BrushMuted")
                ?? new SolidColorBrush(Color.FromRgb(0x8B, 0x93, 0xA7))
        };
        DockPanel.SetDock(label, Dock.Top);

        win.Content = new DockPanel
        {
            Margin = new Thickness(12),
            Children = { buttons, label, box }
        };

        return win.ShowDialog() == true ? result : null;
    }
}

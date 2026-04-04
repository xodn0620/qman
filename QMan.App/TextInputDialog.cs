using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace QMan.App;

internal static class TextInputDialog
{
    public static string? Show(Window owner, string title, string prompt, string initial = "")
    {
        var tabBar = (Brush?)Application.Current.TryFindResource("BrushTabBar")
                     ?? new SolidColorBrush(Color.FromRgb(0x00, 0x39, 0x78));
        var surface = (Brush?)Application.Current.TryFindResource("BrushSurface")
                      ?? Brushes.White;
        var ink = (Brush?)Application.Current.TryFindResource("BrushText")
                  ?? new SolidColorBrush(Color.FromRgb(0x14, 0x2B, 0x45));
        var muted = (Brush?)Application.Current.TryFindResource("BrushMuted")
                    ?? new SolidColorBrush(Color.FromRgb(0x5A, 0x6D, 0x85));
        var paper = (Brush?)Application.Current.TryFindResource("BrushElevated")
                    ?? Brushes.White;
        var line = (Brush?)Application.Current.TryFindResource("BrushBorder")
                   ?? new SolidColorBrush(Color.FromRgb(0xC5, 0xD4, 0xE8));
        var accentStyle = Application.Current.TryFindResource("AccentButton") as Style;

        var win = new Window
        {
            Owner = owner,
            Title = title,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            FontFamily = new FontFamily("Segoe UI"),
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 440,
            MinHeight = 268
        };

        var box = new TextBox
        {
            Style = null,
            Text = initial,
            MinHeight = 44,
            FontSize = 14,
            Foreground = ink,
            Background = paper,
            CaretBrush = ink,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            VerticalContentAlignment = VerticalAlignment.Center
        };

        var ok = new Button
        {
            Content = "확인",
            MinWidth = 96,
            Height = 40,
            Padding = new Thickness(20, 0, 20, 0),
            IsDefault = true,
            Margin = new Thickness(0, 0, 10, 0)
        };
        if (accentStyle != null)
            ok.Style = accentStyle;

        var cancel = new Button
        {
            Content = "취소",
            MinWidth = 96,
            Height = 40,
            Padding = new Thickness(20, 0, 20, 0),
            IsCancel = true
        };

        string? result = null;
        ok.Click += (_, _) =>
        {
            result = box.Text;
            win.DialogResult = true;
        };

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 40,
            Height = 40,
            Margin = new Thickness(0, 0, 10, 0),
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 16,
            Cursor = Cursors.Hand,
            ToolTip = "닫기",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        closeBtn.Click += (_, _) => { win.DialogResult = false; };

        var titleBlock = new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        void StartDrag(object _, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    win.DragMove();
                }
                catch { }
            }
        }

        var titleDragArea = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(18, 0, 8, 0),
            Cursor = Cursors.SizeAll,
            Child = titleBlock,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        titleDragArea.MouseLeftButtonDown += StartDrag;

        var header = new DockPanel
        {
            Height = 50,
            Background = tabBar,
            LastChildFill = true
        };
        DockPanel.SetDock(closeBtn, Dock.Right);
        header.Children.Add(closeBtn);
        header.Children.Add(titleDragArea);

        var promptBlock = new TextBlock
        {
            Text = prompt,
            Foreground = muted,
            FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0),
            Children = { ok, cancel }
        };

        var body = new StackPanel
        {
            Margin = new Thickness(22, 18, 22, 22),
            Children =
            {
                promptBlock,
                box,
                buttonRow
            }
        };
        box.Margin = new Thickness(0, 12, 0, 0);

        var innerCard = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            Background = surface,
            // CornerRadius + ClipToBounds 조합이 헤더 오른쪽 닫기 버튼을 둥근 모서리에서 잘라냄
            ClipToBounds = false,
            Child = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto }
                },
                Children = { header, body }
            }
        };
        Grid.SetRow(header, 0);
        Grid.SetRow(body, 1);

        var outer = new Border
        {
            Margin = new Thickness(28),
            Background = Brushes.Transparent,
            Child = innerCard,
            Effect = new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 3,
                Direction = 270,
                Opacity = 0.22,
                Color = Color.FromRgb(0, 0x28, 0x58)
            }
        };

        win.Content = outer;

        win.Loaded += (_, _) =>
        {
            box.Focus();
            try
            {
                box.SelectAll();
            }
            catch { }
        };

        return win.ShowDialog() == true ? result : null;
    }
}

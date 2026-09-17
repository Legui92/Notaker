using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Notaker;

internal sealed class DictationOverlay : Window
{
    private readonly TextBlock label = new() { Foreground = Brushes.White, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar meter = new() { Width = 42, Height = 5, Maximum = 1, Margin = new Thickness(0, 0, 16, 0), Foreground = new SolidColorBrush(Color.FromRgb(190, 233, 122)) };
    internal DictationOverlay()
    {
        Width = 370; Height = 64; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        var stack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(meter); stack.Children.Add(label);
        Content = new Border { Background = new SolidColorBrush(Color.FromRgb(28, 35, 32)), CornerRadius = new CornerRadius(22), Padding = new Thickness(20, 10, 20, 10), Child = stack, BorderBrush = new SolidColorBrush(Color.FromRgb(80, 91, 80)), BorderThickness = new Thickness(1) };
        SourceInitialized += (_, _) => Native.NoActivate(this);
    }
    internal void Update(string text, float level = 0, bool busy = false)
    {
        label.Text = text; meter.IsIndeterminate = busy; meter.Value = Math.Min(1, level * 8);
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2; Top = area.Bottom - Height - 24;
        if (!IsVisible) Show();
    }
}

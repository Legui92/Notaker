using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Notaker;

internal sealed class DictationOverlay : Window
{
    private readonly TextBlock label = new() { Foreground = Brushes.White, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock clock = new() { Foreground = Brushes.White, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10,0,8,0) };
    private readonly Button cancel = new() { Content = "×", Width = 30, Height = 30, Padding = new Thickness(0), Focusable = false, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), ToolTip = "Cancelar dictado sin pegar texto" };
    internal event Action? CancelRequested;
    private readonly ProgressBar meter = new() { Width = 42, Height = 5, Maximum = 1, Margin = new Thickness(0, 0, 16, 0), Foreground = new SolidColorBrush(Color.FromRgb(151, 117, 250)) };
    internal DictationOverlay()
    {
        Width = 280; Height = 44; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        var stack = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(cancel, Dock.Right); stack.Children.Add(cancel);
        DockPanel.SetDock(clock, Dock.Right); stack.Children.Add(clock);
        DockPanel.SetDock(meter, Dock.Left); stack.Children.Add(meter); stack.Children.Add(label);
        meter.Width = 28; meter.Margin = new Thickness(0,0,10,0);
        System.Windows.Automation.AutomationProperties.SetName(cancel, "Cancelar dictado sin pegar texto");
        cancel.Click += (_, _) => CancelRequested?.Invoke();
        Content = new Border { Background = new SolidColorBrush(Color.FromRgb(24, 22, 45)), CornerRadius = new CornerRadius(22), Padding = new Thickness(14, 5, 6, 5), Child = stack, BorderBrush = new SolidColorBrush(Color.FromRgb(83, 69, 130)), BorderThickness = new Thickness(1) };
        SourceInitialized += (_, _) => {
            Native.NoActivate(this);
            System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle).AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) => { if (msg == 0x21) { handled = true; return new IntPtr(3); } return IntPtr.Zero; });
        };
    }
    internal void Update(string text, float level = 0, bool busy = false, bool canCancel = true)
    {
        var listening = text.StartsWith("Escuchando · ", StringComparison.Ordinal);
        Width = listening ? 244 : 280;
        label.Text = listening ? "Escuchando" : text;
        clock.Text = listening ? "● " + text[13..] : "";
        clock.Visibility = listening ? Visibility.Visible : Visibility.Collapsed;
        cancel.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
        meter.IsIndeterminate = busy; meter.Value = Math.Min(1, level * 8);
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2; Top = area.Bottom - Height - 24;
        if (!IsVisible) Show();
    }
}

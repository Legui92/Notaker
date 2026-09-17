using System.Windows;
using System.Windows.Controls;

namespace Notaker;

internal sealed class CorrectionWindow : Window
{
    private readonly TextBox editor;
    internal string Corrected => editor.Text.Trim();
    internal CorrectionWindow(Dictation entry, bool learning)
    {
        Title = "Corregir dictado · Notaker"; Width = 640; Height = 520;
        MinWidth = 480; MinHeight = 380; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(247, 248, 243));
        var panel = new DockPanel { Margin = new Thickness(24) };
        var header = new TextBlock { Text = learning ? "Corrige el texto. Las palabras nuevas se aprenderán para próximos dictados." : "Corrige el texto. El aprendizaje de vocabulario está desactivado.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var save = new Button { Content = "Guardar corrección", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(editor!.Text)) DialogResult = true; };
        DockPanel.SetDock(save, Dock.Bottom); panel.Children.Add(save);
        if (entry.OriginalText != null)
        {
            var original = new Expander { Header = "Ver transcripción original", Margin = new Thickness(0, 10, 0, 0), Content = new TextBox { Text = entry.OriginalText, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            DockPanel.SetDock(original, Dock.Bottom); panel.Children.Add(original);
        }
        editor = new TextBox { Text = entry.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12), FontSize = 15 };
        panel.Children.Add(editor); Content = panel;
    }
}

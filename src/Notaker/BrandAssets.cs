using System.Windows.Media.Imaging;

namespace Notaker;

public static class BrandAssets
{
    // Keep the decoder's complete frame collection. BitmapImage flattens ICO to 16px,
    // which WPF centers without scaling inside the large taskbar icon.
    public static BitmapFrame WindowIcon() => BitmapFrame.Create(new Uri("pack://application:,,,/Notaker;component/Assets/notaker.ico"));
}

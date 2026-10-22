using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace FluxPlayer.Windows;

internal static class ToolWindowChrome
{
    public static UIElement Wrap(FluentWindow window, UIElement content)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var title = new TitleBar
        {
            Title = window.Title,
            Height = 36,
            ShowClose = true,
            ShowMaximize = window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip,
            ShowMinimize = window.ResizeMode != ResizeMode.NoResize
        };
        Grid.SetRow(content, 1); root.Children.Add(title); root.Children.Add(content);
        return root;
    }
}

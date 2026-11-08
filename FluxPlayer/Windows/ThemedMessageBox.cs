using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using MessageBoxImage = System.Windows.MessageBoxImage;
using FluxPlayer.Models;
using FluxPlayer.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

// This type lives in the FluxPlayer namespace on purpose: inside FluxPlayer.* the simple name "MessageBox" resolves here before it
// reaches System.Windows.MessageBox, so every existing MessageBox.Show call gets the themed dialog without being rewritten.
namespace FluxPlayer
{

/// <summary>
/// A drop-in replacement for <see cref="System.Windows.MessageBox"/> that looks like a Windows 11 content dialog: it follows the
/// player theme (Mica, dark / light, accent colour), is translated with the interface language, uses the Fluent icon font and
/// puts the default action in the accent button. The classic Win32 box is always white and ignores the dark theme.
/// </summary>
public static class MessageBox
{
    public static MessageBoxResult Show(string text)
        => Show(null, text, "FluxPlayer", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);

    public static MessageBoxResult Show(string text, string caption, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
        => Show(null, text, caption, button, icon, defaultResult);

    public static MessageBoxResult Show(Window? owner, string text, string caption, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return MessageBoxResult.None;
        if (!dispatcher.CheckAccess())
            return dispatcher.Invoke(() => Show(owner, text, caption, button, icon, defaultResult));
        return new Windows.MessageDialog(owner, text, caption, button, icon, defaultResult).ShowAndWait();
    }
}

namespace Windows
{
    internal sealed class MessageDialog : FluentWindow
    {
        private MessageBoxResult _result = MessageBoxResult.None;
        private readonly MessageBoxResult _dismissResult;

        public MessageDialog(Window? owner, string text, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
        {
            Title = Localizer.Rich(string.IsNullOrWhiteSpace(caption) ? "FluxPlayer" : caption);
            Width = 460; MinWidth = 360; MaxWidth = 600;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = owner == null;
            WindowBackdropType = WindowBackdropType.Mica;
            ExtendsContentIntoTitleBar = true;
            Background = Brushes.Transparent;
            if (owner is { IsLoaded: true, IsVisible: true }) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else { WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true; }

            var buttons = ButtonsFor(button);
            _dismissResult = button switch
            {
                MessageBoxButton.OK => MessageBoxResult.OK,
                MessageBoxButton.YesNo => MessageBoxResult.No,
                _ => MessageBoxResult.Cancel
            };
            var defaultButton = buttons.Contains(defaultResult) ? defaultResult : buttons[0];

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(new TitleBar { Title = Title, Height = 36, ShowClose = true, ShowMaximize = false, ShowMinimize = false });

            var body = new Grid { Margin = new Thickness(24, 12, 24, 22) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var (glyph, brushKey) = IconFor(icon);
            if (glyph != null)
            {
                body.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 28,
                    Foreground = ResourceBrush(brushKey), Margin = new Thickness(0, 2, 18, 0), VerticalAlignment = VerticalAlignment.Top
                });
            }
            var message = new System.Windows.Controls.TextBlock
            {
                Text = Localizer.Rich(text), TextWrapping = TextWrapping.Wrap, FontSize = 14,
                Foreground = ResourceBrush("TextFillColorPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(message, 1);
            body.Children.Add(message);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var footer = new Border { Background = ResourceBrush("FxFooterBrush"), Padding = new Thickness(24, 14, 24, 16) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var result in buttons)
            {
                bool primary = result == defaultButton;
                var control = new System.Windows.Controls.Button
                {
                    Content = Localizer.T(LabelFor(result)),
                    MinWidth = 96, Height = 34, Margin = new Thickness(row.Children.Count == 0 ? 0 : 8, 0, 0, 0),
                    IsDefault = primary, IsCancel = result == _dismissResult && buttons.Length > 1,
                    Style = (Style?)Application.Current.TryFindResource(primary ? "AccentButton" : "ChipButton"),
                    Focusable = true
                };
                if (!primary) { control.Foreground = ResourceBrush("TextFillColorPrimaryBrush"); control.Background = ResourceBrush("ControlFillColorDefaultBrush"); control.BorderBrush = ResourceBrush("ControlStrokeColorDefaultBrush"); }
                var captured = result;
                control.Click += (_, _) => { _result = captured; Close(); };
                row.Children.Add(control);
                if (primary) Loaded += (_, _) => control.Focus();
            }
            footer.Child = row;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            Content = root;

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { _result = _dismissResult; e.Handled = true; Close(); }
            };
            SourceInitialized += (_, _) =>
            {
                var theme = Application.Current.MainWindow is { } main ? ThemeService.GetTheme(main) : AppTheme.DarkMica;
                ThemeService.Apply(theme, this);
            };
            Closing += (_, _) => { if (_result == MessageBoxResult.None) _result = _dismissResult; };
        }

        public MessageBoxResult ShowAndWait()
        {
            switch (_dismissResult)
            {
                case MessageBoxResult.No: case MessageBoxResult.Cancel: System.Media.SystemSounds.Exclamation.Play(); break;
            }
            ShowDialog();
            return _result;
        }

        private static MessageBoxResult[] ButtonsFor(MessageBoxButton button) => button switch
        {
            MessageBoxButton.OKCancel => [MessageBoxResult.OK, MessageBoxResult.Cancel],
            MessageBoxButton.YesNo => [MessageBoxResult.Yes, MessageBoxResult.No],
            MessageBoxButton.YesNoCancel => [MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel],
            _ => [MessageBoxResult.OK]
        };

        private static string LabelFor(MessageBoxResult result) => result switch
        {
            MessageBoxResult.Yes => "Yes",
            MessageBoxResult.No => "No",
            MessageBoxResult.Cancel => "Cancel",
            _ => "OK"
        };

        // Segoe Fluent Icons: Info, Warning, Error badge and Help.
        private static (string? Glyph, string Brush) IconFor(MessageBoxImage icon) => icon switch
        {
            MessageBoxImage.Information => ("", "FxAccentTextBrush"),
            MessageBoxImage.Warning => ("", "FxWarnTextBrush"),
            MessageBoxImage.Error => ("", "FxDangerTextBrush"),
            MessageBoxImage.Question => ("", "FxAccentTextBrush"),
            _ => (null, "TextFillColorPrimaryBrush")
        };

        private static Brush ResourceBrush(string key) => Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
}
}

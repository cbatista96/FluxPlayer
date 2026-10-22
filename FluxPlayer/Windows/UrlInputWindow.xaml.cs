using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace FluxPlayer.Windows;

public partial class UrlInputWindow : FluentWindow
{
    private static readonly HashSet<string> SupportedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "rtsp", "rtsps", "rtmp", "rtmps", "rtp", "udp", "mms", "mmsh",
        "ftp", "ftps", "sftp", "smb", "srt", "hls"
    };

    public string? Url { get; private set; }

    public UrlInputWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {

            try
            {
                if (Clipboard.ContainsText())
                {
                    string clip = Clipboard.GetText().Trim();
                    if (IsValidUrl(clip, out _)) UrlTextBox.Text = clip;
                }
            }
            catch { }

            UrlTextBox.Focus();
            UrlTextBox.SelectAll();
        };
    }

    private static bool IsValidUrl(string text, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !text.Contains("://"))
        {
            error = "Include the protocol in the address, for example https://";
            return false;
        }
        if (!SupportedSchemes.Contains(uri.Scheme))
        {
            error = $"Protocol \"{uri.Scheme}\" is not supported.";
            return false;
        }
        return true;
    }

    private void UrlTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        string text = UrlTextBox.Text.Trim();
        bool ok = IsValidUrl(text, out string? error);
        OkBtn.IsEnabled = ok;
        ValidationText.Text = error ?? "";
        ValidationText.Visibility = !ok && error != null && text.Length > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        string text = UrlTextBox.Text.Trim();
        if (!IsValidUrl(text, out _)) return;
        Url = text;
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void PasteBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText()) UrlTextBox.Text = Clipboard.GetText().Trim();
        }
        catch { }
        UrlTextBox.Focus();
        UrlTextBox.CaretIndex = UrlTextBox.Text.Length;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OkBtn_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelBtn_Click(sender, e);
            e.Handled = true;
        }
    }
}

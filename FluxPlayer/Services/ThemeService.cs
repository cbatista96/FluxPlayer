using FluxPlayer.Models;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FluxPlayer.Services;

public static class ThemeService
{
    private static Color? _customAccent;
    private static bool _accentResourcesOverridden;
    public static void SetCustomAccent(string hex) { if (hex == "" && _customAccent == null) return; _customAccent = hex == "" ? null : (Color)ColorConverter.ConvertFromString(hex); ApplyDialogPalette(_lastWpfUiTheme == ApplicationTheme.Light); }
    public static AppTheme GetTheme(Window window) =>
        _windowThemes.TryGetValue(window, out var box) ? box.Theme : AppTheme.DarkMica;
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void Apply(AppTheme theme, Window window)
    {

        var appTheme = theme == AppTheme.LightMica
            ? ApplicationTheme.Light
            : ApplicationTheme.Dark;
        if (_lastWpfUiTheme != appTheme)
        {
            _lastWpfUiTheme = appTheme;
            ApplicationThemeManager.Apply(appTheme, BackdropFor(theme));
        }
        ApplyDialogPalette(theme == AppTheme.LightMica);

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int useDark = theme == AppTheme.LightMica ? 0 : 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }

        if (window is FluentWindow fw)
        {
            fw.ExtendsContentIntoTitleBar = true;
            fw.WindowBackdropType = BackdropFor(theme);
        }

        _windowThemes.AddOrUpdate(window, new ThemeBox(theme));
        var state = _frameState.GetValue(window, w => new FrameState { Active = w.IsActive });
        if (!state.Hooked && hwnd != IntPtr.Zero && HwndSource.FromHwnd(hwnd) is { } source)
        {

            state.Hooked = true;
            source.AddHook((IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == WM_NCACTIVATE)
                {
                    bool active = wParam != IntPtr.Zero;
                    if (active != state.Active)
                    {
                        state.Active = active;
                        ApplyBackground(window);
                    }
                }
                return IntPtr.Zero;
            });
        }
        ApplyBackground(window);
    }

    private const int WM_NCACTIVATE = 0x0086;
    private static ApplicationTheme? _lastWpfUiTheme;

    private static WindowBackdropType BackdropFor(AppTheme theme) => theme switch
    {
        AppTheme.AcrylicBlur => WindowBackdropType.Acrylic,

        AppTheme.LightMica => WindowBackdropType.Tabbed,
        AppTheme.FullBlack => WindowBackdropType.None,
        _ => WindowBackdropType.Mica
    };

    private sealed record ThemeBox(AppTheme Theme);
    private sealed class FrameState { public bool Active; public bool Hooked; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, ThemeBox> _windowThemes = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, FrameState> _frameState = new();

    private static readonly SolidColorBrush BlackBrush = Freeze(new SolidColorBrush(Color.FromRgb(0, 0, 0)));

    private static readonly SolidColorBrush LightInactiveBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xEE, 0xF0, 0xF4)));
    private static readonly SolidColorBrush DarkInactiveBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x20, 0x22, 0x28)));

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    private static double Luminance(Color color)
    {
        static double Linear(byte component) { double value = component / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
    private static double Contrast(Color first, Color second)
    { double a = Luminance(first), b = Luminance(second); return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05); }
    private static Color ReadableAccent(Color accent, Color background)
    {
        if (Contrast(accent, background) >= 4.5) return accent;
        Color target = Contrast(Colors.White, background) >= Contrast(Colors.Black, background) ? Colors.White : Colors.Black;
        Color Blend(double amount) => Color.FromRgb((byte)Math.Round(accent.R + (target.R - accent.R) * amount), (byte)Math.Round(accent.G + (target.G - accent.G) * amount), (byte)Math.Round(accent.B + (target.B - accent.B) * amount));
        double low = 0, high = 1; for (int i = 0; i < 20; i++) { double middle = (low + high) / 2; if (Contrast(Blend(middle), background) >= 4.5) high = middle; else low = middle; }
        return Blend(high);
    }

    private static void ApplyBackground(Window window)
    {
        if (!_windowThemes.TryGetValue(window, out var box)) return;
        bool active = !_frameState.TryGetValue(window, out var st) || st.Active;

        if (box.Theme != AppTheme.FullBlack && window is FluentWindow { WindowBackdropType: WindowBackdropType.None })
            return;
        window.Background = box.Theme switch
        {
            AppTheme.FullBlack => BlackBrush,
            AppTheme.LightMica when !active => LightInactiveBrush,
            AppTheme.AcrylicBlur when !active => DarkInactiveBrush,

            _ => Brushes.Transparent
        };
    }

    public static void ApplyDialogPalette(bool light)
    {
        var res = Application.Current?.Resources;
        if (res == null) return;

        void Set(string name, uint dark, uint lightArgb)
        {
            uint v = light ? lightArgb : dark;
            var c = Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            res[name + "Color"] = c;
            res[name + "Brush"] = brush;
        }

        Set("FxSurface", 0xB8151821, 0xD8F5F6F9);
        Set("FxSidebar", 0x0AFFFFFF, 0x07000000);
        Set("FxCard", 0x0DFFFFFF, 0xC8FFFFFF);
        Set("FxCardBorder", 0x10FFFFFF, 0x12000000);
        Set("FxDivider", 0x18FFFFFF, 0x16000000);
        Set("FxHover", 0x1CFFFFFF, 0x10000000);
        Set("FxPressed", 0x2EFFFFFF, 0x1E000000);
        Set("FxChip", 0x16FFFFFF, 0xF2FFFFFF);
        Set("FxChipBorder", 0x26FFFFFF, 0x24000000);
        Set("FxFooter", 0x14000000, 0x08000000);
        Set("FxAccentText", 0xFF60CDFF, 0xFF005FB8);
        Set("FxAccentFill", 0xFF60CDFF, 0xFF005FB8);
        Set("FxAccentSoft", 0x1A60CDFF, 0x14005FB8);
        Set("FxAccentBorder", 0x4060CDFF, 0x55005FB8);
        Set("FxDangerText", 0xFFFFC2C2, 0xFFB42318);
        Set("FxDangerBg", 0x33E5484D, 0x14D92D20);
        Set("FxDangerBorder", 0x66E5484D, 0x55D92D20);
        Set("FxSuccessText", 0xFFA6F4B5, 0xFF0F7B0F);
        Set("FxSuccessBg", 0x2655CC55, 0x180F7B0F);
        Set("FxSuccessBorder", 0x5555CC55, 0x500F7B0F);
        Set("FxBrandText", 0xFFB98CFF, 0xFF6B3FD4);
        Set("FxDialogSurface", 0xF71A1D26, 0xFAFBFBFD);
        Set("FxDialogBorder", 0x38FFFFFF, 0x26000000);
        Set("FxWarnText", 0xFFFFD27A, 0xFF8A5300);
        if (_customAccent is { } accent)
        {
            _accentResourcesOverridden = true;
            var brush = Freeze(new SolidColorBrush(accent)); Color readable = ReadableAccent(accent, light ? Color.FromRgb(251, 251, 253) : Color.FromRgb(26, 29, 38));
            var text = Freeze(new SolidColorBrush(readable)); res["FxAccentTextBrush"] = text; res["FxAccentTextColor"] = readable;
            Color soft = Color.FromArgb(30, accent.R, accent.G, accent.B), border = Color.FromArgb(90, accent.R, accent.G, accent.B);
            res["FxAccentSoftBrush"] = Freeze(new SolidColorBrush(soft)); res["FxAccentSoftColor"] = soft;
            res["FxAccentBorderBrush"] = Freeze(new SolidColorBrush(border)); res["FxAccentBorderColor"] = border;
            res["AccentFillColorDefaultBrush"] = brush; res["AccentTextFillColorPrimaryBrush"] = text;
            res["FxAccentFillBrush"] = brush; res["FxAccentFillColor"] = accent;
            res["TextOnAccentFillColorPrimaryBrush"] = Contrast(Colors.Black, accent) >= Contrast(Colors.White, accent) ? Brushes.Black : Brushes.White;
        }
        else if (_accentResourcesOverridden) { res.Remove("AccentFillColorDefaultBrush"); res.Remove("AccentTextFillColorPrimaryBrush"); res.Remove("TextOnAccentFillColorPrimaryBrush"); _accentResourcesOverridden = false; }
    }

    public static void RemoveBackdrop(Window window)
    {
        if (window is FluentWindow fw)
            fw.WindowBackdropType = WindowBackdropType.None;
    }

    public static string GetDisplayName(AppTheme theme) => theme switch
    {
        AppTheme.DarkMica => "Dark Mica",
        AppTheme.LightMica => "Light Mica",
        AppTheme.FullBlack => "Full Black",
        AppTheme.AcrylicBlur => "Acrylic Blur",
        _ => theme.ToString()
    };
}

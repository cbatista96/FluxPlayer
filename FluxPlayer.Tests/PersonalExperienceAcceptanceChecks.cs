using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using FluxPlayer.Windows;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class PersonalExperienceAcceptanceChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> predicate, string message)
    { var clock = Stopwatch.StartNew(); while (!predicate()) { if (clock.Elapsed.TotalSeconds > 6) throw new InvalidOperationException(message); await Task.Delay(20); } }
    private static IEnumerable<DependencyObject> Nodes(DependencyObject node)
    { yield return node; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in Nodes(VisualTreeHelper.GetChild(node, i))) yield return child; }
    private static Button Button(Window window, string text) => Nodes(window).OfType<Button>().Single(b => b.Content?.ToString() == text);
    private static void Click(Window window, string text) => Button(window, text).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static TextBox Input(Window window, string prefix) => Nodes(window).OfType<TextBox>().Single(b => AutomationProperties.GetName(b).StartsWith(prefix, StringComparison.Ordinal));
    private static void Style(Window window)
    { window.Resources.MergedDictionaries.Insert(0, new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark }); window.Resources.MergedDictionaries.Insert(1, new Wpf.Ui.Markup.ControlsDictionary()); window.ShowActivated = false; }
    private static Color Pixel(BitmapSource image, int x, int y)
    { var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); byte[] pixel = new byte[4]; converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]); }
    private static void Gif(string path, int repeat = 1)
    {
        using var output = new BinaryWriter(File.Create(path)); output.Write("GIF89a"u8); output.Write((ushort)2); output.Write((ushort)1); output.Write(new byte[] { 0x81, 0, 0, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255 });
        output.Write(new byte[] { 0x21, 0xff, 11 }); output.Write("NETSCAPE2.0"u8); output.Write(new byte[] { 3, 1, (byte)repeat, (byte)(repeat >> 8), 0 });
        foreach (var frame in new[] { (X: 0, Color: 1, Disposal: 1), (X: 1, Color: 2, Disposal: 2), (X: 0, Color: 3, Disposal: 3), (X: 1, Color: 2, Disposal: 1) })
        {
            output.Write(new byte[] { 0x21, 0xf9, 4, (byte)(frame.Disposal << 2), 8, 0, 0, 0, 0x2c }); output.Write((ushort)frame.X); output.Write((ushort)0); output.Write((ushort)1); output.Write((ushort)1); output.Write((byte)0);
            int code = 4 | frame.Color << 3 | 5 << 6; output.Write(new byte[] { 2, 2, (byte)code, (byte)(code >> 8), 0 });
        }
        output.Write((byte)0x3b);
    }
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        foreach (int id in new[] { 272, 286, 288, 289, 290 }) await test($"F{id:0000} Personal experience native acceptance: real controls and saved preferences", async () =>
        {
            string folder = Path.Combine(root, "experience-" + id + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); string P(string name) => Path.Combine(folder, name);
            using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false); await using var library = new LibraryService(folder); var settings = new SettingsService(folder); var configuration = new AppSettings { Volume = 0, AutoPlay = false }; var vm = new MainViewModel(engine, settings, configuration, library); var owner = new Window { Width = 500, Height = 420, Left = 200, Top = 150, ShowActivated = false }; Style(owner); owner.Show();
            try
            {
                if (id == 272)
                {
                    string wav = P("Silencio.WAV"); using (var writer = new BinaryWriter(File.Create(wav))) { int bytes = 48000 * 4 * 4; writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000); writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(bytes); writer.Write(new byte[bytes]); }
                    var first = new AutomaticMediaRule(Guid.NewGuid().ToString("N"), "First", [".wav"], "Audio", 17, 1.25); var second = new AutomaticMediaRule(Guid.NewGuid().ToString("N"), "Second", [".WAV"], "Video", 33, 1.5);
                    await vm.AutomaticProfiles.Save(first, CancellationToken.None); await vm.AutomaticProfiles.Save(second, CancellationToken.None); await vm.AutomaticProfiles.Save(first with { Volume = 19 }, CancellationToken.None); Check((await vm.AutomaticProfiles.Snapshot()).Rules[0].Id == first.Id, "Editing preserves first-match priority.");
                    bool invalid = false; try { await vm.AutomaticProfiles.Save(first with { Volume = -.5 }, CancellationToken.None); } catch (InvalidDataException) { invalid = true; } Check(invalid, "Only -1 is accepted as the keep-volume sentinel.");
                    var panel = new AutomaticProfilesWindow(owner, vm); Style(panel); panel.Show();
                    try
                    {
                        await Until(() => Nodes(panel).OfType<DataGrid>().Single().Items.Count == 2, "Saved rules load in the real table."); var table = Nodes(panel).OfType<DataGrid>().Single(); table.SelectedItem = table.Items.Cast<AutomaticMediaRule>().Single(r => r.Id == second.Id); Click(panel, "Move rule up"); await Until(() => Button(panel, "Move rule up").IsEnabled && table.Items.Cast<AutomaticMediaRule>().First().Id == second.Id, "Priority button updates persisted order.");
                        Click(panel, "Activate / reload rules"); await Until(() => Button(panel, "Activate / reload rules").IsEnabled, "Activation finishes."); vm.AddFilesToPlaylist([wav]); vm.PlayAt(0); await Until(() => engine.IsPlaying, "Native media starts."); Check(vm.Volume == 33 && Math.Abs(vm.PlaybackRate - 1.5f) < .01 && vm.ModePreference == PlayerModePreference.Video, "First matching case-insensitive rule reaches live player settings."); vm.StopCommand.Execute(null);
                        var pending = vm.ActivateAutomaticProfiles(true, CancellationToken.None); await vm.ActivateAutomaticProfiles(false, CancellationToken.None); await pending; vm.Volume = 0; vm.PlaybackRate = 1; vm.ModePreference = PlayerModePreference.Automatic; vm.PlayItemCommand.Execute(vm.Playlist[0]); await Until(() => engine.IsPlaying, "Reopen after disable."); Check(vm.Volume == 0 && vm.PlaybackRate == 1 && vm.ModePreference == PlayerModePreference.Automatic, "A pending activation cannot override a subsequent disable."); vm.StopCommand.Execute(null);
                        table.SelectedItem = table.Items.Cast<AutomaticMediaRule>().Single(r => r.Id == second.Id); Click(panel, "Delete rule"); await Until(() => table.Items.Count == 1 && Button(panel, "Delete rule").IsEnabled, "Delete action updates saved rules."); Check((await new AutomaticMediaProfiles(folder).Snapshot()).Rules.Single().Id == first.Id, "Priority and deletion survive reopening.");
                    }
                    finally { panel.Close(); }
                }
                if (id == 286)
                {
                    string path = P("Disposal.gif"); Gif(path); var animation = await GifAnimations.Decode(path, CancellationToken.None); Check(animation.Frames.Length == 4 && animation.Plays == 2 && animation.DelaysMs.All(d => d == 80) && animation.Frames.All(f => f.IsFrozen), "GIF loop count, delays and frozen frames.");
                    Check(Pixel(animation.Frames[0], 0, 0).R == 255 && Pixel(animation.Frames[1], 1, 0).G == 255 && Pixel(animation.Frames[2], 0, 0).B == 255 && Pixel(animation.Frames[2], 1, 0).G == 0 && Pixel(animation.Frames[3], 0, 0).R == 255, "Logical canvas, partial-frame placement and background/previous disposal are composed correctly.");
                    var photo = new PhotoViewerWindow(owner, AppTheme.DarkMica, [path], false); Style(photo); photo.Show();
                    try
                    {
                        await Until(() => Nodes(photo).OfType<Image>().Any(i => i.Source is BitmapSource), "Actual photo window loads GIF."); var image = Nodes(photo).OfType<Image>().Single(i => i.Source is BitmapSource); Click(photo, "Pause GIF"); var paused = image.Source; await Task.Delay(180); Check(ReferenceEquals(paused, image.Source), "Pause freezes the actual presented frame."); Click(photo, "Play GIF"); await Until(() => !ReferenceEquals(paused, image.Source), "Resume changes presented frames."); await Until(() => Nodes(photo).OfType<Button>().Any(b => b.Content?.ToString() == "Play GIF"), "Finite animation finishes after its declared repeat count."); Click(photo, "Play GIF"); await Until(() => Pixel((BitmapSource)image.Source, 0, 0).R == 255, "Replay starts at first frame."); photo.WindowState = WindowState.Minimized; var minimized = image.Source; await Task.Delay(180); Check(ReferenceEquals(minimized, image.Source), "Minimize stops animation."); photo.Close(); Check(image.Source == null, "Closing releases presented GIF data.");
                    }
                    finally { if (photo.IsLoaded) photo.Close(); }
                    using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); bool canceled = false; try { await GifAnimations.Decode(path, cancellation.Token); } catch (OperationCanceledException) { canceled = true; } Check(canceled, "GIF decode honors cancellation.");
                }
                if (id is 288 or 289)
                {
                    string background = P("Background.png"); var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat(new byte[] { 20, 60, 220, 255 }, 16).SelectMany(p => p).ToArray(), 16); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var output = File.Create(background)) png.Save(output);
                    var canvas = new Image(); owner.Content = canvas; int calls = 0; bool fail = false;
                    async Task Apply(ExperienceSettings preference) { if (fail) throw new IOException("Fixture rejects apply."); var artwork = await new CinemaArtwork().Load(preference.Background, 1600, CancellationToken.None); canvas.Source = artwork; canvas.Opacity = preference.BackgroundOpacity; ThemeService.SetCustomAccent(preference.Accent); calls++; }
                    var panel = new ExperienceWindow(owner, vm, Apply); Style(panel); panel.Show();
                    try
                    {
                        Input(panel, "Accent (").Text = "#FFFFFF"; Input(panel, "Local idle-video").Text = background; Input(panel, "Background opacity").Text = "0.35"; Click(panel, "Save and apply"); await Until(() => calls == 1, "Actual save/apply action finishes."); Check(settings.Load().Experience is { Accent: "#FFFFFF", BackgroundOpacity: .35 } && settings.Load().Experience.Background == background, "Appearance preferences persist.");
                        if (id == 288)
                        {
                            foreach (bool light in new[] { false, true }) { ThemeService.ApplyDialogPalette(light); var fill = (SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"]; var text = (SolidColorBrush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]; var onAccent = (SolidColorBrush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]; Check(fill.Color == Colors.White && ((SolidColorBrush)Application.Current.Resources["FxAccentFillBrush"]).Color == Colors.White && onAccent.Color == Colors.Black && (light ? text.Color.R < 128 : text.Color.R > 128), "Custom white fill retains readable theme text and black on-accent labels."); }
                            ThemeService.SetCustomAccent("#000000"); ThemeService.ApplyDialogPalette(false); Check(((SolidColorBrush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]).Color.R > 100 && ((SolidColorBrush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]).Color == Colors.White, "Black custom accents remain readable in dark mode.");
                        }
                        else
                        {
                            Check(canvas.Source is BitmapSource decoded && Pixel(decoded, 0, 0) == Color.FromRgb(220, 60, 20) && canvas.Opacity == .35, "Saved local image is decoded and applied to the real idle canvas.");
                            string corrupt = P("Corrupt.png"); await File.WriteAllTextAsync(corrupt, "invalid image"); Input(panel, "Local idle-video").Text = corrupt; Click(panel, "Save and apply"); await Until(() => Button(panel, "Save and apply").IsEnabled, "Invalid image validation finishes."); Check(calls == 1 && settings.Load().Experience.Background == background, "Unreadable backgrounds do not replace saved working preferences.");
                            Input(panel, "Local idle-video").Text = background; Input(panel, "Background opacity").Text = "0.4"; fail = true; Click(panel, "Save and apply"); await Until(() => Button(panel, "Save and apply").IsEnabled, "Failed callback completes rollback."); Check(settings.Load().Experience.BackgroundOpacity == .35 && configuration.Experience.BackgroundOpacity == .35, "Apply failure restores persisted and current settings.");
                        }
                    }
                    finally { panel.Close(); }
                }
                if (id == 290)
                {
                    var child = new Window { Width = 260, Height = 200, Owner = owner, ShowActivated = false }; using var dock = new WindowDock(owner, child, "Right"); child.Show(); child.UpdateLayout(); await Until(() => Math.Abs(child.Left - (owner.Left + owner.ActualWidth + 8)) < 2, "Dock initializes when child loads."); owner.Left += 30; await Until(() => Math.Abs(child.Left - (owner.Left + owner.ActualWidth + 8)) < 2, "Child follows actual owner movement."); child.Width = 350; child.UpdateLayout(); await Task.Delay(40); Check(child.Left >= 0 && child.Top >= 0, "Resizing keeps child in monitor work area."); dock.Dispose(); double detached = child.Left; owner.Left += 25; await Task.Delay(40); Check(child.Left == detached, "Disposing docking detaches movement handlers."); child.Close(); Check(vm.SaveExperience(new(DockEqualizer: true, DockSide: "Left")) && settings.Load().Experience is { DockEqualizer: true, DockSide: "Left" }, "Dock opt-in and side persist.");
                }
            }
            finally { ThemeService.SetCustomAccent(""); ThemeService.ApplyDialogPalette(false); owner.Close(); vm.StopCommand.Execute(null); await vm.ShutdownAsync(); }
        });
    }
}

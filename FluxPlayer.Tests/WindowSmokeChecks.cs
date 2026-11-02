using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Diagnostics;
using System.IO;
using Lambda = System.Linq.Expressions.Expression;
using System.Collections.Immutable;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

/// <summary>
/// Opens every window the player can show. Most editors were only reachable by
/// clicking, so a missing resource or a broken binding stayed invisible until a
/// user opened that one panel.
/// </summary>
internal static class WindowSmokeChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class BindingFailureListener : TraceListener
    {
        public readonly List<string> Failures = new();
        public string? Current;
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (message == null || Benign(message)) return;
            Failures.Add(Current == null ? message : Current + ": " + message);
        }

        // WPF reports this for every ComboBoxItem declared outside a live ItemsControl
        // tree. It is framework noise, not a binding the player got wrong.
        private static bool Benign(string message) =>
            message.Contains("AncestorType='System.Windows.Controls.ItemsControl'", StringComparison.Ordinal)
            && message.Contains("ContentAlignment", StringComparison.Ordinal);
    }

    // Shown by the shell itself with live playback state; the player checks cover them.
    private static readonly string[] Shell = { "MainWindow", "MiniPlayerWindow" };

    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        string folder = Path.Combine(root, "window-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var types = typeof(MainViewModel).Assembly.GetTypes()
            .Where(type => typeof(Window).IsAssignableFrom(type) && !type.IsAbstract && type.IsPublic)
            .Where(type => !Shell.Contains(type.Name))
            .OrderBy(type => type.Name, StringComparer.Ordinal).ToArray();
        Check(types.Length >= 60, $"The player defines far fewer windows than expected ({types.Length}).");

        await test($"Window smoke: every reachable window opens with its real resources ({types.Length} windows)", async () =>
        {
            using var engine = new MediaService(hardwareDecoding: false, enableDiagnosticLogging: false);
            await using var library = new LibraryService(Path.Combine(folder, "library"));
            var store = new SettingsService(folder);
            Check(store.Save(new AppSettings { Volume = 0, AutoPlay = false }), "Isolated window smoke settings");
            var vm = new MainViewModel(engine, store, library: library);
            var listener = new BindingFailureListener();
            var added = SharedResources();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            // A Fluent host matches the real shell: the shared dictionaries restyle a plain Window after it is shown.
            // SettingsPanel resolves its service through App.Services, which only the
            // real application builds; without it the whole settings window is untestable.
            var servicesSetter = typeof(FluxPlayer.App).GetProperty("Services")!.GetSetMethod(nonPublic: true)!;
            object? previousServices = typeof(FluxPlayer.App).GetProperty("Services")!.GetValue(null);
            var container = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(container, store);
            servicesSetter.Invoke(null, new object?[] { Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(container) });
            var host = new Wpf.Ui.Controls.FluentWindow { Width = 640, Height = 480, Left = 40, Top = 40, ShowActivated = false, ShowInTaskbar = false, Title = "Window smoke host" };
            host.Show();
            var opened = new List<string>();
            var skipped = new List<string>();
            var broken = new List<string>();
            var cramped = new List<string>();
            try
            {
                foreach (var type in types)
                {
                    Window? window = null;
                    listener.Current = type.Name;
                    try
                    {
                        if (!TryBuild(type, host, vm, engine, out window, out string reason)) { skipped.Add(type.Name + " (" + reason + ")"); continue; }
                        window!.ShowActivated = false;
                        window.ShowInTaskbar = false;
                        window.Owner ??= host;
                        // Wait for the first render: the Fluent title bar finishes wiring
                        // itself there, and closing before that leaves it holding no handle.
                        var rendered = new TaskCompletionSource();
                        window.ContentRendered += (_, _) => rendered.TrySetResult();
                        window.Show();
                        window.UpdateLayout();
                        await Task.WhenAny(rendered.Task, Task.Delay(TimeSpan.FromSeconds(5)));
                        Check(rendered.Task.IsCompleted, type.Name + " never rendered its content");
                        await Dispatcher.Yield(DispatcherPriority.Background);
                        opened.Add(type.Name);
                        // A 1366x768 laptop keeps about 1366x728 of work area once the
                        // taskbar is there; a window that cannot shrink into it is unusable.
                        if (window.MinWidth > 1024 || window.MinHeight > 700)
                            cramped.Add($"{type.Name} ({window.MinWidth:0}x{window.MinHeight:0})");

                    }
                    catch (Exception e) when (e is not OutOfMemoryException)
                    {
                        var cause = e is TargetInvocationException { InnerException: { } inner } ? inner : e;
                        broken.Add(type.Name + ": " + cause.GetType().Name + " " + cause.Message);
                    }
                    finally
                    {
                        listener.Current = null;
                        if (window != null) { try { window.Close(); } catch (InvalidOperationException) { } }
                        await Dispatcher.Yield(DispatcherPriority.Background);
                    }
                }
            }
            finally
            {
                host.Close();
                PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                servicesSetter.Invoke(null, new object?[] { previousServices });
                foreach (var dictionary in added) Application.Current.Resources.MergedDictionaries.Remove(dictionary);
                vm.StopCommand.Execute(null);
                await vm.ShutdownAsync();
            }
            Console.WriteLine($"Window smoke: opened {opened.Count} of {types.Length}" + (skipped.Count == 0 ? "" : "; not constructible: " + string.Join(", ", skipped)));
            Check(broken.Count == 0, "Windows failed to open: " + string.Join(" | ", broken));
            Check(cramped.Count == 0, "Windows that cannot fit a small laptop screen: " + string.Join(", ", cramped));
            Check(listener.Failures.Count == 0, "Binding failures while opening windows: " + string.Join(" | ", listener.Failures.Take(10)));
            Check(opened.Count == types.Length, $"Only {opened.Count} of {types.Length} windows could be constructed: {string.Join(", ", skipped)}. Teach TrySample about the missing argument instead of losing the coverage.");
        });
    }

    /// <summary>The player merges these in App.xaml; the console host has to do it explicitly.</summary>
    private static ResourceDictionary[] SharedResources()
    {
        var application = Application.Current;
        var dictionaries = new ResourceDictionary[]
        {
            new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark },
            new Wpf.Ui.Markup.ControlsDictionary(),
            new ResourceDictionary { Source = new Uri("pack://application:,,,/FluxPlayer;component/Themes/PlayerStyles.xaml") },
            new ResourceDictionary { Source = new Uri("pack://application:,,,/FluxPlayer;component/Themes/WorkbenchStyles.xaml") },
        };
        foreach (var dictionary in dictionaries) application.Resources.MergedDictionaries.Add(dictionary);
        application.Resources["BoolToVis"] = new FluxPlayer.Converters.BoolToVisibilityConverter();
        application.Resources["ActiveTag"] = new FluxPlayer.Converters.NonZeroToActiveTagConverter();
        ThemeService.ApplyDialogPalette(false);
        return dictionaries;
    }

    private static bool TryBuild(Type type, Window host, MainViewModel vm, MediaService engine, out Window? window, out string reason)
    {
        window = null; reason = "";
        foreach (var constructor in type.GetConstructors().OrderByDescending(item => item.GetParameters().Length))
        {
            if (!TryArguments(constructor, host, vm, engine, out object?[] arguments, out reason)) continue;
            window = (Window)constructor.Invoke(arguments);
            return true;
        }
        if (reason.Length == 0) reason = "no public constructor";
        return false;
    }

    private static bool TryArguments(ConstructorInfo constructor, Window host, MainViewModel vm, MediaService engine, out object?[] arguments, out string reason)
    {
        var parameters = constructor.GetParameters();
        arguments = new object?[parameters.Length]; reason = "";
        for (int index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            var kind = parameter.ParameterType;
            if (kind == typeof(Window)) arguments[index] = host;
            else if (kind == typeof(MainViewModel)) arguments[index] = vm;
            else if (kind == typeof(AppTheme)) arguments[index] = vm.CurrentTheme;
            else if (kind == typeof(string) && parameter.Name == "provider") arguments[index] = "Jellyfin";
            else if (parameter.HasDefaultValue) arguments[index] = parameter.DefaultValue;
            else if (TrySample(kind, engine, 0, out object? value)) arguments[index] = value;
            else { reason = "needs " + kind.Name; return false; }
        }
        return true;
    }

    /// <summary>Editors take the item they edit; an empty one is enough to prove the window renders.</summary>
    private static bool TrySample(Type kind, MediaService engine, int depth, out object? value)
    {
        value = null;
        if (depth > 3) return false;
        if (kind == typeof(string)) { value = ""; return true; }
        if (kind == typeof(VlcWpfRenderer)) { value = new VlcWpfRenderer(engine, new System.Windows.Controls.Image()); return true; }
        if (typeof(Delegate).IsAssignableFrom(kind)) { value = NoOp(kind); return true; }
        if (kind.IsArray) { value = Populated(kind.GetElementType()!, engine, depth); return true; }
        if (kind.IsGenericType && kind.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
        { value = kind.GetField("Empty", BindingFlags.Public | BindingFlags.Static)!.GetValue(null); return true; }
        if (kind.IsInterface && kind.IsGenericType && CollectionShapes.Contains(kind.GetGenericTypeDefinition()))
        { value = Populated(kind.GetGenericArguments()[0], engine, depth); return true; }
        if (kind.IsEnum) { value = Enum.GetValues(kind).GetValue(0); return true; }
        if (kind.IsValueType) { value = Activator.CreateInstance(kind); return true; }
        if (kind.IsAbstract || kind.IsInterface) return false;
        foreach (var constructor in kind.GetConstructors().OrderBy(item => item.GetParameters().Length))
        {
            var parameters = constructor.GetParameters();
            var arguments = new object?[parameters.Length];
            bool complete = true;
            for (int index = 0; index < parameters.Length && complete; index++)
            {
                if (parameters[index].HasDefaultValue) arguments[index] = parameters[index].DefaultValue;
                else if (TrySample(parameters[index].ParameterType, engine, depth + 1, out object? nested)) arguments[index] = nested;
                else complete = false;
            }
            if (!complete) continue;
            try { value = constructor.Invoke(arguments); return true; } catch (TargetInvocationException) { }
        }
        return false;
    }

    /// <summary>Several editors refuse an empty selection, so hand them one sample row.</summary>
    private static Array Populated(Type element, MediaService engine, int depth)
    {
        if (TrySample(element, engine, depth + 1, out object? single) && single != null)
        {
            var filled = Array.CreateInstance(element, 1);
            filled.SetValue(single, 0);
            return filled;
        }
        return Array.CreateInstance(element, 0);
    }

    private static readonly Type[] CollectionShapes =
    {
        typeof(IEnumerable<>), typeof(IReadOnlyList<>), typeof(IReadOnlyCollection<>), typeof(IList<>), typeof(ICollection<>),
    };

    private static Delegate NoOp(Type delegateType)
    {
        var invoke = delegateType.GetMethod("Invoke")!;
        var parameters = invoke.GetParameters().Select(item => Lambda.Parameter(item.ParameterType)).ToArray();
        var body = invoke.ReturnType == typeof(void) ? (Lambda)Lambda.Empty() : Lambda.Default(invoke.ReturnType);
        return Lambda.Lambda(delegateType, body, parameters).Compile();
    }
}

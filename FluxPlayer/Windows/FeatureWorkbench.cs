using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace FluxPlayer.Windows;

/// <summary>Shared Fluent structure for focused editors, not a catch-all tools page.</summary>
public class FeatureWorkbench : Wpf.Ui.Controls.FluentWindow
{
    protected readonly StackPanel Form = new();
    protected readonly WrapPanel Actions = new();
    protected readonly TextBlock Status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
    protected readonly ContentControl View = new();
    protected readonly CancellationTokenSource Lifetime = new();
    private CancellationTokenSource? _job;
    protected CancellationToken OperationToken => _job?.Token ?? Lifetime.Token;
    protected bool IsClosed, Busy;
    public FeatureWorkbench(Window owner, MainViewModel vm, string title, string description)
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/FluxPlayer;component/Themes/WorkbenchStyles.xaml") });
        Owner = owner; Title = Services.Localizer.T(title) + " · FluxPlayer"; Width = 1020; Height = 740; MinWidth = 820; MinHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner; ExtendsContentIntoTitleBar = true; WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica; Background = System.Windows.Media.Brushes.Transparent;
        var root = new Grid { Margin = new(24, 18, 24, 20) }; foreach (var size in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) root.RowDefinitions.Add(new() { Height = size });
        var header = new StackPanel { Margin = new(0, 0, 0, 16) }; header.Children.Add(new TextBlock { Text = title, FontSize = 25, FontWeight = FontWeights.SemiBold }); header.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) }); root.Children.Add(header);
        var scroll = new ScrollViewer { Content = Form, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 0, 0, 14) }; Grid.SetRow(scroll, 1); root.Children.Add(scroll); Grid.SetRow(View, 2); root.Children.Add(View);
        var footer = new StackPanel { Margin = new(0, 14, 0, 0) }; footer.Children.Add(Actions); footer.Children.Add(Status); Grid.SetRow(footer, 3); root.Children.Add(footer); Content = ToolWindowChrome.Wrap(this, root);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this); Closed += (_, _) => { IsClosed = true; Lifetime.Cancel(); };
        Loaded += (_, _) => FitToOwnerScreen();
    }
    private void FitToOwnerScreen()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(Owner ?? this).Handle;
        var working = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var transform = PresentationSource.FromVisual(Owner ?? this)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var area = Rect.Transform(new Rect(working.X, working.Y, working.Width, working.Height), transform);
        // Large catalog/export editors must keep their title bar and footer
        // accessible on smaller displays and at increased Windows text scaling.
        MinWidth = Math.Min(MinWidth, area.Width); MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Min(Width, area.Width); Height = Math.Min(Height, area.Height);
        Left = Math.Clamp(Left, area.Left, area.Right - Width);
        Top = Math.Clamp(Top, area.Top, area.Bottom - Height);
    }
    protected T Field<T>(string label, T control) where T : Control
    { var panel = new StackPanel { Margin = new(0, 0, 0, 12) }; panel.Children.Add(new TextBlock { Text = label, Margin = new(0, 0, 0, 6) }); panel.Children.Add(control); AutomationProperties.SetName(control, label); Form.Children.Add(panel); return control; }
    protected TextBox Text(string label, string value = "", int max = 4096) => Field(label, new TextBox { Text = value, MaxLength = max, Padding = new(10, 7, 10, 7) });
    protected ComboBox Choice(string label, IEnumerable<string> choices, int selected = 0) => Field(label, new ComboBox { ItemsSource = choices, SelectedIndex = selected });
    protected CheckBox Toggle(string label, bool value = false) { var control = new CheckBox { Content = label, IsChecked = value, Margin = new(0, 0, 0, 12) }; Form.Children.Add(control); return control; }
    protected Button Action(string name, Func<Task> work)
    { var button = new Button { Content = name, Padding = new(14, 8, 14, 8), Margin = new(0, 0, 8, 6) }; button.Click += async (_, _) => await Run(work); Actions.Children.Add(button); return button; }
    protected async Task Run(Func<Task> work)
    { if (Busy || IsClosed) return; Busy = true; using var job = CancellationTokenSource.CreateLinkedTokenSource(Lifetime.Token); _job = job; Actions.IsEnabled = Form.IsEnabled = false; try { await work(); } catch (Exception ex) when (ex is not OutOfMemoryException) { if (!IsClosed) Status.Text = ex is OperationCanceledException ? "Canceled. Completed work has been retained." : ex.Message; } finally { _job = null; Busy = false; if (!IsClosed) Actions.IsEnabled = Form.IsEnabled = true; } }
    protected DataGrid Table(params (string Title, string Property, double Width)[] columns)
    {
        var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Extended, RowHeight = 36, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.None, EnableRowVirtualization = true, EnableColumnVirtualization = true };
        foreach (var column in columns) table.Columns.Add(new DataGridTextColumn { Header = column.Title, Binding = new Binding(column.Property), Width = new DataGridLength(column.Width, DataGridLengthUnitType.Star) }); View.Content = table; return table;
    }
    protected Button CancelAction()
    { var cancel = new Button { Content = "Cancel operation", Padding = new(14, 8, 14, 8), Margin = new(8, 0, 0, 6) }; cancel.Click += (_, _) => _job?.Cancel(); return cancel; }
    protected void UseFormLayout(double height)
    {
        Height = height;
        MinHeight = Math.Min(height, 480);
        View.Visibility = Visibility.Collapsed;
        var root = (Grid)View.Parent;
        root.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
        root.RowDefinitions[2].Height = GridLength.Auto;
        ((ScrollViewer)Form.Parent).MaxHeight = double.PositiveInfinity;
    }
}

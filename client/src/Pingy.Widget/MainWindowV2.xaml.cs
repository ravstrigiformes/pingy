using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Pingy.Widget.ViewModels;

namespace Pingy.Widget;

public partial class MainWindowV2 : Window
{
    private static readonly Regex DigitsOnly = new("^[0-9]+$");

    private MainViewModel? Vm => DataContext as MainViewModel;

    public MainWindowV2()
    {
        InitializeComponent();

        var app = (App)Application.Current;
        DataContext = app.ViewModel;

        Loaded += async (_, _) =>
        {
            if (app.ViewModel is not null)
                await app.ViewModel.StartAsync();
        };
    }

    // -- Window controls -------------------------------------------------

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // -- Toolbar handlers ------------------------------------------------

    private void IntervalDecrease_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.SetInterval(Vm.IntervalSeconds - StepFor(Vm.IntervalSeconds));
    }

    private void IntervalIncrease_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.SetInterval(Vm.IntervalSeconds + StepFor(Vm.IntervalSeconds));
    }

    private static int StepFor(int current)
    {
        if (current < 10) return 1;
        if (current < 60) return 5;
        return 30;
    }

    private void IntervalBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !DigitsOnly.IsMatch(e.Text);
    }

    private void IntervalBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            (sender as TextBox)?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    // -- Add / Edit target ----------------------------------------------

    private async void AddTargetButton_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var dlg = new AddTargetWindow { Owner = this };
        var ok = dlg.ShowDialog();
        if (ok != true) return;
        await Vm.AddTargetAsync(dlg.LabelText, dlg.HostText, dlg.CollectTags());
    }

    private async void TargetRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null) return;
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not TargetStatusViewModel row) return;

        // Bail if the click originated on the drag handle or any interactive child
        if (e.OriginalSource is DependencyObject src && (HasDragHandleAncestor(src) || IsClickInteractive(src))) return;

        var dlg = new AddTargetWindow(row.Target) { Owner = this };
        var ok = dlg.ShowDialog();
        if (ok != true) return;

        if (dlg.DeleteRequested)
            await Vm.DeleteTargetAsync(row.Target.Id);
        else
            await Vm.UpdateTargetAsync(row.Target.Id, dlg.LabelText, dlg.HostText, dlg.CollectTags());
    }

    private static bool IsClickInteractive(DependencyObject src)
    {
        for (var d = src; d is not null; d = LogicalTreeHelper.GetParent(d) ?? VisualTreeHelper.GetParent(d))
        {
            if (d is ButtonBase or TextBox or ToggleButton or Slider or ComboBox) return true;
        }
        return false;
    }

    private static bool HasDragHandleAncestor(DependencyObject src)
    {
        for (var d = src; d is not null; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement fe && fe.Tag as string == "DragHandle") return true;
        }
        return false;
    }

    // -- Drag-to-reorder (with ghost + insertion indicator) -------------

    private Popup? _dragGhost;
    private TargetStatusViewModel? _dragSource;

    private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null || !Vm.CanReorder) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not TargetStatusViewModel vm) return;

        e.Handled = true;
        _dragSource = vm;
        _dragGhost = CreateDragGhost(vm);
        _dragGhost.IsOpen = true;

        // Override system DnD cursor with Hand for the duration of the drag
        GiveFeedbackEventHandler feedback = OnDragGiveFeedback;
        DragDrop.AddGiveFeedbackHandler(fe, feedback);

        try
        {
            DragDrop.DoDragDrop(fe, vm, DragDropEffects.Move);
        }
        finally
        {
            DragDrop.RemoveGiveFeedbackHandler(fe, feedback);
            if (_dragGhost is not null) { _dragGhost.IsOpen = false; _dragGhost = null; }
            _dragSource = null;
            Vm?.ClearDropIndicators();
        }
    }

    private void OnDragGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors = false;
        Mouse.SetCursor(e.Effects == DragDropEffects.None ? Cursors.No : Cursors.Hand);
        e.Handled = true;
    }

    private Popup CreateDragGhost(TargetStatusViewModel vm)
    {
        var cyanBrush = (Brush)Resources["CyanBrush"];
        var voidBrush = (Brush)Resources["BgVoidBrush"];
        var brightBrush = (Brush)Resources["TextBrightBrush"];

        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(new TextBlock
        {
            Text = "⋮⋮ ",
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = cyanBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        });
        stack.Children.Add(new TextBlock
        {
            Text = vm.Label,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = brightBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "  " + vm.Host,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 11,
            Foreground = cyanBrush,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x05, 0x08, 0x10)),
            BorderBrush = cyanBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 5, 10, 5),
            Opacity = 0.85,
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0x00, 0xF0, 0xFF),
                BlurRadius = 14,
                ShadowDepth = 0,
                Opacity = 0.85,
            },
            Child = stack,
        };

        return new Popup
        {
            Child = border,
            AllowsTransparency = true,
            PlacementTarget = this,
            Placement = PlacementMode.RelativePoint,
            StaysOpen = true,
            Focusable = false,
            IsHitTestVisible = false,
        };
    }

    private void Row_DragOver(object sender, DragEventArgs e)
    {
        if (Vm is null || !Vm.CanReorder) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        if (!e.Data.GetDataPresent(typeof(TargetStatusViewModel))) { e.Effects = DragDropEffects.None; e.Handled = true; return; }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        // Place insertion indicator above/below this row based on cursor Y
        if (sender is FrameworkElement fe && fe.DataContext is TargetStatusViewModel target)
        {
            var pos = e.GetPosition(fe);
            var above = pos.Y < fe.ActualHeight / 2;
            Vm.SetDropIndicator(target, above ? TargetStatusViewModel.DropPos.Above : TargetStatusViewModel.DropPos.Below);
        }

        // Move ghost popup near cursor
        if (_dragGhost is not null)
        {
            var p = e.GetPosition(this);
            _dragGhost.HorizontalOffset = p.X + 14;
            _dragGhost.VerticalOffset = p.Y - 12;
        }
    }

    private async void Row_Drop(object sender, DragEventArgs e)
    {
        if (Vm is null || !Vm.CanReorder) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not TargetStatusViewModel target) return;
        if (e.Data.GetData(typeof(TargetStatusViewModel)) is not TargetStatusViewModel source) return;

        Vm.ClearDropIndicators();

        if (source == target) { e.Handled = true; return; }

        var fromIdx = Vm.Targets.IndexOf(source);
        var targetIdx = Vm.Targets.IndexOf(target);
        if (fromIdx < 0 || targetIdx < 0) { e.Handled = true; return; }

        var pos = e.GetPosition(fe);
        var insertAbove = pos.Y < fe.ActualHeight / 2;
        var toIdx = insertAbove ? targetIdx : targetIdx + 1;

        // Adjust for the source's own removal shifting indices
        if (fromIdx < toIdx) toIdx--;
        if (toIdx < 0) toIdx = 0;
        if (toIdx >= Vm.Targets.Count) toIdx = Vm.Targets.Count - 1;

        await Vm.MoveTargetAsync(fromIdx, toIdx);
        e.Handled = true;
    }

    // -- Mini cycler -----------------------------------------------------

    private void MiniCycler_Click(object sender, RoutedEventArgs e)
    {
        Vm?.CycleMiniDisplay();
    }

    // -- Mini-mode toggle -----------------------------------------------

    private double _savedFullWidth = 640;
    private double _savedFullHeight = 500;

    private const double FullMinWidth = 520;
    private const double FullMinHeight = 380;
    private const double FullMaxWidth = 1600;
    private const double FullMaxHeight = 1150;

    private const double MiniWidth = 160;
    private const double MiniHeight = 200;
    private const double MiniMinWidth = 140;
    private const double MiniMinHeight = 180;
    private const double MiniMaxWidth = 320;
    private const double MiniMaxHeight = 400;

    private void ModeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (Vm.IsMiniMode) GoToFull(); else GoToMini();
    }

    private void GoToMini()
    {
        if (Vm is null) return;
        _savedFullWidth = ActualWidth;
        _savedFullHeight = ActualHeight;
        MinWidth = 0; MinHeight = 0;
        MaxWidth = double.PositiveInfinity; MaxHeight = double.PositiveInfinity;
        Width = MiniWidth; Height = MiniHeight;
        MinWidth = MiniMinWidth; MinHeight = MiniMinHeight;
        MaxWidth = MiniMaxWidth; MaxHeight = MiniMaxHeight;
        Vm.IsMiniMode = true;
        ModeToggleBtn.Content = "▣";
        ModeToggleBtn.ToolTip = "Restore";
    }

    private void GoToFull()
    {
        if (Vm is null) return;
        MinWidth = 0; MinHeight = 0;
        MaxWidth = double.PositiveInfinity; MaxHeight = double.PositiveInfinity;
        Width = _savedFullWidth; Height = _savedFullHeight;
        MinWidth = FullMinWidth; MinHeight = FullMinHeight;
        MaxWidth = FullMaxWidth; MaxHeight = FullMaxHeight;
        Vm.IsMiniMode = false;
        ModeToggleBtn.Content = "▢";
        ModeToggleBtn.ToolTip = "Mini mode";
    }
}

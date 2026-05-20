using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Pingy.Widget.Settings;
using Pingy.Widget.ViewModels;

namespace Pingy.Widget;

public partial class MainWindowV2 : Window
{
    private static readonly Regex DigitsOnly = new("^[0-9]+$");

    private MainViewModel? Vm => DataContext as MainViewModel;

    private readonly SettingsStore _settingsStore = new();
    private AppSettings _settings = new();
    private TrayIcon? _trayIcon;
    private bool _forceExit;     // set when an exit is genuine (tray "Exit" / chosen Exit)
    private bool _balloonShown;  // tray hint balloon — once per session is enough

    public MainWindowV2()
    {
        InitializeComponent();

        var app = (App)Application.Current;
        DataContext = app.ViewModel;

        Loaded += async (_, _) =>
        {
            _settings = _settingsStore.Load();
            _trayIcon = new TrayIcon(
                onSingleClick: GlanceFromTray,
                onDoubleClick: RestoreFromTray,
                onExit: ExitFromTray);

            if (app.ViewModel is not null)
                await app.ViewModel.StartAsync();
            StartScanlineAnimation();
            UpdateMaxRestoreGlyph();
        };

        SizeChanged += (_, _) => RebuildChromeGeometry();
        StateChanged += (_, _) =>
        {
            // In mini mode, accidentally maximizing (Win+Up, double-click title) produces a
            // tall thin column (width clamped to MiniMaxWidth, height = full work area).
            // Bounce back to Normal so the user has to use the explicit snap-right button.
            if (Vm is not null && Vm.IsMiniMode && WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            UpdateMaxRestoreGlyph();
        };
    }

    // -- Window controls -------------------------------------------------

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1)
        {
            // Aero snap (Win+arrow / drag-to-edge) needs WindowChrome's own move tracking.
            // DragMove() works but lets us pause animations during the drag.
            if (Vm is not null) Vm.IsInteracting = true;
            try { DragMove(); }
            finally { if (Vm is not null) Vm.IsInteracting = false; }
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // -- Close behaviour / system tray ----------------------------------

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // A genuine exit (tray "Exit", or the user chose Exit) — let it close.
        if (_forceExit) return;

        var behavior = _settings.CloseBehavior;

        // First close ever: ask, and persist the answer if the user opts to.
        if (behavior == CloseBehavior.Ask)
        {
            var dlg = new ClosePromptWindow { Owner = this };
            var confirmed = dlg.ShowDialog();
            if (confirmed != true || dlg.Choice is null)
            {
                e.Cancel = true;   // dismissed — treat as "I didn't mean to close"
                return;
            }

            behavior = dlg.Choice.Value;
            if (dlg.Remember)
            {
                _settings = _settings with { CloseBehavior = behavior };
                _settingsStore.Save(_settings);
            }
        }

        if (behavior == CloseBehavior.MinimizeToTray)
        {
            e.Cancel = true;
            MinimizeToTray();
        }
        // CloseBehavior.Exit — fall through; the window closes normally.
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        base.OnClosed(e);
    }

    private void MinimizeToTray()
    {
        Hide();
        if (!_balloonShown)
        {
            _trayIcon?.ShowBalloon(
                "Pingy is monitoring from the tray — single-click to glance, double-click to open.");
            _balloonShown = true;
        }
    }

    // Single-click on the tray icon: pop up the compact mini view, docked near the
    // tray, for a quick glance at network status without restoring the full window.
    private void GlanceFromTray()
    {
        if (Vm is null) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        // GoToMini saves the full-mode footprint so a later restore is correct.
        if (!Vm.IsMiniMode) GoToMini();

        // Force the compact glance footprint (mini may have been snapped tall before)
        // and dock it to the work-area's bottom-right corner, next to the tray.
        Width = MiniWidth;
        Height = MiniHeight;
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - MiniWidth - 12;
        Top = wa.Bottom - MiniHeight - 12;

        Show();
        Activate();
    }

    // Double-click on the tray icon: open the full window.
    private void RestoreFromTray()
    {
        if (Vm is null) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        if (Vm.IsMiniMode) GoToFull();   // restores the saved full-mode footprint

        Show();
        Activate();
    }

    private void ExitFromTray()
    {
        _forceExit = true;
        Close();
    }

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

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.Zoom = Math.Min(MainViewModel.MaxZoom, Math.Round(Vm.Zoom + 0.1, 2));
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.Zoom = Math.Max(MainViewModel.MinZoom, Math.Round(Vm.Zoom - 0.1, 2));
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.Zoom = 1.0;
    }

    // -- Add / Edit target ----------------------------------------------

    private async void AddTargetButton_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var dlg = new AddTargetWindow { Owner = this };
        var ok = dlg.ShowDialog();
        if (ok != true) return;
        await Vm.AddTargetAsync(dlg.LabelText, dlg.HostText, dlg.CollectTags(), dlg.CollectPorts());
    }

    private async void TargetRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null) return;
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not TargetStatusViewModel row) return;

        if (e.OriginalSource is DependencyObject src && (HasDragHandleAncestor(src) || IsClickInteractive(src))) return;

        var dlg = new AddTargetWindow(row.Target) { Owner = this };
        var ok = dlg.ShowDialog();
        if (ok != true) return;

        if (dlg.DeleteRequested)
            await Vm.DeleteTargetAsync(row.Target.Id);
        else
            await Vm.UpdateTargetAsync(row.Target.Id, dlg.LabelText, dlg.HostText, dlg.CollectTags(), dlg.CollectPorts());
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

        if (sender is FrameworkElement fe && fe.DataContext is TargetStatusViewModel target)
        {
            // Column flow: insert before if cursor is on the left half of the card, otherwise after.
            var pos = e.GetPosition(fe);
            var before = pos.X < fe.ActualWidth / 2;
            Vm.SetDropIndicator(target, before ? TargetStatusViewModel.DropPos.Before : TargetStatusViewModel.DropPos.After);
        }

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
        var insertBefore = pos.X < fe.ActualWidth / 2;
        var toIdx = insertBefore ? targetIdx : targetIdx + 1;

        if (fromIdx < toIdx) toIdx--;
        if (toIdx < 0) toIdx = 0;
        if (toIdx >= Vm.Targets.Count) toIdx = Vm.Targets.Count - 1;

        await Vm.MoveTargetAsync(fromIdx, toIdx);
        e.Handled = true;
    }

    // -- Mini cycler -----------------------------------------------------

    private void MiniCycler_Click(object sender, RoutedEventArgs e) => Vm?.CycleMiniDisplay();

    // -- Mini-mode toggle -----------------------------------------------

    private double _savedFullWidth = 900;
    private double _savedFullHeight = 560;

    private const double FullMinWidth = 360;
    private const double FullMinHeight = 280;

    // Mini-mode design size matches the Viewbox content (160x200). The Viewbox upscales
    // anything larger, which used to make fonts/circles look oversized; restore the
    // 1:1 default so mini looks the same as the original v1 mini overlay.
    private const double MiniWidth = 160;
    private const double MiniHeight = 200;
    private const double MiniMinWidth = 140;
    private const double MiniMinHeight = 160;
    // Width is capped so aero-snap keeps the mini footprint. Height is intentionally
    // unconstrained so the snap-right button can fill the full work-area height.
    private const double MiniMaxWidth = 320;

    private void ModeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (Vm.IsMiniMode) GoToFull(); else GoToMini();
    }

    private void GoToMini()
    {
        if (Vm is null) return;
        // If currently maximized, drop back to Normal first so size assignments stick.
        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;

        _savedFullWidth = ActualWidth;
        _savedFullHeight = ActualHeight;
        MinWidth = 0; MinHeight = 0;
        MaxWidth = double.PositiveInfinity; MaxHeight = double.PositiveInfinity;
        Width = MiniWidth; Height = MiniHeight;
        MinWidth = MiniMinWidth; MinHeight = MiniMinHeight;
        // MaxWidth caps mini-mode aero-snap width so Windows keeps the mini footprint.
        // MaxHeight stays unconstrained so the snap-right button can fill the work area.
        MaxWidth = MiniMaxWidth; MaxHeight = double.PositiveInfinity;
        Vm.IsMiniMode = true;
    }

    private void GoToFull()
    {
        if (Vm is null) return;
        MinWidth = 0; MinHeight = 0;
        MaxWidth = double.PositiveInfinity; MaxHeight = double.PositiveInfinity;
        Width = _savedFullWidth; Height = _savedFullHeight;
        MinWidth = FullMinWidth; MinHeight = FullMinHeight;
        Vm.IsMiniMode = false;

        // Anchor the restored window's right edge to the monitor's right edge so a snap-right
        // mini doesn't restore partially off-screen. Vertically: keep the current Top but
        // clamp to the work area so the window stays fully visible.
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - _savedFullWidth;
        if (Left < wa.Left) Left = wa.Left;
        if (Top + _savedFullHeight > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - _savedFullHeight);
        if (Top < wa.Top) Top = wa.Top;

        UpdateMaxRestoreGlyph();
    }

    // -- Maximize / Restore (full mode only) -----------------------------

    private void MaxRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateMaxRestoreGlyph();
    }

    private void UpdateMaxRestoreGlyph()
    {
        if (MaxRestoreBtn is null) return;
        if (WindowState == WindowState.Maximized)
        {
            MaxRestoreBtn.Content = "❐";
            MaxRestoreBtn.ToolTip = "Restore";
        }
        else
        {
            MaxRestoreBtn.Content = "▢";
            MaxRestoreBtn.ToolTip = "Maximize";
        }
    }

    // -- Mini-mode: snap to right edge (full screen height, mini width) --

    private void MiniSnapRight_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || !Vm.IsMiniMode) return;

        // WPF silently no-ops Width/Height/Left/Top assignments when WindowState != Normal.
        // Drop to Normal first so the new bounds actually apply.
        if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;

        var wa = SystemParameters.WorkArea;
        var w = Math.Min(Math.Max(ActualWidth, MiniMinWidth), MiniMaxWidth);

        Width = w;
        Height = wa.Height;
        Left = wa.Right - w;
        Top = wa.Top;

        Vm.StatusLine = $"~ Snapped right: {w:F0} x {wa.Height:F0} at ({Left:F0}, {Top:F0})";
    }

    // -- Chrome geometry (cyber corner-cut border, redrawn on resize) ---

    private const double CornerCut = 12.0;

    private void RebuildChromeGeometry()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 20 || h < 20) return;
        if (OuterChrome is null || InnerChrome is null) return;

        // Outer (magenta shadow ring): full bounds with corner cuts at top-left and bottom-right.
        OuterChrome.Data = BuildCornerCutGeometry(0, 0, w, h, CornerCut);
        // Inner (cyan): inset by 2px for a double-stroke look.
        InnerChrome.Data = BuildCornerCutGeometry(2, 2, w - 4, h - 4, CornerCut - 2);

        // Resize scanline to the inner width as well.
        if (Scanline is not null) Scanline.Width = w - 6;
    }

    private static Geometry BuildCornerCutGeometry(double x, double y, double w, double h, double cut)
    {
        if (cut < 0) cut = 0;
        // Top-left + bottom-right corner cuts (angled bevels).
        var pf = new PathFigure
        {
            StartPoint = new Point(x + cut, y),
            IsClosed = true,
            IsFilled = true,
        };
        pf.Segments.Add(new LineSegment(new Point(x + w, y), true));
        pf.Segments.Add(new LineSegment(new Point(x + w, y + h - cut), true));
        pf.Segments.Add(new LineSegment(new Point(x + w - cut, y + h), true));
        pf.Segments.Add(new LineSegment(new Point(x, y + h), true));
        pf.Segments.Add(new LineSegment(new Point(x, y + cut), true));

        var pg = new PathGeometry();
        pg.Figures.Add(pf);
        pg.Freeze();
        return pg;
    }

    // -- Scanline animation (started in code so we can pause cleanly) ----

    private Storyboard? _scanlineStoryboard;

    private void StartScanlineAnimation()
    {
        if (ScanlineXf is null) return;
        var anim = new DoubleAnimation
        {
            From = -4,
            To = Math.Max(200, ActualHeight),
            Duration = new Duration(TimeSpan.FromSeconds(3.5)),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        // Re-target each tick (cheap), so the scanline grows with the window.
        SizeChanged += (_, _) =>
        {
            if (_scanlineStoryboard is null) return;
            anim.To = Math.Max(200, ActualHeight);
        };
        Storyboard.SetTarget(anim, ScanlineXf);
        Storyboard.SetTargetProperty(anim, new PropertyPath("Y"));
        _scanlineStoryboard = new Storyboard();
        _scanlineStoryboard.Children.Add(anim);
        _scanlineStoryboard.Begin();
    }

    // -- Win32 hook: pause anims during interactive move/resize ----------

    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var src = (HwndSource)PresentationSource.FromVisual(this);
        src?.AddHook(WndProc);
        RebuildChromeGeometry();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_ENTERSIZEMOVE:
                if (Vm is not null) Vm.IsInteracting = true;
                break;
            case WM_EXITSIZEMOVE:
                if (Vm is not null) Vm.IsInteracting = false;
                break;
            case WM_GETMINMAXINFO:
                // WindowStyle=None + AllowsTransparency=True maximizes past the work-area edges by
                // the resize-border thickness. Clamp the max size + position to the current monitor's
                // work area so cards aren't clipped and column math gets the real visible width.
                ClampMinMaxInfo(hwnd, lParam);
                break;
        }
        return IntPtr.Zero;
    }

    private static void ClampMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var work = info.rcWork;
        var mon = info.rcMonitor;

        mmi.ptMaxPosition.x = work.left - mon.left;
        mmi.ptMaxPosition.y = work.top - mon.top;
        mmi.ptMaxSize.x = work.right - work.left;
        mmi.ptMaxSize.y = work.bottom - work.top;
        mmi.ptMaxTrackSize.x = work.right - work.left;
        mmi.ptMaxTrackSize.y = work.bottom - work.top;

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }
}

using System;
using System.Windows;
using System.Windows.Input;
using Pingy.Widget.ViewModels;

namespace Pingy.Widget;

// Lightweight preferences dialog. It binds directly to the live MainViewModel, so
// every change previews immediately on the main window. Persistence is the owner's
// job — MainWindowV2 saves AppSettings once this window closes.
public partial class SettingsWindow : Window
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = ((App)Application.Current).ViewModel;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    // -- Opacity / zoom steppers — nudge the live VM, clamped to its valid range --

    private void OpacityDown_Click(object sender, RoutedEventArgs e) => StepOpacity(-0.05);
    private void OpacityUp_Click(object sender, RoutedEventArgs e) => StepOpacity(+0.05);
    private void ZoomDown_Click(object sender, RoutedEventArgs e) => StepZoom(-0.1);
    private void ZoomUp_Click(object sender, RoutedEventArgs e) => StepZoom(+0.1);

    private void StepOpacity(double delta)
    {
        if (Vm is null) return;
        Vm.WindowOpacity = Math.Clamp(
            Math.Round(Vm.WindowOpacity + delta, 2),
            MainViewModel.MinOpacity, MainViewModel.MaxOpacity);
    }

    private void StepZoom(double delta)
    {
        if (Vm is null) return;
        Vm.Zoom = Math.Clamp(
            Math.Round(Vm.Zoom + delta, 2),
            MainViewModel.MinZoom, MainViewModel.MaxZoom);
    }
}

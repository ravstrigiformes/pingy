using System.Windows;
using System.Windows.Input;

namespace Pingy.Widget;

// Lightweight preferences dialog. It binds directly to the live MainViewModel, so
// every change previews immediately on the main window. Persistence is the owner's
// job — MainWindowV2 saves AppSettings once this window closes.
public partial class SettingsWindow : Window
{
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
}

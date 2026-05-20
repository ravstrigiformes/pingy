using System.Windows;
using System.Windows.Input;
using Pingy.Widget.Settings;

namespace Pingy.Widget;

// First-close dialog: offers minimize-to-tray vs exit. Choice is null when the user
// dismisses the dialog (✕ / Esc) — the caller treats that as "don't close".
public partial class ClosePromptWindow : Window
{
    public CloseBehavior? Choice { get; private set; }

    public bool Remember => RememberToggle.IsChecked == true;

    public ClosePromptWindow()
    {
        InitializeComponent();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        Choice = CloseBehavior.MinimizeToTray;
        DialogResult = true;
        Close();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Choice = CloseBehavior.Exit;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
        base.OnKeyDown(e);
    }
}

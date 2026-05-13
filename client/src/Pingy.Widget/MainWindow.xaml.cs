using System.Windows;
using System.Windows.Input;

namespace Pingy.Widget;

public partial class MainWindow : Window
{
    public MainWindow()
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

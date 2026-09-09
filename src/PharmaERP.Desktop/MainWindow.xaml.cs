using System.Windows;
using PharmaERP.Desktop.ViewModels;

namespace PharmaERP.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml shell.
/// Resolves MainWindowViewModel via Dependency Injection.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => Focus();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (e.Key == System.Windows.Input.Key.F2)
        {
            if (vm.PurchaseEntryHotkeyCommand.CanExecute(null))
            {
                vm.PurchaseEntryHotkeyCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == System.Windows.Input.Key.F3)
        {
            if (vm.SalesEntryHotkeyCommand.CanExecute(null))
            {
                vm.SalesEntryHotkeyCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == System.Windows.Input.Key.F5)
        {
            if (vm.RefreshCommand.CanExecute(null))
            {
                vm.RefreshCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}

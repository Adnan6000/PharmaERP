using System.Windows;
using System.Windows.Input;
using PharmaERP.Desktop.ViewModels;

namespace PharmaERP.Desktop.Views;

public partial class InvoicePrintPreviewDialog : Window
{
    public InvoicePrintPreviewDialog()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (DataContext is InvoicePrintPreviewViewModel vm)
            {
                vm.RequestClose += () => DialogResult = true;
            }
        };

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
            else if (e.Key == Key.Enter && DataContext is InvoicePrintPreviewViewModel vm && vm.PrintCommand.CanExecute(null))
            {
                vm.PrintCommand.Execute(null);
            }
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}


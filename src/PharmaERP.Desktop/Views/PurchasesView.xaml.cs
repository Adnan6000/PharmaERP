using System.Windows.Controls;
using System.Windows.Input;
using PharmaERP.Desktop.ViewModels;

namespace PharmaERP.Desktop.Views;

public partial class PurchasesView : UserControl
{
    public PurchasesView()
    {
        InitializeComponent();
    }

    private void OnScrimMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PurchasesViewModel vm && vm.IsDetailsDrawerOpen)
        {
            vm.IsDetailsDrawerOpen = false;
        }
    }
}


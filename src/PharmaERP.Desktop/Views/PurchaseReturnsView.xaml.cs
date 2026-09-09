using System.Windows.Controls;
using System.Windows.Input;
using PharmaERP.Desktop.ViewModels;

namespace PharmaERP.Desktop.Views;

public partial class PurchaseReturnsView : UserControl
{
    public PurchaseReturnsView()
    {
        InitializeComponent();
    }

    private void OnScrimMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PurchaseReturnsViewModel vm && vm.IsDetailsDrawerOpen)
        {
            vm.IsDetailsDrawerOpen = false;
        }
    }
}


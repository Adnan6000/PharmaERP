using System.Windows.Controls;
using System.Windows.Input;
using PharmaERP.Desktop.ViewModels;

namespace PharmaERP.Desktop.Views;

public partial class SuppliersView : UserControl
{
    public SuppliersView()
    {
        InitializeComponent();
    }

    private void OnScrimMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SuppliersViewModel vm && vm.IsDrawerOpen)
        {
            vm.CloseDrawer();
        }
    }
}


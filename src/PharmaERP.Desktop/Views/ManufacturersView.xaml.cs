using System.Windows.Controls;
using System.Windows.Input;
using PharmaERP.Desktop.ViewModels;

namespace PharmaERP.Desktop.Views;

public partial class ManufacturersView : UserControl
{
    public ManufacturersView()
    {
        InitializeComponent();
    }

    private void OnScrimMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ManufacturersViewModel vm && vm.IsDrawerOpen)
        {
            vm.CloseDrawer();
        }
    }
}


using System.Windows.Controls;
using System.Windows.Input;
using PharmaERP.Desktop.ViewModels;

namespace PharmaERP.Desktop.Views;

public partial class UnitsView : UserControl
{
    public UnitsView()
    {
        InitializeComponent();
    }

    private void OnScrimMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is UnitsViewModel vm && vm.IsDrawerOpen)
        {
            vm.CloseDrawer();
        }
    }
}


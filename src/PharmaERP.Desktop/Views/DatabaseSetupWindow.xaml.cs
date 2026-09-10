using System.Windows;
using PharmaERP.Desktop.ViewModels;

namespace PharmaERP.Desktop.Views;

public partial class DatabaseSetupWindow : Window
{
    private readonly DatabaseSetupViewModel _viewModel;

    public DatabaseSetupWindow(DatabaseSetupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        if (!string.IsNullOrEmpty(_viewModel.Password))
        {
            SqlPasswordBox.Password = _viewModel.Password;
        }

        _viewModel.RequestClose += OnRequestClose;
    }

    private void OnRequestClose(bool success)
    {
        DialogResult = success;
        Close();
    }

    private void SqlPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.Password = SqlPasswordBox.Password;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        base.OnClosed(e);
    }
}


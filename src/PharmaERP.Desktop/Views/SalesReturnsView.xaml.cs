using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaERP.Desktop.ViewModels;

namespace PharmaERP.Desktop.Views;

public partial class SalesReturnsView : UserControl
{
    public SalesReturnsView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (DataContext is SalesReturnsViewModel vm)
            {
                vm.RequestPromptInput += PromptReason;
            }
        };
    }

    private void InvoiceSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SalesReturnsViewModel vm)
        {
            if (vm.SearchOriginalInvoiceCommand.CanExecute(null))
            {
                vm.SearchOriginalInvoiceCommand.Execute(null);
            }
        }
    }

    private string? PromptReason(string prompt)
    {
        var inputWindow = new Window
        {
            Title = "Cancellation Reason",
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8), FontWeight = FontWeights.SemiBold });

        var textBox = new TextBox { Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(textBox);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnOk = new Button { Content = "Confirm", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var btnCancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };

        string? result = null;
        btnOk.Click += (_, _) =>
        {
            result = textBox.Text;
            inputWindow.DialogResult = true;
            inputWindow.Close();
        };

        btnPanel.Children.Add(btnOk);
        btnPanel.Children.Add(btnCancel);
        panel.Children.Add(btnPanel);
        inputWindow.Content = panel;

        textBox.Focus();
        return inputWindow.ShowDialog() == true ? result : null;
    }
}


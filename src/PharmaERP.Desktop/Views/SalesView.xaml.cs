using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using PharmaERP.Application.DTOs;
using PharmaERP.Desktop.Services;
using PharmaERP.Desktop.ViewModels;

namespace PharmaERP.Desktop.Views;

public partial class SalesView : UserControl
{
    public SalesView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (DataContext is SalesViewModel vm)
            {
                vm.RequestShowPrintPreview += ShowPrintPreviewDialog;
                vm.RequestPromptInput += PromptReason;
            }
        };
    }

    private void ShowPrintPreviewDialog(InvoicePrintDataDto printData)
    {
        var printService = App.Services.GetRequiredService<InvoicePrintService>();
        var configService = App.Services.GetRequiredService<WorkstationConfigService>();

        var previewVm = new InvoicePrintPreviewViewModel(printService, configService, printData);
        var dialog = new InvoicePrintPreviewDialog
        {
            DataContext = previewVm,
            Owner = Window.GetWindow(this)
        };

        dialog.ShowDialog();
    }

    private string? PromptReason(string prompt)
    {
        // Simple input prompt using standard Windows input or messagebox
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


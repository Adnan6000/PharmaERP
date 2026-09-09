using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaERP.Application.DTOs;
using PharmaERP.Desktop.Services;
using PharmaERP.Desktop.ViewModels;

namespace PharmaERP.Desktop.Views;

public partial class SalesEntryView : UserControl
{
    public SalesEntryView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (DataContext is SalesEntryViewModel vm)
            {
                vm.RequestFocusSearch += () =>
                {
                    SearchBox.Focus();
                    SearchBox.SelectAll();
                };

                vm.RequestShowPrintPreview += ShowPrintPreviewDialog;
            }

            SearchBox.Focus();
        };
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SalesEntryViewModel vm)
        {
            if (vm.SearchOrScanCommand.CanExecute(null))
            {
                vm.SearchOrScanCommand.Execute(null);
            }
        }
    }

    private void TenderBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SalesEntryViewModel vm)
        {
            if (vm.CompleteSaleCommand.CanExecute(null))
            {
                vm.CompleteSaleCommand.Execute(null);
            }
        }
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
}


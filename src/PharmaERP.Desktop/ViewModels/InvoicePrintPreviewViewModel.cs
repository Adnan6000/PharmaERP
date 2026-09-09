using System.Windows.Documents;
using System.Windows.Input;
using PharmaERP.Application.DTOs;
using PharmaERP.Desktop.Common;
using PharmaERP.Desktop.Models;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.ViewModels;

public class InvoicePrintPreviewViewModel : ViewModelBase
{
    private readonly InvoicePrintService _printService;
    private readonly WorkstationConfigService _configService;
    private readonly InvoicePrintDataDto _printData;

    private InvoicePrintFormat _selectedFormat;
    private FlowDocument _document = null!;
    private string _statusMessage = string.Empty;

    public InvoicePrintPreviewViewModel(
        InvoicePrintService printService,
        WorkstationConfigService configService,
        InvoicePrintDataDto printData,
        InvoicePrintFormat? initialFormat = null)
    {
        _printService = printService;
        _configService = configService;
        _printData = printData;

        _selectedFormat = initialFormat ?? _configService.GetConfig().DefaultFormat;
        RefreshDocument();

        PrintCommand = new RelayCommand(_ => Print());
        SetA4Command = new RelayCommand(_ => SetFormat(InvoicePrintFormat.A4));
        SetThermalCommand = new RelayCommand(_ => SetFormat(InvoicePrintFormat.Thermal80mm));
    }

    public InvoicePrintDataDto PrintData => _printData;

    public InvoicePrintFormat SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (SetProperty(ref _selectedFormat, value))
            {
                OnPropertyChanged(nameof(IsA4Selected));
                OnPropertyChanged(nameof(IsThermalSelected));
                RefreshDocument();
            }
        }
    }

    public bool IsA4Selected => SelectedFormat == InvoicePrintFormat.A4;
    public bool IsThermalSelected => SelectedFormat == InvoicePrintFormat.Thermal80mm;

    public FlowDocument Document
    {
        get => _document;
        set => SetProperty(ref _document, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ICommand PrintCommand { get; }
    public ICommand SetA4Command { get; }
    public ICommand SetThermalCommand { get; }

    public event Action? RequestClose;

    private void SetFormat(InvoicePrintFormat format)
    {
        SelectedFormat = format;
    }

    private void RefreshDocument()
    {
        Document = _printService.BuildDocument(_printData, _selectedFormat);
    }

    private void Print()
    {
        try
        {
            _printService.PrintDocument(Document, documentTitle: $"Invoice {_printData.InvoiceNumber}");
            StatusMessage = "Printed successfully.";
            RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Print failed: {ex.Message}";
        }
    }
}


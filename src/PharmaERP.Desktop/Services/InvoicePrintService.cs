using System.Printing;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.DTOs;
using PharmaERP.Desktop.Models;
using PharmaERP.Desktop.Services.FlowDocumentBuilders;

namespace PharmaERP.Desktop.Services;

public class InvoicePrintService
{
    private readonly WorkstationConfigService _configService;
    private readonly ILogger<InvoicePrintService> _logger;

    public InvoicePrintService(
        WorkstationConfigService configService,
        ILogger<InvoicePrintService> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public FlowDocument BuildDocument(InvoicePrintDataDto invoice, InvoicePrintFormat format)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return format switch
        {
            InvoicePrintFormat.A4 => A4InvoiceDocumentBuilder.Build(invoice),
            InvoicePrintFormat.Thermal80mm => ThermalReceiptDocumentBuilder.Build(invoice),
            _ => ThermalReceiptDocumentBuilder.Build(invoice)
        };
    }

    public bool PrintDocument(FlowDocument document, string? printerName = null, string documentTitle = "PharmaERP Invoice")
    {
        ArgumentNullException.ThrowIfNull(document);

        var printDialog = new PrintDialog();

        if (!string.IsNullOrWhiteSpace(printerName))
        {
            try
            {
                var printServer = new LocalPrintServer();
                var queue = printServer.GetPrintQueue(printerName);
                printDialog.PrintQueue = queue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not set printer to '{PrinterName}'. Using default system printer.", printerName);
            }
        }

        try
        {
            IDocumentPaginatorSource idp = document;
            printDialog.PrintDocument(idp.DocumentPaginator, documentTitle);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to print document '{DocumentTitle}'", documentTitle);
            throw;
        }
    }

    public bool PrintInvoice(InvoicePrintDataDto invoice, InvoicePrintFormat? format = null, string? printerName = null)
    {
        var config = _configService.GetConfig();
        var selectedFormat = format ?? config.DefaultFormat;
        var selectedPrinter = printerName ?? config.PrinterName;

        var doc = BuildDocument(invoice, selectedFormat);
        return PrintDocument(doc, selectedPrinter, $"Invoice {invoice.InvoiceNumber}");
    }
}


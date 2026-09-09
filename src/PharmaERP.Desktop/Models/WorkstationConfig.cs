namespace PharmaERP.Desktop.Models;

public class WorkstationConfig
{
    public string? PrinterName { get; set; }
    public InvoicePrintFormat DefaultFormat { get; set; } = InvoicePrintFormat.Thermal80mm;
    public bool SilentPrint { get; set; } = false;
    public bool ShowPreviewBeforePrint { get; set; } = true;
}


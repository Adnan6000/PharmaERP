using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Enums;
using PharmaERP.Desktop.Services;

namespace PharmaERP.Desktop.Services.FlowDocumentBuilders;

public static class ThermalReceiptDocumentBuilder
{
    public static FlowDocument Build(InvoicePrintDataDto invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        // 80mm thermal receipt has ~280pt printable width at standard DPI
        var doc = new FlowDocument
        {
            PageWidth = 280,
            PagePadding = new Thickness(8, 12, 8, 12),
            ColumnWidth = 264,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 9.5,
            Foreground = Brushes.Black,
            Background = Brushes.White
        };

        // 1. Header (Centered)
        var pHeader = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        pHeader.Inlines.Add(new Run(invoice.PharmacyName.ToUpperInvariant()) { FontSize = 13, FontWeight = FontWeights.Bold });
        pHeader.Inlines.Add(new LineBreak());
        pHeader.Inlines.Add(new Run(invoice.PharmacyAddress) { FontSize = 8.5 });
        pHeader.Inlines.Add(new LineBreak());
        pHeader.Inlines.Add(new Run($"Tel: {invoice.PharmacyPhone}") { FontSize = 8.5 });
        if (!string.IsNullOrWhiteSpace(invoice.DrugLicenseNo))
        {
            pHeader.Inlines.Add(new LineBreak());
            pHeader.Inlines.Add(new Run($"Lic: {invoice.DrugLicenseNo}") { FontSize = 8 });
        }
        doc.Blocks.Add(pHeader);

        AddDashedLine(doc);

        // 2. Metadata Block
        var pMeta = new Paragraph { Margin = new Thickness(0, 2, 0, 4), FontSize = 9 };
        pMeta.Inlines.Add(new Run($"Rcpt #: {invoice.InvoiceNumber}") { FontWeight = FontWeights.Bold });
        pMeta.Inlines.Add(new LineBreak());
        pMeta.Inlines.Add(new Run($"Date  : {invoice.BusinessDate:dd/MM/yyyy} {invoice.InvoiceDateTimeUtc:HH:mm}"));
        pMeta.Inlines.Add(new LineBreak());
        pMeta.Inlines.Add(new Run($"Cust  : {invoice.CustomerName}"));
        pMeta.Inlines.Add(new LineBreak());
        pMeta.Inlines.Add(new Run($"Type  : {(invoice.SaleType == SaleType.Credit ? "CREDIT" : "CASH")}"));
        if (!string.IsNullOrWhiteSpace(invoice.Reference))
        {
            pMeta.Inlines.Add(new LineBreak());
            pMeta.Inlines.Add(new Run($"Ref   : {invoice.Reference}"));
        }
        doc.Blocks.Add(pMeta);

        AddDashedLine(doc);

        // 3. Items List
        var itemsTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 4) };
        itemsTable.Columns.Add(new TableColumn { Width = new GridLength(140) }); // Item name / details
        itemsTable.Columns.Add(new TableColumn { Width = new GridLength(50) });  // Qty x Rate
        itemsTable.Columns.Add(new TableColumn { Width = new GridLength(74) });  // Total

        var headerGroup = new TableRowGroup();
        var hRow = new TableRow();
        hRow.Cells.Add(new TableCell(new Paragraph(new Run("Item")) { FontWeight = FontWeights.Bold }));
        hRow.Cells.Add(new TableCell(new Paragraph(new Run("Qty")) { FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Right }));
        hRow.Cells.Add(new TableCell(new Paragraph(new Run("Amount")) { FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Right }));
        headerGroup.Rows.Add(hRow);
        itemsTable.RowGroups.Add(headerGroup);

        var bodyGroup = new TableRowGroup();
        foreach (var item in invoice.Items)
        {
            var tr = new TableRow();

            // Name
            var nameCell = new TableCell();
            nameCell.Blocks.Add(new Paragraph(new Run(item.ProductName)) { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0) });

            // Multi-batch breakdown (never showing cost)
            foreach (var alloc in item.BatchAllocations)
            {
                nameCell.Blocks.Add(new Paragraph(new Run($" B:{alloc.BatchNumber} E:{alloc.ExpiryDate:MM/yy} ({alloc.Quantity:0.##})"))
                {
                    FontSize = 8,
                    Foreground = Brushes.DimGray,
                    Margin = new Thickness(2, 0, 0, 0)
                });
            }
            tr.Cells.Add(nameCell);

            // Qty
            tr.Cells.Add(new TableCell(new Paragraph(new Run($"{item.Quantity:0.##}"))
            {
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 2, 0, 0)
            }));

            // Net Line Amount
            tr.Cells.Add(new TableCell(new Paragraph(new Run(item.NetLineAmount.ToString("N2", CultureInfo.InvariantCulture)))
            {
                TextAlignment = TextAlignment.Right,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 0)
            }));

            bodyGroup.Rows.Add(tr);
        }
        itemsTable.RowGroups.Add(bodyGroup);
        doc.Blocks.Add(itemsTable);

        AddDashedLine(doc);

        // 4. Totals Block
        var pTotals = new Paragraph { TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 2, 0, 4) };
        pTotals.Inlines.Add(new Run($"Gross Total: {AppCurrency.Format(invoice.GrossTotal)}"));
        pTotals.Inlines.Add(new LineBreak());

        decimal totalDisc = invoice.LineDiscountTotal + invoice.InvoiceDiscountAmount;
        if (totalDisc > 0)
        {
            pTotals.Inlines.Add(new Run($"Discount   : -{AppCurrency.Format(totalDisc)}"));
            pTotals.Inlines.Add(new LineBreak());
        }

        pTotals.Inlines.Add(new Run($"NET TOTAL  : {AppCurrency.Format(invoice.NetTotal)}") { FontWeight = FontWeights.Bold, FontSize = 11 });
        pTotals.Inlines.Add(new LineBreak());

        if (invoice.TenderedAmount.HasValue)
        {
            pTotals.Inlines.Add(new Run($"Tendered   : {AppCurrency.Format(invoice.TenderedAmount.Value)}"));
            pTotals.Inlines.Add(new LineBreak());
            if (invoice.ChangeGiven.HasValue)
            {
                pTotals.Inlines.Add(new Run($"Change     : {AppCurrency.Format(invoice.ChangeGiven.Value)}"));
                pTotals.Inlines.Add(new LineBreak());
            }
        }
        doc.Blocks.Add(pTotals);

        AddDashedLine(doc);

        // 5. Barcode / Receipt Number & Footer
        var pFooter = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        pFooter.Inlines.Add(new Run($"* {invoice.InvoiceNumber} *") { FontWeight = FontWeights.Bold, FontSize = 10 });
        pFooter.Inlines.Add(new LineBreak());
        pFooter.Inlines.Add(new Run(invoice.ReceiptFooter) { FontSize = 8, Foreground = Brushes.DimGray });
        doc.Blocks.Add(pFooter);

        return doc;
    }

    private static void AddDashedLine(FlowDocument doc)
    {
        var p = new Paragraph(new Run("--------------------------------"))
        {
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 1, 0, 1),
            Foreground = Brushes.Gray,
            FontSize = 9
        };
        doc.Blocks.Add(p);
    }
}


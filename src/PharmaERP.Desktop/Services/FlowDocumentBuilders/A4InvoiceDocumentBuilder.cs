using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Desktop.Services.FlowDocumentBuilders;

public static class A4InvoiceDocumentBuilder
{
    public static FlowDocument Build(InvoicePrintDataDto invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var doc = new FlowDocument
        {
            PageWidth = 793,
            PageHeight = 1122,
            PagePadding = new Thickness(40),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Foreground = Brushes.Black,
            Background = Brushes.White
        };

        // 1. Header Section
        var headerTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 15) };
        headerTable.Columns.Add(new TableColumn { Width = new GridLength(450) });
        headerTable.Columns.Add(new TableColumn { Width = new GridLength(260) });

        var headerRowGroup = new TableRowGroup();
        var headerRow = new TableRow();

        // Left Header: Pharmacy Info
        var leftCell = new TableCell();
        leftCell.Blocks.Add(new Paragraph(new Run(invoice.PharmacyName))
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        leftCell.Blocks.Add(new Paragraph(new Run(invoice.PharmacyAddress)) { FontSize = 11, Margin = new Thickness(0, 0, 0, 2), Foreground = Brushes.DimGray });
        leftCell.Blocks.Add(new Paragraph(new Run($"Phone: {invoice.PharmacyPhone} | License: {invoice.DrugLicenseNo}")) { FontSize = 10, Margin = new Thickness(0, 0, 0, 2), Foreground = Brushes.DimGray });
        if (!string.IsNullOrWhiteSpace(invoice.TaxNumber))
        {
            leftCell.Blocks.Add(new Paragraph(new Run($"Tax Reg: {invoice.TaxNumber}")) { FontSize = 10, Margin = new Thickness(0), Foreground = Brushes.DimGray });
        }

        // Right Header: Invoice Badge & Details
        var rightCell = new TableCell { TextAlignment = TextAlignment.Right };
        rightCell.Blocks.Add(new Paragraph(new Run("TAX INVOICE"))
        {
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 76, 129)),
            Margin = new Thickness(0, 0, 0, 6)
        });
        rightCell.Blocks.Add(new Paragraph(new Run($"Invoice #: {invoice.InvoiceNumber}")) { FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 2) });
        rightCell.Blocks.Add(new Paragraph(new Run($"Date: {invoice.BusinessDate:yyyy-MM-dd} {invoice.InvoiceDateTimeUtc:HH:mm} UTC")) { FontSize = 10, Margin = new Thickness(0, 0, 0, 2), Foreground = Brushes.DimGray });
        rightCell.Blocks.Add(new Paragraph(new Run($"Type: {(invoice.SaleType == SaleType.Credit ? "CREDIT SALE" : "CASH SALE")}")) { FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0) });

        headerRow.Cells.Add(leftCell);
        headerRow.Cells.Add(rightCell);
        headerRowGroup.Rows.Add(headerRow);
        headerTable.RowGroups.Add(headerRowGroup);
        doc.Blocks.Add(headerTable);

        // Divider
        doc.Blocks.Add(new BlockUIContainer(new System.Windows.Shapes.Rectangle
        {
            Height = 1.5,
            Fill = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            Margin = new Thickness(0, 0, 0, 15)
        }));

        // 2. Customer & Reference Box
        var custTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 15) };
        custTable.Columns.Add(new TableColumn { Width = new GridLength(355) });
        custTable.Columns.Add(new TableColumn { Width = new GridLength(355) });
        var custRowGroup = new TableRowGroup();
        var custRow = new TableRow();

        var custCellLeft = new TableCell();
        custCellLeft.Blocks.Add(new Paragraph(new Run($"Customer: {invoice.CustomerName}")) { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        if (!string.IsNullOrWhiteSpace(invoice.Remarks))
        {
            custCellLeft.Blocks.Add(new Paragraph(new Run($"Remarks: {invoice.Remarks}")) { FontSize = 10, Foreground = Brushes.DimGray, Margin = new Thickness(0) });
        }

        var custCellRight = new TableCell { TextAlignment = TextAlignment.Right };
        if (!string.IsNullOrWhiteSpace(invoice.Reference))
        {
            custCellRight.Blocks.Add(new Paragraph(new Run($"Prescription / Ref: {invoice.Reference}")) { FontSize = 11, Margin = new Thickness(0) });
        }

        custRow.Cells.Add(custCellLeft);
        custRow.Cells.Add(custCellRight);
        custRowGroup.Rows.Add(custRow);
        custTable.RowGroups.Add(custRowGroup);
        doc.Blocks.Add(custTable);

        // 3. Line Items Table
        var itemsTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 15) };
        itemsTable.Columns.Add(new TableColumn { Width = new GridLength(35) });  // #
        itemsTable.Columns.Add(new TableColumn { Width = new GridLength(280) }); // Description
        itemsTable.Columns.Add(new TableColumn { Width = new GridLength(60) });  // Unit
        itemsTable.Columns.Add(new TableColumn { Width = new GridLength(65) });  // Qty
        itemsTable.Columns.Add(new TableColumn { Width = new GridLength(85) });  // Rate
        itemsTable.Columns.Add(new TableColumn { Width = new GridLength(75) });  // Disc
        itemsTable.Columns.Add(new TableColumn { Width = new GridLength(110) }); // Net

        // Header Row
        var itemHeaderGroup = new TableRowGroup();
        var thRow = new TableRow { Background = new SolidColorBrush(Color.FromRgb(245, 247, 250)) };
        AddHeaderCell(thRow, "#", TextAlignment.Left);
        AddHeaderCell(thRow, "Item Description", TextAlignment.Left);
        AddHeaderCell(thRow, "Unit", TextAlignment.Center);
        AddHeaderCell(thRow, "Qty", TextAlignment.Right);
        AddHeaderCell(thRow, "Price", TextAlignment.Right);
        AddHeaderCell(thRow, "Discount", TextAlignment.Right);
        AddHeaderCell(thRow, $"Net Total ({AppCurrency.Symbol})", TextAlignment.Right);
        itemHeaderGroup.Rows.Add(thRow);
        itemsTable.RowGroups.Add(itemHeaderGroup);

        // Body Rows
        var itemBodyGroup = new TableRowGroup();
        for (int i = 0; i < invoice.Items.Count; i++)
        {
            var item = invoice.Items[i];
            var tr = new TableRow();
            var bg = (i % 2 == 1) ? new SolidColorBrush(Color.FromRgb(252, 252, 253)) : Brushes.White;
            tr.Background = bg;

            // #
            tr.Cells.Add(new TableCell(new Paragraph(new Run(item.LineNumber.ToString())) { Margin = new Thickness(4, 6, 4, 6), FontSize = 10, Foreground = Brushes.Gray }));

            // Description + Multi-Batch sublines
            var descCell = new TableCell();
            descCell.Blocks.Add(new Paragraph(new Run(item.ProductName)) { FontWeight = FontWeights.Medium, Margin = new Thickness(4, 6, 4, 2) });
            if (!string.IsNullOrWhiteSpace(item.ProductCode))
            {
                descCell.Blocks.Add(new Paragraph(new Run($"Code: {item.ProductCode}")) { FontSize = 9, Foreground = Brushes.DimGray, Margin = new Thickness(4, 0, 4, 2) });
            }

            // Multi-Batch allocation sub-breakdown (never showing cost!)
            foreach (var alloc in item.BatchAllocations)
            {
                descCell.Blocks.Add(new Paragraph(new Run($"↳ Batch: {alloc.BatchNumber} | Exp: {alloc.ExpiryDate:MM/yy} | Qty: {alloc.Quantity:0.##}"))
                {
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(80, 100, 120)),
                    Margin = new Thickness(10, 0, 4, 1)
                });
            }
            tr.Cells.Add(descCell);

            // Unit
            tr.Cells.Add(new TableCell(new Paragraph(new Run(item.Unit)) { Margin = new Thickness(4, 6, 4, 6), TextAlignment = TextAlignment.Center, FontSize = 10 }));

            // Qty
            tr.Cells.Add(new TableCell(new Paragraph(new Run(item.Quantity.ToString("0.##", CultureInfo.InvariantCulture))) { Margin = new Thickness(4, 6, 4, 6), TextAlignment = TextAlignment.Right, FontWeight = FontWeights.Medium }));

            // Price
            tr.Cells.Add(new TableCell(new Paragraph(new Run(item.UnitSalePrice.ToString("N2", CultureInfo.InvariantCulture))) { Margin = new Thickness(4, 6, 4, 6), TextAlignment = TextAlignment.Right }));

            // Disc (Line + Prorated Invoice Discount)
            decimal totalLineDisc = item.LineDiscountAmount + item.InvoiceDiscountAllocated;
            tr.Cells.Add(new TableCell(new Paragraph(new Run(totalLineDisc > 0 ? totalLineDisc.ToString("N2", CultureInfo.InvariantCulture) : "-")) { Margin = new Thickness(4, 6, 4, 6), TextAlignment = TextAlignment.Right, Foreground = totalLineDisc > 0 ? Brushes.Crimson : Brushes.Black }));

            // Net Line Amount
            tr.Cells.Add(new TableCell(new Paragraph(new Run(item.NetLineAmount.ToString("N2", CultureInfo.InvariantCulture))) { Margin = new Thickness(4, 6, 4, 6), TextAlignment = TextAlignment.Right, FontWeight = FontWeights.SemiBold }));

            itemBodyGroup.Rows.Add(tr);
        }
        itemsTable.RowGroups.Add(itemBodyGroup);
        doc.Blocks.Add(itemsTable);

        // 4. Totals Section
        var totalsTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 10, 0, 20) };
        totalsTable.Columns.Add(new TableColumn { Width = new GridLength(450) });
        totalsTable.Columns.Add(new TableColumn { Width = new GridLength(260) });

        var totalsRowGroup = new TableRowGroup();
        var totalsRow = new TableRow();

        var noteCell = new TableCell();
        noteCell.Blocks.Add(new Paragraph(new Run(invoice.ReceiptFooter)) { FontSize = 10, Foreground = Brushes.DimGray, Margin = new Thickness(0, 10, 0, 0) });

        var sumCell = new TableCell { TextAlignment = TextAlignment.Right };
        sumCell.Blocks.Add(CreateSummaryLine("Gross Total:", AppCurrency.Format(invoice.GrossTotal)));
        if (invoice.LineDiscountTotal > 0)
            sumCell.Blocks.Add(CreateSummaryLine("Line Discounts:", $"-{AppCurrency.Format(invoice.LineDiscountTotal)}"));
        if (invoice.InvoiceDiscountAmount > 0)
            sumCell.Blocks.Add(CreateSummaryLine("Invoice Discount:", $"-{AppCurrency.Format(invoice.InvoiceDiscountAmount)}"));

        // Net Payable Box
        var netPara = new Paragraph();
        netPara.Inlines.Add(new Run("Net Payable: ") { FontSize = 14, FontWeight = FontWeights.Bold });
        netPara.Inlines.Add(new Run(AppCurrency.Format(invoice.NetTotal)) { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(15, 76, 129)) });
        netPara.Margin = new Thickness(0, 6, 0, 4);
        sumCell.Blocks.Add(netPara);

        if (invoice.TenderedAmount.HasValue)
        {
            sumCell.Blocks.Add(CreateSummaryLine("Cash Tendered:", AppCurrency.Format(invoice.TenderedAmount.Value)));
            if (invoice.ChangeGiven.HasValue)
                sumCell.Blocks.Add(CreateSummaryLine("Change Returned:", AppCurrency.Format(invoice.ChangeGiven.Value)));
        }

        totalsRow.Cells.Add(noteCell);
        totalsRow.Cells.Add(sumCell);
        totalsRowGroup.Rows.Add(totalsRow);
        totalsTable.RowGroups.Add(totalsRowGroup);
        doc.Blocks.Add(totalsTable);

        // Signatures
        var sigPara = new Paragraph(new Run("_______________________\nAuthorized Signature"))
        {
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 30, 10, 0),
            FontSize = 10,
            Foreground = Brushes.Gray
        };
        doc.Blocks.Add(sigPara);

        return doc;
    }

    private static void AddHeaderCell(TableRow row, string text, TextAlignment alignment)
    {
        var cell = new TableCell(new Paragraph(new Run(text))
        {
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Margin = new Thickness(4, 8, 4, 8),
            TextAlignment = alignment
        });
        row.Cells.Add(cell);
    }

    private static Paragraph CreateSummaryLine(string label, string value)
    {
        var p = new Paragraph { Margin = new Thickness(0, 1, 0, 1), FontSize = 11 };
        p.Inlines.Add(new Run(label) { Foreground = Brushes.DimGray });
        p.Inlines.Add(new Run($" {value}") { FontWeight = FontWeights.Medium });
        return p;
    }
}


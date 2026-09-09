using PharmaERP.Domain.Common;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Domain.Entities;

public class SaleReturn : BaseEntity
{
    public string ReturnNumber { get; set; } = string.Empty;

    public Guid OperationId { get; set; }

    public int OriginalSaleInvoiceId { get; set; }
    public SaleInvoice OriginalSaleInvoice { get; set; } = null!;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string CustomerNameSnapshot { get; set; } = string.Empty;

    public DateOnly ReturnDate { get; set; }

    public string? Reason { get; set; }

    public decimal TotalAmount { get; set; }

    public SaleReturnStatus Status { get; set; } = SaleReturnStatus.Draft;

    public DateTime? PostedAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public string? CancellationReason { get; set; }

    public ICollection<SaleReturnItem> Items { get; set; } = new List<SaleReturnItem>();
}


using PharmaERP.Domain.Common;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Domain.Entities;

/// <summary>
/// Opening Balance Voucher representing financial opening balance setup.
/// Initial customer/supplier/cash/bank balances are recorded with an automatic balancing line to Opening Balance Equity.
/// </summary>
public class OpeningBalanceVoucher : BaseEntity
{
    public string VoucherNumber { get; set; } = string.Empty; // "OB-YYYY-XXXXXX"

    public DateOnly BusinessDate { get; set; }

    public DateTime PostedAtUtc { get; set; }

    public string? Reference { get; set; }

    public string? Narration { get; set; }

    public VoucherStatus Status { get; set; } = VoucherStatus.Posted;

    public Guid OperationId { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public string? CancellationReason { get; set; }
}

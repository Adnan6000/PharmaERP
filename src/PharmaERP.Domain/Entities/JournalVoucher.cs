using PharmaERP.Domain.Common;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Domain.Entities;

/// <summary>
/// Journal Voucher representing manual multi-line adjustments, provisions, and corrections.
/// </summary>
public class JournalVoucher : BaseEntity
{
    public string VoucherNumber { get; set; } = string.Empty; // "JV-2026-000001"

    public DateOnly BusinessDate { get; set; }

    public DateTime PostedAtUtc { get; set; }

    public string? Reference { get; set; }

    public string? Narration { get; set; }

    public decimal TotalAmount { get; set; }

    public VoucherStatus Status { get; set; } = VoucherStatus.Posted;

    public Guid OperationId { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public string? CancellationReason { get; set; }
}

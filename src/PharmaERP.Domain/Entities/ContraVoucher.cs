using PharmaERP.Domain.Common;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Domain.Entities;

/// <summary>
/// Contra Voucher representing cash-to-bank, bank-to-cash, or bank-to-bank transfers.
/// Non-circular relation to JournalEntry via SourceDocumentType / SourceDocumentId.
/// </summary>
public class ContraVoucher : BaseEntity
{
    public string VoucherNumber { get; set; } = string.Empty; // ""CV-2026-000001""

    public DateOnly BusinessDate { get; set; }

    public DateTime PostedAtUtc { get; set; }

    public int SourceAccountId { get; set; }
    public Account SourceAccount { get; set; } = null!;

    public int DestinationAccountId { get; set; }
    public Account DestinationAccount { get; set; } = null!;

    public decimal Amount { get; set; }

    public string? Reference { get; set; }

    public string? Narration { get; set; }

    public VoucherStatus Status { get; set; } = VoucherStatus.Posted;

    public Guid OperationId { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public string? CancellationReason { get; set; }
}

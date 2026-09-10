using PharmaERP.Domain.Common;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Domain.Entities;

/// <summary>
/// General Ledger Journal Entry header.
/// Posted journal entries are permanently immutable. Cancellations append a new compensating reversal entry.
/// </summary>
public class JournalEntry : BaseEntity
{
    public string EntryNumber { get; set; } = string.Empty; // e.g. "JE-2026-000001"

    public DateOnly BusinessDate { get; set; }

    public DateTime PostedAtUtc { get; set; }

    public string? Narration { get; set; }

    public JournalSourceDocumentType SourceDocumentType { get; set; } = JournalSourceDocumentType.Manual;

    public int? SourceDocumentId { get; set; }

    public string? SourceDocumentNumber { get; set; }

    public JournalPostingRole PostingRole { get; set; } = JournalPostingRole.Primary;

    public JournalEntryStatus Status { get; set; } = JournalEntryStatus.Draft;

    public Guid OperationId { get; set; }

    public int? ReversesJournalEntryId { get; set; }
    public JournalEntry? ReversesJournalEntry { get; set; }

    public decimal TotalDebit { get; set; }

    public decimal TotalCredit { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public string? CancellationReason { get; set; }

    public ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();
}

using PharmaERP.Domain.Common;

namespace PharmaERP.Domain.Entities;

/// <summary>
/// Individual debit or credit line of a Journal Entry.
/// Mutex constraint: Cannot reference both Customer and Supplier simultaneously.
/// Line amounts follow: (DebitAmount > 0 XOR CreditAmount > 0).
/// </summary>
public class JournalEntryLine : BaseEntity
{
    public int JournalEntryId { get; set; }
    public JournalEntry JournalEntry { get; set; } = null!;

    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public decimal DebitAmount { get; set; }

    public decimal CreditAmount { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public string? Narration { get; set; }
}

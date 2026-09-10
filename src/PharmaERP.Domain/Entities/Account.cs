using PharmaERP.Domain.Common;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Domain.Entities;

/// <summary>
/// Hierarchical Chart of Accounts entity representing general ledger accounts.
/// Balances are never stored as mutable truth; they are dynamically aggregated from Posted journal lines.
/// </summary>
public class Account : BaseEntity
{
    public string AccountCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public AccountType AccountType { get; set; }

    public int? ParentAccountId { get; set; }
    public Account? ParentAccount { get; set; }

    /// <summary>
    /// False for header/group accounts; True for postable leaf accounts.
    /// </summary>
    public bool AllowPosting { get; set; } = true;

    /// <summary>
    /// Indicates control accounts like Accounts Receivable Control or Accounts Payable Control.
    /// </summary>
    public bool IsControlAccount { get; set; }

    /// <summary>
    /// Indicates cash on hand accounts.
    /// </summary>
    public bool IsCashAccount { get; set; }

    /// <summary>
    /// Indicates bank accounts. Multiple bank accounts may have IsBankAccount = true.
    /// </summary>
    public bool IsBankAccount { get; set; }

    /// <summary>
    /// Authoritative semantic system mapping (e.g. CashOnHand, Inventory, COGS).
    /// </summary>
    public SystemAccountType? SystemAccountType { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Description { get; set; }

    public ICollection<Account> SubAccounts { get; set; } = new List<Account>();

    public ICollection<JournalEntryLine> JournalLines { get; set; } = new List<JournalEntryLine>();
}

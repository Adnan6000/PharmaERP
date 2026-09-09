using PharmaERP.Domain.Common;

namespace PharmaERP.Domain.Entities;

/// <summary>
/// Core customer entity for client sales, credit limit checks, and invoicing.
/// Note: Opening financial balances are staged for the Accounts/Ledger milestone.
/// </summary>
public class Customer : BaseEntity
{
    public string CustomerCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? ContactPerson { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public decimal CreditLimit { get; set; }

    public bool IsActive { get; set; } = true;
}


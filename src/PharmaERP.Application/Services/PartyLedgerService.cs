using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Application.Services;

public class PartyLedgerService : IPartyLedgerService
{
    private readonly IJournalRepository _journalRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ISupplierRepository _supplierRepository;

    public PartyLedgerService(
        IJournalRepository journalRepository,
        IAccountRepository accountRepository,
        ICustomerRepository customerRepository,
        ISupplierRepository supplierRepository)
    {
        _journalRepository = journalRepository;
        _accountRepository = accountRepository;
        _customerRepository = customerRepository;
        _supplierRepository = supplierRepository;
    }

    public async Task<LedgerReportDto> GetCustomerLedgerAsync(
        int customerId,
        DateOnly fromDate,
        DateOnly toDate,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var cust = await _customerRepository.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException($"Customer with ID {customerId} was not found.");

        var arAccount = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl, cancellationToken)
            ?? throw new ValidationException("Accounts Receivable Control system account is not configured.");

        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 50;

        return await _journalRepository.GetCustomerLedgerAsync(customerId, arAccount.Id, fromDate, toDate, pageNumber, pageSize, cancellationToken);
    }

    public async Task<LedgerReportDto> GetSupplierLedgerAsync(
        int supplierId,
        DateOnly fromDate,
        DateOnly toDate,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var supp = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken)
            ?? throw new NotFoundException($"Supplier with ID {supplierId} was not found.");

        var apAccount = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.AccountsPayableControl, cancellationToken)
            ?? throw new ValidationException("Accounts Payable Control system account is not configured.");

        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 50;

        return await _journalRepository.GetSupplierLedgerAsync(supplierId, apAccount.Id, fromDate, toDate, pageNumber, pageSize, cancellationToken);
    }

    public async Task<decimal> GetCustomerBalanceAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var arAccount = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl, cancellationToken);
        if (arAccount == null) return 0m;

        return await _journalRepository.GetPartyRunningBalanceAsync(customerId, isCustomer: true, arAccount.Id, cancellationToken);
    }

    public async Task<decimal> GetSupplierBalanceAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        var apAccount = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.AccountsPayableControl, cancellationToken);
        if (apAccount == null) return 0m;

        return await _journalRepository.GetPartyRunningBalanceAsync(supplierId, isCustomer: false, apAccount.Id, cancellationToken);
    }
}

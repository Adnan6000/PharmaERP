using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;

namespace PharmaERP.Infrastructure.Persistence.Services;

public class HistoricalBackfillRunner : IHistoricalBackfillRunner
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IBusinessClock _businessClock;
    private readonly IAccountRepository _accountRepository;
    private readonly IDocumentNumberGenerator _documentNumberGenerator;
    private readonly ILogger<HistoricalBackfillRunner> _logger;

    public HistoricalBackfillRunner(
        IDbContextFactory<AppDbContext> contextFactory,
        IBusinessClock businessClock,
        IAccountRepository accountRepository,
        IDocumentNumberGenerator documentNumberGenerator,
        ILogger<HistoricalBackfillRunner> logger)
    {
        _contextFactory = contextFactory;
        _businessClock = businessClock;
        _accountRepository = accountRepository;
        _documentNumberGenerator = documentNumberGenerator;
        _logger = logger;
    }

    public async Task<HistoricalInitializationResultDto> ExecuteBackfillAsync(CancellationToken cancellationToken = default)
    {
        var result = new HistoricalInitializationResultDto
        {
            FinalState = AccountingSetupState.Initializing
        };

        // Validate required system accounts
        var invAcc = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.Inventory, cancellationToken)
            ?? throw new InvalidOperationException("System account for 'Inventory' is missing.");
        var cogsAcc = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.CostOfGoodsSold, cancellationToken)
            ?? throw new InvalidOperationException("System account for 'CostOfGoodsSold' is missing.");
        var arAcc = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl, cancellationToken)
            ?? throw new InvalidOperationException("System account for 'AccountsReceivableControl' is missing.");
        var apAcc = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.AccountsPayableControl, cancellationToken)
            ?? throw new InvalidOperationException("System account for 'AccountsPayableControl' is missing.");
        var cashAcc = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.CashOnHand, cancellationToken)
            ?? throw new InvalidOperationException("System account for 'CashOnHand' is missing.");
        var revAcc = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.SalesRevenue, cancellationToken)
            ?? throw new InvalidOperationException("System account for 'SalesRevenue' is missing.");
        var sDiscAcc = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.SalesDiscount, cancellationToken)
            ?? throw new InvalidOperationException("System account for 'SalesDiscount' is missing.");
        var sRetAcc = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.SalesReturns, cancellationToken)
            ?? throw new InvalidOperationException("System account for 'SalesReturns' is missing.");
        var pDiscAcc = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.PurchaseDiscount, cancellationToken)
            ?? throw new InvalidOperationException("System account for 'PurchaseDiscount' is missing.");
        var eqAcc = await _accountRepository.GetBySystemTypeAsync(SystemAccountType.OpeningBalanceEquity, cancellationToken)
            ?? throw new InvalidOperationException("System account for 'OpeningBalanceEquity' is missing.");

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        int createdCount = 0;
        int skippedCount = 0;

        // 1. OPENING STOCKS
        var openingMovements = await context.StockMovements
            .Where(m => m.MovementType == StockMovementType.OpeningStock)
            .OrderBy(m => m.TransactionDateUtc)
            .ToListAsync(cancellationToken);

        foreach (var m in openingMovements)
        {
            bool exists = await context.JournalEntries.AnyAsync(j =>
                j.SourceDocumentType == JournalSourceDocumentType.OpeningStock &&
                j.SourceDocumentId == m.Id &&
                j.PostingRole == JournalPostingRole.Primary, cancellationToken);

            if (exists)
            {
                skippedCount++;
                continue;
            }

            await using var docTx = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                var bDate = await _businessClock.ToBusinessDateAsync(m.TransactionDateUtc, cancellationToken);
                string jNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, bDate.Year, cancellationToken);

                var j = new JournalEntry
                {
                    EntryNumber = jNum,
                    BusinessDate = bDate,
                    PostedAtUtc = m.TransactionDateUtc,
                    Narration = $"Opening Stock: OSTK-{m.Id}",
                    SourceDocumentType = JournalSourceDocumentType.OpeningStock,
                    SourceDocumentId = m.Id,
                    SourceDocumentNumber = $"OSTK-{m.Id}",
                    PostingRole = JournalPostingRole.Primary,
                    Status = JournalEntryStatus.Draft,
                    OperationId = Guid.NewGuid()
                };

                j.Lines.Add(new JournalEntryLine
                {
                    AccountId = invAcc.Id,
                    DebitAmount = m.InventoryValueDelta,
                    CreditAmount = 0m,
                    Narration = $"Opening Stock: OSTK-{m.Id}"
                });

                j.Lines.Add(new JournalEntryLine
                {
                    AccountId = eqAcc.Id,
                    DebitAmount = 0m,
                    CreditAmount = m.InventoryValueDelta,
                    Narration = $"Opening Stock: OSTK-{m.Id}"
                });

                await AccountingTransactionWriter.PostTwoPhaseJournalAsync(context, j, cancellationToken);
                await docTx.CommitAsync(cancellationToken);
                createdCount++;
            }
            catch (Exception ex)
            {
                await docTx.RollbackAsync(cancellationToken);
                result.ErrorMessages.Add($"Opening stock #{m.Id} failed: {ex.Message}");
                return result with { TotalJournalsCreated = createdCount, TotalJournalsSkipped = skippedCount };
            }
        }
        result = result with { OpeningStocksProcessed = openingMovements.Count };

        // 2. PURCHASE INVOICES
        var purchases = await context.PurchaseInvoices
            .Include(p => p.Items)
            .OrderBy(p => p.InvoiceDate)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        foreach (var p in purchases)
        {
            if (p.TaxAmount != 0m)
            {
                result.ErrorMessages.Add($"Historical purchase {p.InvoiceNumber} contains non-zero TaxAmount ({p.TaxAmount}). Tax accounting is not supported.");
                return result with { TotalJournalsCreated = createdCount, TotalJournalsSkipped = skippedCount };
            }

            bool primaryExists = await context.JournalEntries.AnyAsync(j =>
                j.SourceDocumentType == JournalSourceDocumentType.PurchaseInvoice &&
                j.SourceDocumentId == p.Id &&
                j.PostingRole == JournalPostingRole.Primary, cancellationToken);

            JournalEntry? primaryJournal = null;

            if (!primaryExists)
            {
                await using var docTx = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                try
                {
                    decimal invInflow = await context.StockMovements
                        .Where(m => m.ReferenceDocumentType == StockReferenceDocumentType.PurchaseInvoice &&
                                    m.ReferenceDocumentId == p.Id &&
                                    m.MovementType == StockMovementType.Purchase)
                        .SumAsync(m => m.InventoryValueDelta, cancellationToken);

                    if (invInflow == 0m)
                    {
                        invInflow = p.GrossTotal;
                    }

                    string jNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, p.InvoiceDate.Year, cancellationToken);

                    primaryJournal = new JournalEntry
                    {
                        EntryNumber = jNum,
                        BusinessDate = p.InvoiceDate,
                        PostedAtUtc = p.PostedAtUtc ?? p.CreatedAtUtc,
                        Narration = $"Purchase Invoice: {p.InvoiceNumber}",
                        SourceDocumentType = JournalSourceDocumentType.PurchaseInvoice,
                        SourceDocumentId = p.Id,
                        SourceDocumentNumber = p.InvoiceNumber,
                        PostingRole = JournalPostingRole.Primary,
                        Status = JournalEntryStatus.Draft,
                        OperationId = Guid.NewGuid()
                    };

                    primaryJournal.Lines.Add(new JournalEntryLine
                    {
                        AccountId = invAcc.Id,
                        DebitAmount = invInflow,
                        CreditAmount = 0m,
                        Narration = $"Purchase: {p.InvoiceNumber}"
                    });

                    primaryJournal.Lines.Add(new JournalEntryLine
                    {
                        AccountId = apAcc.Id,
                        DebitAmount = 0m,
                        CreditAmount = p.NetTotal,
                        SupplierId = p.SupplierId,
                        Narration = $"Purchase: {p.InvoiceNumber}"
                    });

                    if (p.DiscountAmount > 0)
                    {
                        primaryJournal.Lines.Add(new JournalEntryLine
                        {
                            AccountId = pDiscAcc.Id,
                            DebitAmount = 0m,
                            CreditAmount = p.DiscountAmount,
                            Narration = $"Purchase Discount: {p.InvoiceNumber}"
                        });
                    }

                    await AccountingTransactionWriter.PostTwoPhaseJournalAsync(context, primaryJournal, cancellationToken);
                    await docTx.CommitAsync(cancellationToken);
                    createdCount++;
                }
                catch (Exception ex)
                {
                    await docTx.RollbackAsync(cancellationToken);
                    result.ErrorMessages.Add($"Purchase {p.InvoiceNumber} failed: {ex.Message}");
                    return result with { TotalJournalsCreated = createdCount, TotalJournalsSkipped = skippedCount };
                }
            }
            else
            {
                skippedCount++;
                primaryJournal = await context.JournalEntries
                    .Include(j => j.Lines)
                    .FirstAsync(j => j.SourceDocumentType == JournalSourceDocumentType.PurchaseInvoice &&
                                     j.SourceDocumentId == p.Id &&
                                     j.PostingRole == JournalPostingRole.Primary, cancellationToken);
            }

            // Cancellation reversal if cancelled
            if (p.Status == PurchaseInvoiceStatus.Cancelled)
            {
                bool revExists = await context.JournalEntries.AnyAsync(j =>
                    j.SourceDocumentType == JournalSourceDocumentType.PurchaseInvoice &&
                    j.SourceDocumentId == p.Id &&
                    j.PostingRole == JournalPostingRole.Reversal, cancellationToken);

                if (!revExists && primaryJournal != null)
                {
                    await using var revTx = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                    try
                    {
                        var revUtc = p.CancelledAtUtc ?? DateTime.UtcNow;
                        var revDate = await _businessClock.ToBusinessDateAsync(revUtc, cancellationToken);
                        string revNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, revDate.Year, cancellationToken);

                        var revJ = AccountingTransactionWriter.CreateReversalJournal(
                            primaryJournal, revNum, revDate, revUtc, p.CancellationReason ?? "Historical Cancellation");

                        await AccountingTransactionWriter.PostTwoPhaseJournalAsync(context, revJ, cancellationToken);
                        await revTx.CommitAsync(cancellationToken);
                        createdCount++;
                    }
                    catch (Exception ex)
                    {
                        await revTx.RollbackAsync(cancellationToken);
                        result.ErrorMessages.Add($"Purchase cancellation for {p.InvoiceNumber} failed: {ex.Message}");
                        return result with { TotalJournalsCreated = createdCount, TotalJournalsSkipped = skippedCount };
                    }
                }
                else
                {
                    skippedCount++;
                }
            }
        }
        result = result with { PurchasesProcessed = purchases.Count };

        // 3. PURCHASE RETURNS
        var purchaseReturns = await context.PurchaseReturns
            .OrderBy(r => r.ReturnDate)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        foreach (var r in purchaseReturns)
        {
            bool primaryExists = await context.JournalEntries.AnyAsync(j =>
                j.SourceDocumentType == JournalSourceDocumentType.PurchaseReturn &&
                j.SourceDocumentId == r.Id &&
                j.PostingRole == JournalPostingRole.Primary, cancellationToken);

            JournalEntry? primaryJournal = null;

            if (!primaryExists)
            {
                await using var docTx = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                try
                {
                    string jNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, r.ReturnDate.Year, cancellationToken);

                    primaryJournal = new JournalEntry
                    {
                        EntryNumber = jNum,
                        BusinessDate = r.ReturnDate,
                        PostedAtUtc = r.PostedAtUtc ?? r.CreatedAtUtc,
                        Narration = $"Purchase Return: {r.ReturnNumber}",
                        SourceDocumentType = JournalSourceDocumentType.PurchaseReturn,
                        SourceDocumentId = r.Id,
                        SourceDocumentNumber = r.ReturnNumber,
                        PostingRole = JournalPostingRole.Primary,
                        Status = JournalEntryStatus.Draft,
                        OperationId = Guid.NewGuid()
                    };

                    primaryJournal.Lines.Add(new JournalEntryLine
                    {
                        AccountId = apAcc.Id,
                        DebitAmount = r.TotalAmount,
                        CreditAmount = 0m,
                        SupplierId = r.SupplierId,
                        Narration = $"Purchase Return: {r.ReturnNumber}"
                    });

                    primaryJournal.Lines.Add(new JournalEntryLine
                    {
                        AccountId = invAcc.Id,
                        DebitAmount = 0m,
                        CreditAmount = r.TotalAmount,
                        Narration = $"Purchase Return: {r.ReturnNumber}"
                    });

                    await AccountingTransactionWriter.PostTwoPhaseJournalAsync(context, primaryJournal, cancellationToken);
                    await docTx.CommitAsync(cancellationToken);
                    createdCount++;
                }
                catch (Exception ex)
                {
                    await docTx.RollbackAsync(cancellationToken);
                    result.ErrorMessages.Add($"Purchase Return {r.ReturnNumber} failed: {ex.Message}");
                    return result with { TotalJournalsCreated = createdCount, TotalJournalsSkipped = skippedCount };
                }
            }
            else
            {
                skippedCount++;
                primaryJournal = await context.JournalEntries
                    .Include(j => j.Lines)
                    .FirstAsync(j => j.SourceDocumentType == JournalSourceDocumentType.PurchaseReturn &&
                                     j.SourceDocumentId == r.Id &&
                                     j.PostingRole == JournalPostingRole.Primary, cancellationToken);
            }

            if (r.Status == PurchaseReturnStatus.Cancelled)
            {
                bool revExists = await context.JournalEntries.AnyAsync(j =>
                    j.SourceDocumentType == JournalSourceDocumentType.PurchaseReturn &&
                    j.SourceDocumentId == r.Id &&
                    j.PostingRole == JournalPostingRole.Reversal, cancellationToken);

                if (!revExists && primaryJournal != null)
                {
                    await using var revTx = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                    try
                    {
                        var revUtc = r.CancelledAtUtc ?? DateTime.UtcNow;
                        var revDate = await _businessClock.ToBusinessDateAsync(revUtc, cancellationToken);
                        string revNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, revDate.Year, cancellationToken);

                        var revJ = AccountingTransactionWriter.CreateReversalJournal(
                            primaryJournal, revNum, revDate, revUtc, r.Reason ?? "Historical Cancellation");

                        await AccountingTransactionWriter.PostTwoPhaseJournalAsync(context, revJ, cancellationToken);
                        await revTx.CommitAsync(cancellationToken);
                        createdCount++;
                    }
                    catch (Exception ex)
                    {
                        await revTx.RollbackAsync(cancellationToken);
                        result.ErrorMessages.Add($"Purchase Return cancellation for {r.ReturnNumber} failed: {ex.Message}");
                        return result with { TotalJournalsCreated = createdCount, TotalJournalsSkipped = skippedCount };
                    }
                }
                else
                {
                    skippedCount++;
                }
            }
        }
        result = result with { PurchaseReturnsProcessed = purchaseReturns.Count };

        // 4. SALE INVOICES
        var sales = await context.SaleInvoices
            .Include(s => s.Items)
                .ThenInclude(i => i.BatchAllocations)
            .OrderBy(s => s.BusinessDate)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        foreach (var s in sales)
        {
            if (s.TaxAmount != 0m)
            {
                result.ErrorMessages.Add($"Historical sale {s.InvoiceNumber} contains non-zero TaxAmount ({s.TaxAmount}). Tax accounting is not supported.");
                return result with { TotalJournalsCreated = createdCount, TotalJournalsSkipped = skippedCount };
            }

            bool primaryExists = await context.JournalEntries.AnyAsync(j =>
                j.SourceDocumentType == JournalSourceDocumentType.SaleInvoice &&
                j.SourceDocumentId == s.Id &&
                j.PostingRole == JournalPostingRole.Primary, cancellationToken);

            JournalEntry? primaryJournal = null;

            if (!primaryExists)
            {
                await using var docTx = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                try
                {
                    decimal totalCogs = s.Items.SelectMany(i => i.BatchAllocations).Sum(a => a.InventoryValueConsumed);
                    decimal totalDiscount = s.LineDiscountTotal + s.InvoiceDiscountAmount;

                    string jNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, s.BusinessDate.Year, cancellationToken);

                    primaryJournal = new JournalEntry
                    {
                        EntryNumber = jNum,
                        BusinessDate = s.BusinessDate,
                        PostedAtUtc = s.PostedAtUtc ?? s.CreatedAtUtc,
                        Narration = $"Sale Invoice: {s.InvoiceNumber}",
                        SourceDocumentType = JournalSourceDocumentType.SaleInvoice,
                        SourceDocumentId = s.Id,
                        SourceDocumentNumber = s.InvoiceNumber,
                        PostingRole = JournalPostingRole.Primary,
                        Status = JournalEntryStatus.Draft,
                        OperationId = Guid.NewGuid()
                    };

                    if (s.SaleType == SaleType.Cash)
                    {
                        primaryJournal.Lines.Add(new JournalEntryLine
                        {
                            AccountId = cashAcc.Id,
                            DebitAmount = s.NetTotal,
                            CreditAmount = 0m,
                            Narration = $"Cash Sale: {s.InvoiceNumber}"
                        });
                    }
                    else
                    {
                        primaryJournal.Lines.Add(new JournalEntryLine
                        {
                            AccountId = arAcc.Id,
                            DebitAmount = s.NetTotal,
                            CreditAmount = 0m,
                            CustomerId = s.CustomerId,
                            Narration = $"Credit Sale: {s.InvoiceNumber}"
                        });
                    }

                    if (totalDiscount > 0)
                    {
                        primaryJournal.Lines.Add(new JournalEntryLine
                        {
                            AccountId = sDiscAcc.Id,
                            DebitAmount = totalDiscount,
                            CreditAmount = 0m,
                            Narration = $"Sales Discount: {s.InvoiceNumber}"
                        });
                    }

                    primaryJournal.Lines.Add(new JournalEntryLine
                    {
                        AccountId = revAcc.Id,
                        DebitAmount = 0m,
                        CreditAmount = s.GrossTotal,
                        Narration = $"Sales Revenue: {s.InvoiceNumber}"
                    });

                    primaryJournal.Lines.Add(new JournalEntryLine
                    {
                        AccountId = cogsAcc.Id,
                        DebitAmount = totalCogs,
                        CreditAmount = 0m,
                        Narration = $"COGS: {s.InvoiceNumber}"
                    });

                    primaryJournal.Lines.Add(new JournalEntryLine
                    {
                        AccountId = invAcc.Id,
                        DebitAmount = 0m,
                        CreditAmount = totalCogs,
                        Narration = $"Inventory Outflow: {s.InvoiceNumber}"
                    });

                    await AccountingTransactionWriter.PostTwoPhaseJournalAsync(context, primaryJournal, cancellationToken);
                    await docTx.CommitAsync(cancellationToken);
                    createdCount++;
                }
                catch (Exception ex)
                {
                    await docTx.RollbackAsync(cancellationToken);
                    result.ErrorMessages.Add($"Sale {s.InvoiceNumber} failed: {ex.Message}");
                    return result with { TotalJournalsCreated = createdCount, TotalJournalsSkipped = skippedCount };
                }
            }
            else
            {
                skippedCount++;
                primaryJournal = await context.JournalEntries
                    .Include(j => j.Lines)
                    .FirstAsync(j => j.SourceDocumentType == JournalSourceDocumentType.SaleInvoice &&
                                     j.SourceDocumentId == s.Id &&
                                     j.PostingRole == JournalPostingRole.Primary, cancellationToken);
            }

            if (s.Status == SaleInvoiceStatus.Cancelled)
            {
                bool revExists = await context.JournalEntries.AnyAsync(j =>
                    j.SourceDocumentType == JournalSourceDocumentType.SaleInvoice &&
                    j.SourceDocumentId == s.Id &&
                    j.PostingRole == JournalPostingRole.Reversal, cancellationToken);

                if (!revExists && primaryJournal != null)
                {
                    await using var revTx = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                    try
                    {
                        var revUtc = s.CancelledAtUtc ?? DateTime.UtcNow;
                        var revDate = await _businessClock.ToBusinessDateAsync(revUtc, cancellationToken);
                        string revNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, revDate.Year, cancellationToken);

                        var revJ = AccountingTransactionWriter.CreateReversalJournal(
                            primaryJournal, revNum, revDate, revUtc, s.CancellationReason ?? "Historical Cancellation");

                        await AccountingTransactionWriter.PostTwoPhaseJournalAsync(context, revJ, cancellationToken);
                        await revTx.CommitAsync(cancellationToken);
                        createdCount++;
                    }
                    catch (Exception ex)
                    {
                        await revTx.RollbackAsync(cancellationToken);
                        result.ErrorMessages.Add($"Sale cancellation for {s.InvoiceNumber} failed: {ex.Message}");
                        return result with { TotalJournalsCreated = createdCount, TotalJournalsSkipped = skippedCount };
                    }
                }
                else
                {
                    skippedCount++;
                }
            }
        }
        result = result with { SalesProcessed = sales.Count };

        // 5. SALES RETURNS
        var saleReturns = await context.SaleReturns
            .Include(r => r.OriginalSaleInvoice)
            .Include(r => r.Items)
                .ThenInclude(i => i.BatchAllocations)
            .Include(r => r.Items)
                .ThenInclude(i => i.OriginalSaleInvoiceItem)
            .OrderBy(r => r.ReturnDate)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        foreach (var r in saleReturns)
        {
            bool primaryExists = await context.JournalEntries.AnyAsync(j =>
                j.SourceDocumentType == JournalSourceDocumentType.SaleReturn &&
                j.SourceDocumentId == r.Id &&
                j.PostingRole == JournalPostingRole.Primary, cancellationToken);

            JournalEntry? primaryJournal = null;

            if (!primaryExists)
            {
                await using var docTx = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                try
                {
                    decimal restoredInv = r.Items.SelectMany(i => i.BatchAllocations).Sum(a => a.RestoredInventoryValue);
                    decimal grossReturned = r.Items.Sum(i => i.Quantity * (i.OriginalSaleInvoiceItem?.UnitSalePrice ?? 0m));
                    if (grossReturned == 0m) grossReturned = r.TotalAmount;

                    decimal netRefund = r.TotalAmount;
                    decimal discountReversed = grossReturned - netRefund;
                    if (discountReversed < 0) discountReversed = 0m;

                    string jNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, r.ReturnDate.Year, cancellationToken);

                    primaryJournal = new JournalEntry
                    {
                        EntryNumber = jNum,
                        BusinessDate = r.ReturnDate,
                        PostedAtUtc = r.PostedAtUtc ?? r.CreatedAtUtc,
                        Narration = $"Sale Return: {r.ReturnNumber}",
                        SourceDocumentType = JournalSourceDocumentType.SaleReturn,
                        SourceDocumentId = r.Id,
                        SourceDocumentNumber = r.ReturnNumber,
                        PostingRole = JournalPostingRole.Primary,
                        Status = JournalEntryStatus.Draft,
                        OperationId = Guid.NewGuid()
                    };

                    primaryJournal.Lines.Add(new JournalEntryLine
                    {
                        AccountId = sRetAcc.Id,
                        DebitAmount = grossReturned,
                        CreditAmount = 0m,
                        Narration = $"Sale Return: {r.ReturnNumber}"
                    });

                    if (discountReversed > 0)
                    {
                        primaryJournal.Lines.Add(new JournalEntryLine
                        {
                            AccountId = sDiscAcc.Id,
                            DebitAmount = 0m,
                            CreditAmount = discountReversed,
                            Narration = $"Discount Reversed: {r.ReturnNumber}"
                        });
                    }

                    if (r.OriginalSaleInvoice.SaleType == SaleType.Cash)
                    {
                        primaryJournal.Lines.Add(new JournalEntryLine
                        {
                            AccountId = cashAcc.Id,
                            DebitAmount = 0m,
                            CreditAmount = netRefund,
                            Narration = $"Cash Refund: {r.ReturnNumber}"
                        });
                    }
                    else
                    {
                        primaryJournal.Lines.Add(new JournalEntryLine
                        {
                            AccountId = arAcc.Id,
                            DebitAmount = 0m,
                            CreditAmount = netRefund,
                            CustomerId = r.CustomerId,
                            Narration = $"AR Credit: {r.ReturnNumber}"
                        });
                    }

                    primaryJournal.Lines.Add(new JournalEntryLine
                    {
                        AccountId = invAcc.Id,
                        DebitAmount = restoredInv,
                        CreditAmount = 0m,
                        Narration = $"Inventory Restored: {r.ReturnNumber}"
                    });

                    primaryJournal.Lines.Add(new JournalEntryLine
                    {
                        AccountId = cogsAcc.Id,
                        DebitAmount = 0m,
                        CreditAmount = restoredInv,
                        Narration = $"COGS Restored: {r.ReturnNumber}"
                    });

                    await AccountingTransactionWriter.PostTwoPhaseJournalAsync(context, primaryJournal, cancellationToken);
                    await docTx.CommitAsync(cancellationToken);
                    createdCount++;
                }
                catch (Exception ex)
                {
                    await docTx.RollbackAsync(cancellationToken);
                    result.ErrorMessages.Add($"Sale return {r.ReturnNumber} failed: {ex.Message}");
                    return result with { TotalJournalsCreated = createdCount, TotalJournalsSkipped = skippedCount };
                }
            }
            else
            {
                skippedCount++;
                primaryJournal = await context.JournalEntries
                    .Include(j => j.Lines)
                    .FirstAsync(j => j.SourceDocumentType == JournalSourceDocumentType.SaleReturn &&
                                     j.SourceDocumentId == r.Id &&
                                     j.PostingRole == JournalPostingRole.Primary, cancellationToken);
            }

            if (r.Status == SaleReturnStatus.Cancelled)
            {
                bool revExists = await context.JournalEntries.AnyAsync(j =>
                    j.SourceDocumentType == JournalSourceDocumentType.SaleReturn &&
                    j.SourceDocumentId == r.Id &&
                    j.PostingRole == JournalPostingRole.Reversal, cancellationToken);

                if (!revExists && primaryJournal != null)
                {
                    await using var revTx = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                    try
                    {
                        var revUtc = r.CancelledAtUtc ?? DateTime.UtcNow;
                        var revDate = await _businessClock.ToBusinessDateAsync(revUtc, cancellationToken);
                        string revNum = await _documentNumberGenerator.NextDocumentNumberAsync(DocumentType.JournalEntry, revDate.Year, cancellationToken);

                        var revJ = AccountingTransactionWriter.CreateReversalJournal(
                            primaryJournal, revNum, revDate, revUtc, r.CancellationReason ?? "Historical Cancellation");

                        await AccountingTransactionWriter.PostTwoPhaseJournalAsync(context, revJ, cancellationToken);
                        await revTx.CommitAsync(cancellationToken);
                        createdCount++;
                    }
                    catch (Exception ex)
                    {
                        await revTx.RollbackAsync(cancellationToken);
                        result.ErrorMessages.Add($"Sale return cancellation for {r.ReturnNumber} failed: {ex.Message}");
                        return result with { TotalJournalsCreated = createdCount, TotalJournalsSkipped = skippedCount };
                    }
                }
                else
                {
                    skippedCount++;
                }
            }
        }
        result = result with { SalesReturnsProcessed = saleReturns.Count };

        return result with { TotalJournalsCreated = createdCount, TotalJournalsSkipped = skippedCount };
    }
}

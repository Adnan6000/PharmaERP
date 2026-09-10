using Microsoft.EntityFrameworkCore;
using PharmaERP.Application.Common.Exceptions;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Application.DTOs;
using PharmaERP.Application.Interfaces;
using PharmaERP.Domain.Entities;
using PharmaERP.Domain.Enums;
using PharmaERP.Infrastructure.Persistence;
using PharmaERP.Infrastructure.Persistence.Services;

namespace PharmaERP.Infrastructure.Tests;

[Collection("SqlServerDatabaseCollection")]
public class AccountingIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerTestFixture _fixture;

    public AccountingIntegrationTests(SqlServerTestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private (
        IAccountService accountService,
        IAccountingConfigService configService,
        IVoucherService voucherService,
        IJournalService journalService,
        IPartyLedgerService partyLedgerService,
        IAccountingReconciliationService reconService,
        IAccountingSetupService setupService) GetServices()
    {
        return (
            _fixture.CreateAccountService(),
            _fixture.CreateAccountingConfigService(),
            _fixture.CreateVoucherService(),
            _fixture.CreateJournalService(),
            _fixture.CreatePartyLedgerService(),
            _fixture.CreateReconciliationService(),
            _fixture.CreateAccountingSetupService()
        );
    }

    private IPaymentSettlementService GetSettlementService() => _fixture.CreateSettlementService();

    // 1. CHART OF ACCOUNTS CREATION & HIERARCHY
    [Fact]
    public async Task InitializeChartOfAccounts_CreatesHierarchyAndSystemMappings()
    {
        var (accountService, _, _, _, _, _, setupService) = GetServices();

        await setupService.InitializeDefaultChartOfAccountsAsync();

        var accounts = await accountService.GetAllAccountsAsync();
        Assert.True(accounts.Count >= 15);

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        Assert.NotNull(cashAcc);
        Assert.Equal("1010", cashAcc.AccountCode);
        Assert.True(cashAcc.AllowPosting);

        var arAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl);
        Assert.NotNull(arAcc);
        Assert.Equal("1030", arAcc.AccountCode);
        Assert.True(arAcc.IsControlAccount);

        var apAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsPayableControl);
        Assert.NotNull(apAcc);
        Assert.Equal("2010", apAcc.AccountCode);
        Assert.True(apAcc.IsControlAccount);

        var invAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.Inventory);
        Assert.NotNull(invAcc);
        Assert.Equal("1040", invAcc.AccountCode);

        var cogsAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CostOfGoodsSold);
        Assert.NotNull(cogsAcc);
        Assert.Equal("5010", cogsAcc.AccountCode);

        var salesAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.SalesRevenue);
        Assert.NotNull(salesAcc);
        Assert.Equal("4010", salesAcc.AccountCode);
    }

    // 2. DOUBLE-ENTRY TRIGGER ENFORCEMENT
    [Fact]
    public async Task Trigger_RejectsImbalancedJournalPost()
    {
        var (accountService, _, _, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var salesAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.SalesRevenue);

        await using var db = await _fixture.ContextFactory.CreateDbContextAsync();

        var journal = new JournalEntry
        {
            EntryNumber = "JE-2026-999999",
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            PostedAtUtc = DateTime.UtcNow,
            SourceDocumentType = JournalSourceDocumentType.Manual,
            PostingRole = JournalPostingRole.Primary,
            OperationId = Guid.NewGuid(),
            TotalDebit = 1000m,
            TotalCredit = 500m, // Imbalanced!
            Status = JournalEntryStatus.Posted
        };

        journal.Lines.Add(new JournalEntryLine
        {
            AccountId = cashAcc!.Id,
            DebitAmount = 1000m,
            CreditAmount = 0m
        });
        journal.Lines.Add(new JournalEntryLine
        {
            AccountId = salesAcc!.Id,
            DebitAmount = 0m,
            CreditAmount = 500m
        });

        await db.JournalEntries.AddAsync(journal);

        // SQL Server trigger TR_JournalEntries_EnforceBalanceOnPost will rollback
        await Assert.ThrowsAnyAsync<Exception>(async () => await db.SaveChangesAsync());
    }

    // 3. IMMUTABILITY TRIGGER ENFORCEMENT
    [Fact]
    public async Task Trigger_PreventsModifyingPostedJournalHeaderAndLines()
    {
        var (accountService, _, _, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var equityAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.OpeningBalanceEquity);

        await using var db = await _fixture.ContextFactory.CreateDbContextAsync();

        // 1. Post a valid balanced journal using two-phase post
        var journal = new JournalEntry
        {
            EntryNumber = "JE-2026-000111",
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            PostedAtUtc = DateTime.UtcNow,
            SourceDocumentType = JournalSourceDocumentType.Manual,
            PostingRole = JournalPostingRole.Primary,
            OperationId = Guid.NewGuid()
        };
        journal.Lines.Add(new JournalEntryLine { AccountId = cashAcc!.Id, DebitAmount = 500m, CreditAmount = 0m });
        journal.Lines.Add(new JournalEntryLine { AccountId = equityAcc!.Id, DebitAmount = 0m, CreditAmount = 500m });

        await AccountingTransactionWriter.PostTwoPhaseJournalAsync(db, journal, default);

        var journalId = journal.Id;
        var lineId = journal.Lines.First().Id;

        // 2. Try to update header of posted journal -> Trigger should reject
        await using var updateDb = await _fixture.ContextFactory.CreateDbContextAsync();
        var postedEntry = await updateDb.JournalEntries.FirstAsync(j => j.Id == journalId);
        postedEntry.Narration = "Tampered narration";

        await Assert.ThrowsAnyAsync<Exception>(async () => await updateDb.SaveChangesAsync());

        // 3. Try to update lines of posted journal -> Trigger should reject
        await using var lineUpdateDb = await _fixture.ContextFactory.CreateDbContextAsync();
        var postedLine = await lineUpdateDb.JournalEntryLines.FirstAsync(l => l.Id == lineId);
        postedLine.DebitAmount = 999m;

        await Assert.ThrowsAnyAsync<Exception>(async () => await lineUpdateDb.SaveChangesAsync());

        // 4. Try to insert line into posted journal -> Trigger should reject
        await using var lineInsertDb = await _fixture.ContextFactory.CreateDbContextAsync();
        lineInsertDb.JournalEntryLines.Add(new JournalEntryLine
        {
            JournalEntryId = journalId,
            AccountId = cashAcc.Id,
            DebitAmount = 100m,
            CreditAmount = 0m
        });

        await Assert.ThrowsAnyAsync<Exception>(async () => await lineInsertDb.SaveChangesAsync());

        // 5. Try to delete header -> Trigger should reject
        await using var deleteDb = await _fixture.ContextFactory.CreateDbContextAsync();
        var deleteEntry = await deleteDb.JournalEntries.FirstAsync(j => j.Id == journalId);
        deleteDb.JournalEntries.Remove(deleteEntry);

        await Assert.ThrowsAnyAsync<Exception>(async () => await deleteDb.SaveChangesAsync());

        // 6. Try to delete line from posted journal -> Trigger should reject
        await using var lineDeleteDb = await _fixture.ContextFactory.CreateDbContextAsync();
        var lineToDelete = await lineDeleteDb.JournalEntryLines.FirstAsync(l => l.Id == lineId);
        lineDeleteDb.JournalEntryLines.Remove(lineToDelete);

        await Assert.ThrowsAnyAsync<Exception>(async () => await lineDeleteDb.SaveChangesAsync());
    }

    // 4. VOUCHER WORKFLOW: RECEIPT VOUCHER (RV) CREATE & CANCEL
    [Fact]
    public async Task ReceiptVoucher_CreateAndCancel_PostsCompensatingReversal()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var arAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl);

        // Create Customer
        int customerId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var cust = new Customer { Name = "Ali Medical Store", CustomerCode = "C-001", IsActive = true };
            db.Customers.Add(cust);
            await db.SaveChangesAsync();
            customerId = cust.Id;
        }

        // 1. Create RV
        var rvDto = new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = arAcc!.Id,
            CustomerId = customerId,
            Amount = 2500m,
            Reference = "CHQ-12345",
            Narration = "Received on account"
        };

        var rv = await voucherService.CreateReceiptVoucherAsync(rvDto);
        Assert.NotNull(rv);
        Assert.StartsWith("RV-", rv.VoucherNumber);
        Assert.Equal(VoucherStatus.Posted, rv.Status);

        // Verify Journal
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var je = await db.JournalEntries.Include(j => j.Lines).FirstAsync(j => j.SourceDocumentType == JournalSourceDocumentType.ReceiptVoucher && j.SourceDocumentId == rv.Id);
            Assert.Equal(JournalPostingRole.Primary, je.PostingRole);
            Assert.Equal(2500m, je.TotalDebit);
            Assert.Equal(2500m, je.TotalCredit);

            var drLine = je.Lines.First(l => l.DebitAmount > 0);
            Assert.Equal(cashAcc.Id, drLine.AccountId);

            var crLine = je.Lines.First(l => l.CreditAmount > 0);
            Assert.Equal(arAcc.Id, crLine.AccountId);
            Assert.Equal(customerId, crLine.CustomerId);
        }

        // 2. Cancel RV
        var cancelledRv = await voucherService.CancelReceiptVoucherAsync(rv.Id, "Bounced cheque");
        Assert.Equal(VoucherStatus.Cancelled, cancelledRv.Status);

        // Verify Reversal Journal created
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var reversal = await db.JournalEntries.Include(j => j.Lines)
                .FirstAsync(j => j.SourceDocumentType == JournalSourceDocumentType.ReceiptVoucher && j.SourceDocumentId == rv.Id && j.PostingRole == JournalPostingRole.Reversal);

            Assert.Equal(2500m, reversal.TotalDebit);
            Assert.Equal(2500m, reversal.TotalCredit);

            // In reversal: Dr AR / Cr Cash
            var revDr = reversal.Lines.First(l => l.DebitAmount > 0);
            Assert.Equal(arAcc.Id, revDr.AccountId);
            Assert.Equal(customerId, revDr.CustomerId);

            var revCr = reversal.Lines.First(l => l.CreditAmount > 0);
            Assert.Equal(cashAcc.Id, revCr.AccountId);
        }
    }

    // 5. VOUCHER WORKFLOW: PAYMENT VOUCHER (PV) CREATE & CANCEL
    [Fact]
    public async Task PaymentVoucher_CreateAndCancel_PostsCompensatingReversal()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var apAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsPayableControl);

        // Create Supplier
        int supplierId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var sup = new Supplier { Name = "GlaxoSmithKline", SupplierCode = "S-001", IsActive = true };
            db.Suppliers.Add(sup);
            await db.SaveChangesAsync();
            supplierId = sup.Id;
        }

        // 1. Create PV
        var pvDto = new PaymentVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = apAcc!.Id,
            SupplierId = supplierId,
            Amount = 4500m,
            Reference = "ONLINE-REF-99",
            Narration = "Vendor payment"
        };

        var pv = await voucherService.CreatePaymentVoucherAsync(pvDto);
        Assert.NotNull(pv);
        Assert.StartsWith("PV-", pv.VoucherNumber);
        Assert.Equal(VoucherStatus.Posted, pv.Status);

        // 2. Cancel PV
        var cancelledPv = await voucherService.CancelPaymentVoucherAsync(pv.Id, "Wrong vendor selected");
        Assert.Equal(VoucherStatus.Cancelled, cancelledPv.Status);

        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var reversal = await db.JournalEntries.Include(j => j.Lines)
                .FirstAsync(j => j.SourceDocumentType == JournalSourceDocumentType.PaymentVoucher && j.SourceDocumentId == pv.Id && j.PostingRole == JournalPostingRole.Reversal);

            Assert.Equal(4500m, reversal.TotalDebit);
            Assert.Equal(4500m, reversal.TotalCredit);

            // In PV Reversal: Dr Cash / Cr AP
            var revDr = reversal.Lines.First(l => l.DebitAmount > 0);
            Assert.Equal(cashAcc.Id, revDr.AccountId);

            var revCr = reversal.Lines.First(l => l.CreditAmount > 0);
            Assert.Equal(apAcc.Id, revCr.AccountId);
            Assert.Equal(supplierId, revCr.SupplierId);
        }
    }

    // 6. OPERATIONAL INTEGRATION: SALE CASH AND COGS
    [Fact]
    public async Task OperationalSale_Cash_PostsAtomicJournals()
    {
        var (_, _, _, _, _, _, setupService) = GetServices();
        await setupService.RunHistoricalInitializationAsync();

        // Seed Product and Batch
        int productId, batchId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Panadol 500mg", ProductCode = "PAN-500", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;

            var batch = new ProductBatch
            {
                ProductId = productId,
                BatchNumber = "BATCH-01",
                NormalizedBatchNumber = "BATCH-01",
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                QuantityOnHand = 100m,
                AveragePurchaseCost = 10m,
                LastPurchaseCost = 10m,
                InventoryValue = 1000m,
                SuggestedSalePrice = 15m,
                IsActive = true
            };
            db.ProductBatches.Add(batch);
            await db.SaveChangesAsync();
            batchId = batch.Id;
        }

        var saleWriter = _fixture.CreateSaleWriter();
        var cashSaleDto = new SaleInvoiceCreateDto
        {
            CustomerId = null,
            SaleType = SaleType.Cash,
            TenderedAmount = 150m,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new()
                {
                    ProductId = productId,
                    Quantity = 10m,
                    UnitSalePrice = 15m
                }
            }
        };

        var sale = await saleWriter.CreateAndPostSaleInvoiceAsync("SINV-2026-000001", cashSaleDto);
        Assert.NotNull(sale);

        // Verify that in the EXACT same database state, JournalEntries exist for this sale
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var journals = await db.JournalEntries.Include(j => j.Lines).ThenInclude(l => l.Account)
                .Where(j => j.SourceDocumentType == JournalSourceDocumentType.SaleInvoice && j.SourceDocumentId == sale.Id)
                .ToListAsync();

            Assert.Single(journals);
            var journal = journals.First();

            Assert.Equal(250m, journal.TotalDebit);
            Assert.Equal(250m, journal.TotalCredit);
            Assert.Equal(JournalEntryStatus.Posted, journal.Status);

            // Revenue & Cash lines: Dr CashOnHand (150) / Cr SalesRevenue (150)
            var cashLine = journal.Lines.First(l => l.Account.SystemAccountType == SystemAccountType.CashOnHand);
            var revLine = journal.Lines.First(l => l.Account.SystemAccountType == SystemAccountType.SalesRevenue);
            Assert.Equal(150m, cashLine.DebitAmount);
            Assert.Equal(150m, revLine.CreditAmount);

            // COGS & Inventory lines: Dr CostOfGoodsSold (100) / Cr Inventory (100)
            var cogsLine = journal.Lines.First(l => l.Account.SystemAccountType == SystemAccountType.CostOfGoodsSold);
            var invLine = journal.Lines.First(l => l.Account.SystemAccountType == SystemAccountType.Inventory);
            Assert.Equal(100m, cogsLine.DebitAmount);
            Assert.Equal(100m, invLine.CreditAmount);
        }
    }

    // 7. OPERATIONAL SALE CANCELLATION: REVERSES JOURNALS
    [Fact]
    public async Task OperationalSale_Cancellation_PostsAtomicReversalJournals()
    {
        var (_, _, _, _, _, _, setupService) = GetServices();
        await setupService.RunHistoricalInitializationAsync();

        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Augmentin 625mg", ProductCode = "AUG-625", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;

            var batch = new ProductBatch
            {
                ProductId = productId,
                BatchNumber = "AUG-B1",
                NormalizedBatchNumber = "AUG-B1",
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                QuantityOnHand = 50m,
                AveragePurchaseCost = 50m,
                LastPurchaseCost = 50m,
                InventoryValue = 2500m,
                SuggestedSalePrice = 80m,
                IsActive = true
            };
            db.ProductBatches.Add(batch);
            await db.SaveChangesAsync();
        }

        var saleWriter = _fixture.CreateSaleWriter();
        var saleDto = new SaleInvoiceCreateDto
        {
            CustomerId = null,
            SaleType = SaleType.Cash,
            TenderedAmount = 400m,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new()
                {
                    ProductId = productId,
                    Quantity = 5m,
                    UnitSalePrice = 80m
                }
            }
        };

        var sale = await saleWriter.CreateAndPostSaleInvoiceAsync("SINV-2026-000002", saleDto);
        Assert.NotNull(sale);

        // Cancel the sale invoice
        await saleWriter.CancelSaleInvoiceAsync(sale.Id, "Customer changed mind at counter");

        // Verify reversal journals exist
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var reversalJournals = await db.JournalEntries.Include(j => j.Lines)
                .Where(j => j.SourceDocumentType == JournalSourceDocumentType.SaleInvoice && j.SourceDocumentId == sale.Id && j.PostingRole == JournalPostingRole.Reversal)
                .ToListAsync();

            Assert.Single(reversalJournals);
            var rev = reversalJournals.First();
            Assert.Equal(JournalPostingRole.Reversal, rev.PostingRole);
            Assert.Equal(rev.TotalDebit, rev.TotalCredit);
            Assert.Equal(650m, rev.TotalDebit); // 400 gross rev + 250 cogs

            var allSaleJournals = await db.JournalEntries
                .Where(j => j.SourceDocumentType == JournalSourceDocumentType.SaleInvoice && j.SourceDocumentId == sale.Id)
                .ToListAsync();
            Assert.Equal(2, allSaleJournals.Count);
            Assert.All(allSaleJournals, j => Assert.Equal(JournalEntryStatus.Posted, j.Status));
        }
    }

    // 8. ACCOUNTING LOCK DATE ENFORCEMENT
    [Fact]
    public async Task AccountingLockDate_RejectsOperationalTransactionsOnOrBeforeLockDate()
    {
        var (_, configService, _, _, _, _, setupService) = GetServices();
        await setupService.RunHistoricalInitializationAsync();

        var lockDate = DateOnly.FromDateTime(DateTime.Today);
        await configService.SetAccountingLockDateAsync(lockDate);

        // Seed product & batch
        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Brufen 400mg", ProductCode = "BRU-400", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;

            var batch = new ProductBatch
            {
                ProductId = productId,
                BatchNumber = "B-LOCK",
                NormalizedBatchNumber = "B-LOCK",
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                QuantityOnHand = 50m,
                AveragePurchaseCost = 20m,
                InventoryValue = 1000m,
                SuggestedSalePrice = 30m,
                IsActive = true
            };
            db.ProductBatches.Add(batch);
            await db.SaveChangesAsync();
        }

        var saleWriter = _fixture.CreateSaleWriter();
        var lockedSaleDto = new SaleInvoiceCreateDto
        {
            CustomerId = null,
            SaleType = SaleType.Cash,
            TenderedAmount = 30m,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new()
                {
                    ProductId = productId,
                    Quantity = 1m,
                    UnitSalePrice = 30m
                }
            }
        };

        // Must reject due to lock date
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await saleWriter.CreateAndPostSaleInvoiceAsync("SINV-2026-000003", lockedSaleDto));
    }

    // 9. HISTORICAL INITIALIZATION & 4-WAY AUTOMATED RECONCILIATION
    [Fact]
    public async Task HistoricalInitialization_BackfillsAndPasses4WayReconciliation()
    {
        var (_, _, _, _, _, _, setupService) = GetServices();

        // 1. Seed operational data BEFORE accounting setup (simulating legacy data)
        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Disprin 300mg", ProductCode = "DIS-300", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;
        }

        // Record opening stock through InventoryWriter before accounting initialization
        var invWriter = _fixture.CreateInventoryWriter();
        var osDto = new OpeningStockCreateDto
        {
            ProductId = productId,
            BatchNumber = "DIS-B1",
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(2)),
            Quantity = 100m,
            UnitCost = 5m,
            SuggestedSalePrice = 8m
        };
        await invWriter.RecordOpeningStockAsync(osDto);

        // 2. Run Historical Initialization & Backfill
        var result = await setupService.RunHistoricalInitializationAsync();

        Assert.True(result.Success);
        Assert.Equal(AccountingSetupState.Active, result.FinalState);
        Assert.True(result.TotalJournalsCreated > 0);
        Assert.NotNull(result.ReconciliationReport);

        // 3. Verify 4-Way Reconciliation
        var recon = result.ReconciliationReport;
        Assert.True(recon.AllMatched, $"Reconciliation failed: InvDiff={recon.InventoryDifference}, ArDiff={recon.ArDifference}, ApDiff={recon.ApDifference}, TbDiff={recon.TrialBalanceDifference}");
        Assert.True(recon.InventoryMatched);
        Assert.True(recon.ArMatched);
        Assert.True(recon.ApMatched);
        Assert.True(recon.TrialBalanceMatched);
        Assert.Equal(500m, recon.InventoryGlBalance);
        Assert.Equal(500m, recon.PhysicalStockValuation);
    }

    // 10. POSTED JOURNAL NARRATION IMMUTABILITY (ALL TRANSITIONS)
    [Fact]
    public async Task Trigger_PreventsPostedJournalNarrationTransitions()
    {
        var (accountService, _, _, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var equityAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.OpeningBalanceEquity);

        await using var db = await _fixture.ContextFactory.CreateDbContextAsync();

        // 1. Post a journal with non-null Narration
        var j1 = new JournalEntry
        {
            EntryNumber = "JE-2026-000201",
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            PostedAtUtc = DateTime.UtcNow,
            SourceDocumentType = JournalSourceDocumentType.Manual,
            PostingRole = JournalPostingRole.Primary,
            OperationId = Guid.NewGuid(),
            Narration = "Initial narration"
        };
        j1.Lines.Add(new JournalEntryLine { AccountId = cashAcc!.Id, DebitAmount = 100m, CreditAmount = 0m });
        j1.Lines.Add(new JournalEntryLine { AccountId = equityAcc!.Id, DebitAmount = 0m, CreditAmount = 100m });
        await AccountingTransactionWriter.PostTwoPhaseJournalAsync(db, j1, default);

        // Case A: Value -> Value transition must be rejected by trigger
        await using (var updateDb1 = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var entry1 = await updateDb1.JournalEntries.FirstAsync(j => j.Id == j1.Id);
            entry1.Narration = "Modified narration";
            await Assert.ThrowsAnyAsync<Exception>(async () => await updateDb1.SaveChangesAsync());
        }

        // Case B: Value -> NULL transition must be rejected by trigger
        await using (var updateDb2 = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var entry2 = await updateDb2.JournalEntries.FirstAsync(j => j.Id == j1.Id);
            entry2.Narration = null;
            await Assert.ThrowsAnyAsync<Exception>(async () => await updateDb2.SaveChangesAsync());
        }

        // 2. Post a journal with NULL Narration
        var j2 = new JournalEntry
        {
            EntryNumber = "JE-2026-000202",
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            PostedAtUtc = DateTime.UtcNow,
            SourceDocumentType = JournalSourceDocumentType.Manual,
            PostingRole = JournalPostingRole.Primary,
            OperationId = Guid.NewGuid(),
            Narration = null
        };
        j2.Lines.Add(new JournalEntryLine { AccountId = cashAcc!.Id, DebitAmount = 200m, CreditAmount = 0m });
        j2.Lines.Add(new JournalEntryLine { AccountId = equityAcc!.Id, DebitAmount = 0m, CreditAmount = 200m });
        await AccountingTransactionWriter.PostTwoPhaseJournalAsync(db, j2, default);

        // Case C: NULL -> Value transition must be rejected by trigger
        await using (var updateDb3 = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var entry3 = await updateDb3.JournalEntries.FirstAsync(j => j.Id == j2.Id);
            entry3.Narration = "Tampered narration";
            await Assert.ThrowsAnyAsync<Exception>(async () => await updateDb3.SaveChangesAsync());
        }
    }

    // 11. CONTRA VOUCHER LIFECYCLE (CASH-BANK, BANK-CASH, BANK-BANK, GUARDS & CANCELLATION)
    [Fact]
    public async Task ContraVoucher_CashToBank_BankToCash_BankToBank_AndCancelLifecycle()
    {
        var (accountService, _, voucherService, journalService, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);

        // Retrieve Default Bank Account (1020) and create 2nd Bank Account (1025)
        var bank1 = await accountService.GetBySystemTypeAsync(SystemAccountType.DefaultBankAccount);
        Assert.NotNull(bank1);

        var bank2 = await accountService.CreateAccountAsync(new AccountCreateDto
        {
            AccountCode = "1025",
            Name = "Meezan Islamic",
            AccountType = AccountType.Asset,
            AllowPosting = true,
            IsBankAccount = true
        });

        // 1. Cash to Bank 1
        var cv1 = await voucherService.CreateContraVoucherAsync(new ContraVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            SourceAccountId = cashAcc!.Id,
            DestinationAccountId = bank1.Id,
            Amount = 5000m,
            Reference = "DEP-001",
            Narration = "Deposit excess cash into HBL"
        });

        Assert.NotNull(cv1);
        Assert.StartsWith("CV-", cv1.VoucherNumber);
        Assert.Equal(VoucherStatus.Posted, cv1.Status);
        Assert.Equal(5000m, cv1.Amount);

        // Verify Journal Entry for CV1 (Bank1 Dr 5000, Cash Cr 5000)
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var je = await db.JournalEntries.Include(j => j.Lines).FirstOrDefaultAsync(j => j.SourceDocumentNumber == cv1.VoucherNumber);
            Assert.NotNull(je);
            Assert.Equal(JournalSourceDocumentType.ContraVoucher, je.SourceDocumentType);
            var bankLine = je.Lines.FirstOrDefault(l => l.AccountId == bank1.Id);
            var cashLine = je.Lines.FirstOrDefault(l => l.AccountId == cashAcc.Id);
            Assert.NotNull(bankLine);
            Assert.NotNull(cashLine);
            Assert.Equal(5000m, bankLine.DebitAmount);
            Assert.Equal(5000m, cashLine.CreditAmount);
        }

        // 2. Bank 1 to Cash
        var cv2 = await voucherService.CreateContraVoucherAsync(new ContraVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            SourceAccountId = bank1.Id,
            DestinationAccountId = cashAcc.Id,
            Amount = 2000m,
            Reference = "ATM-001",
            Narration = "Cash withdrawal from HBL"
        });
        Assert.Equal(VoucherStatus.Posted, cv2.Status);

        // 3. Bank 1 to Bank 2
        var cv3 = await voucherService.CreateContraVoucherAsync(new ContraVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            SourceAccountId = bank1.Id,
            DestinationAccountId = bank2.Id,
            Amount = 1000m,
            Reference = "IBFT-001",
            Narration = "Inter-bank transfer HBL to Meezan"
        });
        Assert.Equal(VoucherStatus.Posted, cv3.Status);

        // 4. Validation Guards
        // Same account
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await voucherService.CreateContraVoucherAsync(new ContraVoucherCreateDto
            {
                SourceAccountId = bank1.Id,
                DestinationAccountId = bank1.Id,
                Amount = 500m
            });
        });

        // Zero / Negative amount
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await voucherService.CreateContraVoucherAsync(new ContraVoucherCreateDto
            {
                SourceAccountId = cashAcc.Id,
                DestinationAccountId = bank1.Id,
                Amount = 0m
            });
        });

        // Non cash/bank account
        var salesAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.SalesRevenue);
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await voucherService.CreateContraVoucherAsync(new ContraVoucherCreateDto
            {
                SourceAccountId = salesAcc!.Id,
                DestinationAccountId = bank1.Id,
                Amount = 500m
            });
        });

        // 5. Cancellation
        var cancelledCv1 = await voucherService.CancelContraVoucherAsync(cv1.Id, "Duplicate entry");
        Assert.Equal(VoucherStatus.Cancelled, cancelledCv1.Status);
        Assert.Equal("Duplicate entry", cancelledCv1.CancellationReason);

        // Verify Compensating Reversal Journal posted
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var revJe = await db.JournalEntries.Include(j => j.Lines).FirstOrDefaultAsync(j => j.SourceDocumentNumber == cv1.VoucherNumber && j.PostingRole == JournalPostingRole.Reversal);
            Assert.NotNull(revJe);
            Assert.Equal(JournalPostingRole.Reversal, revJe.PostingRole);
            var revBankLine = revJe.Lines.FirstOrDefault(l => l.AccountId == bank1.Id);
            var revCashLine = revJe.Lines.FirstOrDefault(l => l.AccountId == cashAcc.Id);
            Assert.NotNull(revBankLine);
            Assert.NotNull(revCashLine);
            Assert.Equal(5000m, revBankLine.CreditAmount);
            Assert.Equal(5000m, revCashLine.DebitAmount);
        }
    }

    // 12. PROFIT & LOSS STATEMENT GENERATION
    [Fact]
    public async Task ProfitAndLossStatement_CalculatesAccurateRevenuesExpensesAndNetMargin()
    {
        var (accountService, _, _, journalService, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var salesAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.SalesRevenue);
        var cogsAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CostOfGoodsSold);
        var invAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.Inventory);

        // Retrieve existing Rent Expense account (5030)
        var rentAcc = await accountService.GetByCodeAsync("5030");
        Assert.NotNull(rentAcc);

        var today = DateOnly.FromDateTime(DateTime.Today);

        // Post Revenue journal: Cash Dr 10,000 / Sales Revenue Cr 10,000
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var j1 = new JournalEntry
            {
                EntryNumber = "JE-2026-REV-01",
                BusinessDate = today,
                PostedAtUtc = DateTime.UtcNow,
                SourceDocumentType = JournalSourceDocumentType.SaleInvoice,
                PostingRole = JournalPostingRole.Primary,
                OperationId = Guid.NewGuid()
            };
            j1.Lines.Add(new JournalEntryLine { AccountId = cashAcc!.Id, DebitAmount = 10000m });
            j1.Lines.Add(new JournalEntryLine { AccountId = salesAcc!.Id, CreditAmount = 10000m });
            await AccountingTransactionWriter.PostTwoPhaseJournalAsync(db, j1, default);

            // Post COGS journal: COGS Dr 6,000 / Inventory Cr 6,000
            var j2 = new JournalEntry
            {
                EntryNumber = "JE-2026-COG-01",
                BusinessDate = today,
                PostedAtUtc = DateTime.UtcNow,
                SourceDocumentType = JournalSourceDocumentType.SaleInvoice,
                PostingRole = JournalPostingRole.Primary,
                OperationId = Guid.NewGuid()
            };
            j2.Lines.Add(new JournalEntryLine { AccountId = cogsAcc!.Id, DebitAmount = 6000m });
            j2.Lines.Add(new JournalEntryLine { AccountId = invAcc!.Id, CreditAmount = 6000m });
            await AccountingTransactionWriter.PostTwoPhaseJournalAsync(db, j2, default);

            // Post Rent expense: Rent Dr 1,500 / Cash Cr 1,500
            var j3 = new JournalEntry
            {
                EntryNumber = "JE-2026-EXP-01",
                BusinessDate = today,
                PostedAtUtc = DateTime.UtcNow,
                SourceDocumentType = JournalSourceDocumentType.Manual,
                PostingRole = JournalPostingRole.Primary,
                OperationId = Guid.NewGuid()
            };
            j3.Lines.Add(new JournalEntryLine { AccountId = rentAcc.Id, DebitAmount = 1500m });
            j3.Lines.Add(new JournalEntryLine { AccountId = cashAcc.Id, CreditAmount = 1500m });
            await AccountingTransactionWriter.PostTwoPhaseJournalAsync(db, j3, default);
        }

        // Query P&L
        var pnl = await journalService.GetProfitAndLossAsync(today.AddDays(-1), today.AddDays(1));

        Assert.NotNull(pnl);
        Assert.Equal(10000m, pnl.TotalRevenue);
        Assert.Equal(7500m, pnl.TotalExpense);
        Assert.Equal(2500m, pnl.NetProfitOrLoss);
        Assert.True(pnl.IsProfit);
        Assert.Contains(pnl.RevenueLines, r => r.AccountId == salesAcc.Id && r.Amount == 10000m);
        Assert.Contains(pnl.ExpenseLines, e => e.AccountId == cogsAcc.Id && e.Amount == 6000m);
        Assert.Contains(pnl.ExpenseLines, e => e.AccountId == rentAcc.Id && e.Amount == 1500m);
    }

    // 13. BALANCE SHEET STATEMENT GENERATION & VERIFICATION
    [Fact]
    public async Task BalanceSheetStatement_CalculatesAssetsLiabilitiesEquityAndBalances()
    {
        var (accountService, _, _, journalService, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var salesAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.SalesRevenue);
        var cogsAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CostOfGoodsSold);
        var invAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.Inventory);

        var today = DateOnly.FromDateTime(DateTime.Today);

        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            // Initial Capital / Equity injection: Cash Dr 20,000 / Opening Balance Equity Cr 20,000
            var equityAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.OpeningBalanceEquity);
            var j0 = new JournalEntry
            {
                EntryNumber = "JE-2026-EQ-01",
                BusinessDate = today,
                PostedAtUtc = DateTime.UtcNow,
                SourceDocumentType = JournalSourceDocumentType.Manual,
                PostingRole = JournalPostingRole.Primary,
                OperationId = Guid.NewGuid()
            };
            j0.Lines.Add(new JournalEntryLine { AccountId = cashAcc!.Id, DebitAmount = 20000m });
            j0.Lines.Add(new JournalEntryLine { AccountId = equityAcc!.Id, CreditAmount = 20000m });
            await AccountingTransactionWriter.PostTwoPhaseJournalAsync(db, j0, default);

            // Buy Inventory for cash: Inventory Dr 8,000 / Cash Cr 8,000
            var j1 = new JournalEntry
            {
                EntryNumber = "JE-2026-INV-01",
                BusinessDate = today,
                PostedAtUtc = DateTime.UtcNow,
                SourceDocumentType = JournalSourceDocumentType.PurchaseInvoice,
                PostingRole = JournalPostingRole.Primary,
                OperationId = Guid.NewGuid()
            };
            j1.Lines.Add(new JournalEntryLine { AccountId = invAcc!.Id, DebitAmount = 8000m });
            j1.Lines.Add(new JournalEntryLine { AccountId = cashAcc.Id, CreditAmount = 8000m });
            await AccountingTransactionWriter.PostTwoPhaseJournalAsync(db, j1, default);

            // Sell part of inventory: Cash Dr 10,000 / Sales Revenue Cr 10,000
            var j2 = new JournalEntry
            {
                EntryNumber = "JE-2026-SLS-01",
                BusinessDate = today,
                PostedAtUtc = DateTime.UtcNow,
                SourceDocumentType = JournalSourceDocumentType.SaleInvoice,
                PostingRole = JournalPostingRole.Primary,
                OperationId = Guid.NewGuid()
            };
            j2.Lines.Add(new JournalEntryLine { AccountId = cashAcc.Id, DebitAmount = 10000m });
            j2.Lines.Add(new JournalEntryLine { AccountId = salesAcc!.Id, CreditAmount = 10000m });
            await AccountingTransactionWriter.PostTwoPhaseJournalAsync(db, j2, default);

            // Cost of sale: COGS Dr 5,000 / Inventory Cr 5,000
            var j3 = new JournalEntry
            {
                EntryNumber = "JE-2026-COG-02",
                BusinessDate = today,
                PostedAtUtc = DateTime.UtcNow,
                SourceDocumentType = JournalSourceDocumentType.SaleInvoice,
                PostingRole = JournalPostingRole.Primary,
                OperationId = Guid.NewGuid()
            };
            j3.Lines.Add(new JournalEntryLine { AccountId = cogsAcc!.Id, DebitAmount = 5000m });
            j3.Lines.Add(new JournalEntryLine { AccountId = invAcc.Id, CreditAmount = 5000m });
            await AccountingTransactionWriter.PostTwoPhaseJournalAsync(db, j3, default);
        }

        // Net Earnings: Revenue (10,000) - Expense (5,000) = +5,000
        // Assets: Cash = 20,000 - 8,000 + 10,000 = 22,000. Inventory = 8,000 - 5,000 = 3,000. Total Assets = 25,000.
        // Liabilities: 0
        // Equity: Base Equity = 20,000. Current Period Earnings = 5,000. Total Liab + Eq = 25,000.

        var bs = await journalService.GetBalanceSheetAsync(today.AddDays(1));

        Assert.NotNull(bs);
        Assert.Equal(25000m, bs.TotalAssets);
        Assert.Equal(0m, bs.TotalLiabilities);
        Assert.Equal(20000m, bs.TotalEquity);
        Assert.Equal(5000m, bs.CurrentPeriodEarnings);
        Assert.Equal(25000m, bs.TotalLiabilitiesAndEquity);
        Assert.Equal(0m, bs.Difference);
        Assert.True(bs.IsBalanced);
    }

    // 14. INVOICE PAYMENT RECONCILIATION & SETTLEMENT ALLOCATION
    [Fact]
    public async Task PaymentSettlement_AllocateReceiptAndPaymentVouchers_FullPartialAndGuards()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        var settlementService = GetSettlementService();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var arAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl);
        var apAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsPayableControl);

        // 1. Seed Customer and Sale Invoice
        int customer1Id;
        int customer2Id;
        int saleInv1Id;
        int saleInv2Id;
        int supplierId;
        int purchaseInvId;

        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var cust1 = new Customer { CustomerCode = "CUST-0001", Name = "Ali Pharmacy", Phone = "03001112233", IsActive = true };
            var cust2 = new Customer { CustomerCode = "CUST-0002", Name = "Bilal Medicos", Phone = "03004445566", IsActive = true };
            var sup = new Supplier { SupplierCode = "SUPP-0001", Name = "Pfizer Distributor", Phone = "03009998877", IsActive = true };

            db.Customers.AddRange(cust1, cust2);
            db.Suppliers.Add(sup);
            await db.SaveChangesAsync();

            customer1Id = cust1.Id;
            customer2Id = cust2.Id;
            supplierId = sup.Id;

            // Seed SaleInvoice for Customer 1
            var sinv1 = new SaleInvoice
            {
                InvoiceNumber = "SINV-202609-000501",
                OperationId = Guid.NewGuid(),
                BusinessDate = DateOnly.FromDateTime(DateTime.Today),
                CustomerId = customer1Id,
                CustomerNameSnapshot = cust1.Name,
                SaleType = SaleType.Credit,
                GrossTotal = 3000m,
                NetTotal = 3000m,
                Status = SaleInvoiceStatus.Posted,
                PostedAtUtc = DateTime.UtcNow
            };

            // Seed SaleInvoice for Customer 2
            var sinv2 = new SaleInvoice
            {
                InvoiceNumber = "SINV-202609-000502",
                OperationId = Guid.NewGuid(),
                BusinessDate = DateOnly.FromDateTime(DateTime.Today),
                CustomerId = customer2Id,
                CustomerNameSnapshot = cust2.Name,
                SaleType = SaleType.Credit,
                GrossTotal = 1500m,
                NetTotal = 1500m,
                Status = SaleInvoiceStatus.Posted,
                PostedAtUtc = DateTime.UtcNow
            };

            // Seed PurchaseInvoice for Supplier
            var pinv = new PurchaseInvoice
            {
                InvoiceNumber = "PINV-202609-000501",
                SupplierId = supplierId,
                InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
                GrossTotal = 4000m,
                NetTotal = 4000m,
                Status = PurchaseInvoiceStatus.Posted,
                PostedAtUtc = DateTime.UtcNow
            };

            db.SaleInvoices.AddRange(sinv1, sinv2);
            db.PurchaseInvoices.Add(pinv);
            await db.SaveChangesAsync();

            saleInv1Id = sinv1.Id;
            saleInv2Id = sinv2.Id;
            purchaseInvId = pinv.Id;
        }

        // 2. RECEIPT VOUCHER ALLOCATION WORKFLOW
        var rv = await voucherService.CreateReceiptVoucherAsync(new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = arAcc!.Id,
            CustomerId = customer1Id,
            Amount = 3000m,
            Narration = "Cheque collection Ali Pharmacy"
        });

        // Check Open Invoices before allocation
        var openInvsBefore = await settlementService.GetCustomerOpenInvoicesAsync(customer1Id);
        var inv1Before = openInvsBefore.Single(i => i.InvoiceId == saleInv1Id);
        Assert.Equal(3000m, inv1Before.OutstandingBalance);
        Assert.Equal(0m, inv1Before.TotalAllocated);
        Assert.Equal("Unpaid", inv1Before.Status);

        // Partial Allocation (1200m)
        var alloc1 = await settlementService.AllocateReceiptVoucherAsync(new ReceiptAllocationCreateDto
        {
            ReceiptVoucherId = rv.Id,
            SaleInvoiceId = saleInv1Id,
            AllocatedAmount = 1200m
        });
        Assert.Equal(1200m, alloc1.AllocatedAmount);

        var openInvsPart = await settlementService.GetCustomerOpenInvoicesAsync(customer1Id);
        var inv1Part = openInvsPart.Single(i => i.InvoiceId == saleInv1Id);
        Assert.Equal(1200m, inv1Part.TotalAllocated);
        Assert.Equal(1800m, inv1Part.OutstandingBalance);
        Assert.Equal("PartiallyPaid", inv1Part.Status);

        // Complete Settle (remaining 1800m)
        var alloc2 = await settlementService.AllocateReceiptVoucherAsync(new ReceiptAllocationCreateDto
        {
            ReceiptVoucherId = rv.Id,
            SaleInvoiceId = saleInv1Id,
            AllocatedAmount = 1800m
        });
        Assert.Equal(1800m, alloc2.AllocatedAmount);

        var openInvsFull = await settlementService.GetCustomerOpenInvoicesAsync(customer1Id);
        Assert.DoesNotContain(openInvsFull, i => i.InvoiceId == saleInv1Id);

        var rvAllocs = await settlementService.GetReceiptVoucherAllocationsAsync(rv.Id);
        Assert.Equal(2, rvAllocs.Count);
        Assert.Equal(3000m, rvAllocs.Sum(a => a.AllocatedAmount));

        // Guard: Over-allocation on invoice (already paid)
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await settlementService.AllocateReceiptVoucherAsync(new ReceiptAllocationCreateDto
            {
                ReceiptVoucherId = rv.Id,
                SaleInvoiceId = saleInv1Id,
                AllocatedAmount = 1m
            });
        });

        // Guard: Voucher remaining unallocated balance (RV total 3000m is fully allocated)
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await settlementService.AllocateReceiptVoucherAsync(new ReceiptAllocationCreateDto
            {
                ReceiptVoucherId = rv.Id,
                SaleInvoiceId = saleInv2Id,
                AllocatedAmount = 500m
            });
        });

        // Guard: Cross-party mismatch (allocate Customer 1's RV to Customer 2's invoice)
        var rv2 = await voucherService.CreateReceiptVoucherAsync(new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc.Id,
            OffsetAccountId = arAcc.Id,
            CustomerId = customer1Id,
            Amount = 1000m,
            Narration = "Second RV for Cust 1"
        });

        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await settlementService.AllocateReceiptVoucherAsync(new ReceiptAllocationCreateDto
            {
                ReceiptVoucherId = rv2.Id,
                SaleInvoiceId = saleInv2Id, // Belongs to Customer 2
                AllocatedAmount = 500m
            });
        });

        // 3. PAYMENT VOUCHER ALLOCATION WORKFLOW
        var pv = await voucherService.CreatePaymentVoucherAsync(new PaymentVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc.Id,
            OffsetAccountId = apAcc!.Id,
            SupplierId = supplierId,
            Amount = 4000m,
            Narration = "Supplier settlement Pfizer"
        });

        // Partial allocate PV (2500m)
        var pAlloc1 = await settlementService.AllocatePaymentVoucherAsync(new PaymentAllocationCreateDto
        {
            PaymentVoucherId = pv.Id,
            PurchaseInvoiceId = purchaseInvId,
            AllocatedAmount = 2500m
        });
        Assert.Equal(2500m, pAlloc1.AllocatedAmount);

        var supInvsPart = await settlementService.GetSupplierOpenInvoicesAsync(supplierId);
        var pInvPart = supInvsPart.Single(i => i.InvoiceId == purchaseInvId);
        Assert.Equal(2500m, pInvPart.TotalAllocated);
        Assert.Equal(1500m, pInvPart.OutstandingBalance);
        Assert.Equal("PartiallyPaid", pInvPart.Status);

        // Full allocate remaining (1500m)
        var pAlloc2 = await settlementService.AllocatePaymentVoucherAsync(new PaymentAllocationCreateDto
        {
            PaymentVoucherId = pv.Id,
            PurchaseInvoiceId = purchaseInvId,
            AllocatedAmount = 1500m
        });
        Assert.Equal(1500m, pAlloc2.AllocatedAmount);

        var supInvsFull = await settlementService.GetSupplierOpenInvoicesAsync(supplierId);
        Assert.DoesNotContain(supInvsFull, i => i.InvoiceId == purchaseInvId);

        var pvAllocs = await settlementService.GetPaymentVoucherAllocationsAsync(pv.Id);
        Assert.Equal(2, pvAllocs.Count);
        Assert.Equal(4000m, pvAllocs.Sum(a => a.AllocatedAmount));

        // Guard: Over-allocation
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await settlementService.AllocatePaymentVoucherAsync(new PaymentAllocationCreateDto
            {
                PaymentVoucherId = pv.Id,
                PurchaseInvoiceId = purchaseInvId,
                AllocatedAmount = 1m
            });
        });
    }


    // 17. SETTLEMENT PARTY INTEGRITY: STRICT NULL & CROSS-PARTY REJECTION
    [Fact]
    public async Task SettlementPartyIntegrity_StrictValidation_RejectsInvalidOrNullParties()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var arAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl);
        var apAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsPayableControl);
        var salesAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.SalesRevenue);

        var custRepo = _fixture.CreateCustomerRepository();
        var cust1 = await custRepo.AddAsync(new Customer { Name = "Cust 1", CustomerCode = "CUST-PI-1", IsActive = true });
        var cust2 = await custRepo.AddAsync(new Customer { Name = "Cust 2", CustomerCode = "CUST-PI-2", IsActive = true });

        var supRepo = _fixture.CreateSupplierRepository();
        var sup1 = await supRepo.AddAsync(new Supplier { Name = "Sup 1", SupplierCode = "SUP-PI-1", IsActive = true });
        var sup2 = await supRepo.AddAsync(new Supplier { Name = "Sup 2", SupplierCode = "SUP-PI-2", IsActive = true });

        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Party Test Med", ProductCode = "PTM-001", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;

            var b = new ProductBatch
            {
                ProductId = productId,
                BatchNumber = "B-PI-1",
                NormalizedBatchNumber = "B-PI-1",
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                QuantityOnHand = 200m,
                AveragePurchaseCost = 10m,
                InventoryValue = 2000m,
                SuggestedSalePrice = 20m,
                IsActive = true
            };
            db.ProductBatches.Add(b);
            await db.SaveChangesAsync();
        }

        var saleWriter = _fixture.CreateSaleWriter();
        var saleInv1 = await saleWriter.CreateAndPostSaleInvoiceAsync("SINV-PI-1", new SaleInvoiceCreateDto
        {
            CustomerId = cust1.Id,
            SaleType = SaleType.Credit,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 5m, UnitSalePrice = 20m }
            }
        });

        var purchWriter = _fixture.CreatePurchaseWriter();
        var purchInv1 = await purchWriter.CreateAndPostPurchaseInvoiceAsync("PINV-PI-1", new PurchaseInvoiceCreateDto
        {
            SupplierId = sup1.Id,
            SupplierInvoiceNumber = "SUP-PI-INV-1",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items = new List<PurchaseInvoiceItemCreateDto>
            {
                new()
                {
                    ProductId = productId,
                    BatchNumber = "B-PI-PURCH",
                    ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(2)),
                    Quantity = 10m,
                    PurchaseRate = 10m,
                    SuggestedSaleRate = 20m
                }
            }
        });

        var settlementService = GetSettlementService();

        // 1. General RV without CustomerId cannot allocate to sale invoice
        var rvGeneral = await voucherService.CreateReceiptVoucherAsync(new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = salesAcc!.Id,
            CustomerId = null,
            Amount = 100m,
            Narration = "General Cash Receipt"
        });

        var exRvGen = await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await settlementService.AllocateReceiptVoucherAsync(new ReceiptAllocationCreateDto
            {
                ReceiptVoucherId = rvGeneral.Id,
                SaleInvoiceId = saleInv1.Id,
                AllocatedAmount = 50m
            });
        });
        Assert.Contains("general receipt voucher", exRvGen.Message, StringComparison.OrdinalIgnoreCase);

        // 2. Cross-party customer mismatch rejected
        var rvCust2 = await voucherService.CreateReceiptVoucherAsync(new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc.Id,
            OffsetAccountId = arAcc!.Id,
            CustomerId = cust2.Id,
            Amount = 100m,
            Narration = "Cust 2 RV"
        });

        var exCustMismatch = await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await settlementService.AllocateReceiptVoucherAsync(new ReceiptAllocationCreateDto
            {
                ReceiptVoucherId = rvCust2.Id,
                SaleInvoiceId = saleInv1.Id, // Belongs to Cust 1
                AllocatedAmount = 50m
            });
        });
        Assert.Contains("does not match", exCustMismatch.Message, StringComparison.OrdinalIgnoreCase);

        // 3. General PV without SupplierId cannot allocate to purchase invoice
        var salariesAcc = await accountService.GetByCodeAsync("5020");
        var pvGeneral = await voucherService.CreatePaymentVoucherAsync(new PaymentVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc.Id,
            OffsetAccountId = salariesAcc!.Id,
            SupplierId = null,
            Amount = 100m,
            Narration = "General Cash Payment"
        });


        var exPvGen = await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await settlementService.AllocatePaymentVoucherAsync(new PaymentAllocationCreateDto
            {
                PaymentVoucherId = pvGeneral.Id,
                PurchaseInvoiceId = purchInv1.Id,
                AllocatedAmount = 50m
            });
        });
        Assert.Contains("general payment voucher", exPvGen.Message, StringComparison.OrdinalIgnoreCase);

        // 4. Cross-party supplier mismatch rejected
        var pvSup2 = await voucherService.CreatePaymentVoucherAsync(new PaymentVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc.Id,
            OffsetAccountId = apAcc!.Id,
            SupplierId = sup2.Id,
            Amount = 100m,
            Narration = "Sup 2 PV"

        });

        var exSupMismatch = await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await settlementService.AllocatePaymentVoucherAsync(new PaymentAllocationCreateDto
            {
                PaymentVoucherId = pvSup2.Id,
                PurchaseInvoiceId = purchInv1.Id, // Belongs to Sup 1
                AllocatedAmount = 50m
            });
        });
        Assert.Contains("does not match", exSupMismatch.Message, StringComparison.OrdinalIgnoreCase);
    }

    // 18. CREDIT-ONLY SETTLEMENT: CASH SALES EXCLUDED AND REJECTED
    [Fact]
    public async Task CreditOnlySettlement_RejectsCashSale_ExcludesCashFromOpenInvoices()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var arAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl);

        var custRepo = _fixture.CreateCustomerRepository();
        var cust = await custRepo.AddAsync(new Customer { Name = "Credit Only Cust", CustomerCode = "CUST-CR-1", IsActive = true });

        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Credit Test Item", ProductCode = "CTI-001", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;

            var b = new ProductBatch
            {
                ProductId = productId,
                BatchNumber = "B-CR-1",
                NormalizedBatchNumber = "B-CR-1",
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                QuantityOnHand = 200m,
                AveragePurchaseCost = 10m,
                InventoryValue = 2000m,
                SuggestedSalePrice = 25m,
                IsActive = true
            };
            db.ProductBatches.Add(b);
            await db.SaveChangesAsync();
        }

        var saleWriter = _fixture.CreateSaleWriter();

        // 1. Post Cash Sale for customer
        var cashSale = await saleWriter.CreateAndPostSaleInvoiceAsync("SINV-CR-CASH", new SaleInvoiceCreateDto
        {
            CustomerId = cust.Id,
            SaleType = SaleType.Cash,
            TenderedAmount = 50m,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 2m, UnitSalePrice = 25m }
            }
        });

        // 2. Post Credit Sale for same customer
        var creditSale = await saleWriter.CreateAndPostSaleInvoiceAsync("SINV-CR-CREDIT", new SaleInvoiceCreateDto
        {
            CustomerId = cust.Id,
            SaleType = SaleType.Credit,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 4m, UnitSalePrice = 25m }
            }
        });

        var settlementService = GetSettlementService();

        // 3. Open invoices must ONLY list the Credit sale
        var openInvoices = await settlementService.GetCustomerOpenInvoicesAsync(cust.Id);
        Assert.Single(openInvoices);
        Assert.Equal(creditSale.Id, openInvoices[0].InvoiceId);
        Assert.DoesNotContain(openInvoices, i => i.InvoiceId == cashSale.Id);

        // 4. Direct allocation against cash sale invoice must be rejected
        var rv = await voucherService.CreateReceiptVoucherAsync(new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = arAcc!.Id,
            CustomerId = cust.Id,
            Amount = 100m,
            Narration = "Cust RV"
        });

        var ex = await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await settlementService.AllocateReceiptVoucherAsync(new ReceiptAllocationCreateDto
            {
                ReceiptVoucherId = rv.Id,
                SaleInvoiceId = cashSale.Id,
                AllocatedAmount = 50m
            });
        });
        Assert.Contains("Only credit sale invoices", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // 19. VOUCHER CANCELLATION BLOCKED WHEN ACTIVE ALLOCATIONS EXIST
    [Fact]
    public async Task VoucherCancellation_BlockedWhenActiveAllocationsExist()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var arAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl);
        var apAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsPayableControl);

        var custRepo = _fixture.CreateCustomerRepository();
        var cust = await custRepo.AddAsync(new Customer { Name = "Cancel Guard Cust", CustomerCode = "CUST-CG-1", IsActive = true });

        var supRepo = _fixture.CreateSupplierRepository();
        var sup = await supRepo.AddAsync(new Supplier { Name = "Cancel Guard Sup", SupplierCode = "SUP-CG-1", IsActive = true });

        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Cancel Guard Item", ProductCode = "CGI-001", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;

            var b = new ProductBatch
            {
                ProductId = productId,
                BatchNumber = "B-CG-1",
                NormalizedBatchNumber = "B-CG-1",
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                QuantityOnHand = 200m,
                AveragePurchaseCost = 10m,
                InventoryValue = 2000m,
                SuggestedSalePrice = 30m,
                IsActive = true
            };
            db.ProductBatches.Add(b);
            await db.SaveChangesAsync();
        }

        var saleWriter = _fixture.CreateSaleWriter();
        var saleInv = await saleWriter.CreateAndPostSaleInvoiceAsync("SINV-CG-1", new SaleInvoiceCreateDto
        {
            CustomerId = cust.Id,
            SaleType = SaleType.Credit,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 5m, UnitSalePrice = 30m }
            }
        });

        var purchWriter = _fixture.CreatePurchaseWriter();
        var purchInv = await purchWriter.CreateAndPostPurchaseInvoiceAsync("PINV-CG-1", new PurchaseInvoiceCreateDto
        {
            SupplierId = sup.Id,
            SupplierInvoiceNumber = "SUP-CG-INV-1",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items = new List<PurchaseInvoiceItemCreateDto>
            {
                new()
                {
                    ProductId = productId,
                    BatchNumber = "B-CG-PURCH",
                    ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(2)),
                    Quantity = 10m,
                    PurchaseRate = 10m,
                    SuggestedSaleRate = 30m
                }
            }
        });

        var rv = await voucherService.CreateReceiptVoucherAsync(new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = arAcc!.Id,
            CustomerId = cust.Id,
            Amount = 150m,
            Narration = "RV for Cust"
        });

        var pv = await voucherService.CreatePaymentVoucherAsync(new PaymentVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc.Id,
            OffsetAccountId = apAcc!.Id,
            SupplierId = sup.Id,
            Amount = 100m,
            Narration = "PV for Sup"
        });

        var settlementService = GetSettlementService();
        await settlementService.AllocateReceiptVoucherAsync(new ReceiptAllocationCreateDto
        {
            ReceiptVoucherId = rv.Id,
            SaleInvoiceId = saleInv.Id,
            AllocatedAmount = 50m
        });

        await settlementService.AllocatePaymentVoucherAsync(new PaymentAllocationCreateDto
        {
            PaymentVoucherId = pv.Id,
            PurchaseInvoiceId = purchInv.Id,
            AllocatedAmount = 50m
        });

        // Attempting to cancel RV with active allocation must be blocked
        var exRv = await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await voucherService.CancelReceiptVoucherAsync(rv.Id, "Attempted cancel");
        });
        Assert.Contains("active invoice settlement allocations", exRv.Message, StringComparison.OrdinalIgnoreCase);

        // Attempting to cancel PV with active allocation must be blocked
        var exPv = await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await voucherService.CancelPaymentVoucherAsync(pv.Id, "Attempted cancel");
        });
        Assert.Contains("active invoice settlement allocations", exPv.Message, StringComparison.OrdinalIgnoreCase);
    }

    // 20. INVOICE CANCELLATION BLOCKED WHEN ACTIVE ALLOCATIONS EXIST
    [Fact]
    public async Task InvoiceCancellation_BlockedWhenActiveAllocationsExist()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var arAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl);
        var apAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsPayableControl);

        var custRepo = _fixture.CreateCustomerRepository();
        var cust = await custRepo.AddAsync(new Customer { Name = "Inv Cancel Cust", CustomerCode = "CUST-IC-1", IsActive = true });

        var supRepo = _fixture.CreateSupplierRepository();
        var sup = await supRepo.AddAsync(new Supplier { Name = "Inv Cancel Sup", SupplierCode = "SUP-IC-1", IsActive = true });

        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Inv Cancel Item", ProductCode = "ICI-001", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;

            var b = new ProductBatch
            {
                ProductId = productId,
                BatchNumber = "B-IC-1",
                NormalizedBatchNumber = "B-IC-1",
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                QuantityOnHand = 200m,
                AveragePurchaseCost = 10m,
                InventoryValue = 2000m,
                SuggestedSalePrice = 40m,
                IsActive = true
            };
            db.ProductBatches.Add(b);
            await db.SaveChangesAsync();
        }

        var saleWriter = _fixture.CreateSaleWriter();
        var saleInv = await saleWriter.CreateAndPostSaleInvoiceAsync("SINV-IC-1", new SaleInvoiceCreateDto
        {
            CustomerId = cust.Id,
            SaleType = SaleType.Credit,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 5m, UnitSalePrice = 40m }
            }
        });

        var purchWriter = _fixture.CreatePurchaseWriter();
        var purchInv = await purchWriter.CreateAndPostPurchaseInvoiceAsync("PINV-IC-1", new PurchaseInvoiceCreateDto
        {
            SupplierId = sup.Id,
            SupplierInvoiceNumber = "SUP-IC-INV-1",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items = new List<PurchaseInvoiceItemCreateDto>
            {
                new()
                {
                    ProductId = productId,
                    BatchNumber = "B-IC-PURCH",
                    ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(2)),
                    Quantity = 10m,
                    PurchaseRate = 10m,
                    SuggestedSaleRate = 40m
                }
            }
        });

        var rv = await voucherService.CreateReceiptVoucherAsync(new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = arAcc!.Id,
            CustomerId = cust.Id,
            Amount = 200m,
            Narration = "RV for Cust"
        });

        var pv = await voucherService.CreatePaymentVoucherAsync(new PaymentVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc.Id,
            OffsetAccountId = apAcc!.Id,
            SupplierId = sup.Id,
            Amount = 100m,
            Narration = "PV for Sup"
        });

        var settlementService = GetSettlementService();
        await settlementService.AllocateReceiptVoucherAsync(new ReceiptAllocationCreateDto
        {
            ReceiptVoucherId = rv.Id,
            SaleInvoiceId = saleInv.Id,
            AllocatedAmount = 100m
        });

        await settlementService.AllocatePaymentVoucherAsync(new PaymentAllocationCreateDto
        {
            PaymentVoucherId = pv.Id,
            PurchaseInvoiceId = purchInv.Id,
            AllocatedAmount = 100m
        });

        // Cancelling sale invoice with active RV allocation must be blocked
        var exSale = await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await saleWriter.CancelSaleInvoiceAsync(saleInv.Id, "Attempted invoice cancel");
        });
        Assert.Contains("active receipt voucher settlement allocations", exSale.Message, StringComparison.OrdinalIgnoreCase);

        // Cancelling purchase invoice with active PV allocation must be blocked
        var exPurch = await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await purchWriter.CancelPurchaseInvoiceAsync(purchInv.Id, "Attempted purchase invoice cancel");
        });
        Assert.Contains("active payment voucher settlement allocations", exPurch.Message, StringComparison.OrdinalIgnoreCase);
    }

    // 21. RETURN AFTER PAYMENT: CLAMPS TO ZERO & BLOCKS OVER-SETTLEMENT
    [Fact]
    public async Task ReturnAfterPayment_OverSettlement_ClampsToZero()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var arAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl);

        var custRepo = _fixture.CreateCustomerRepository();
        var cust = await custRepo.AddAsync(new Customer { Name = "Return Cust", CustomerCode = "CUST-RET-1", IsActive = true });

        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Return Test Med", ProductCode = "RTM-001", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;

            var b = new ProductBatch
            {
                ProductId = productId,
                BatchNumber = "B-RET-1",
                NormalizedBatchNumber = "B-RET-1",
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                QuantityOnHand = 200m,
                AveragePurchaseCost = 10m,
                InventoryValue = 2000m,
                SuggestedSalePrice = 50m,
                IsActive = true
            };
            db.ProductBatches.Add(b);
            await db.SaveChangesAsync();
        }

        var saleWriter = _fixture.CreateSaleWriter();
        var saleInv = await saleWriter.CreateAndPostSaleInvoiceAsync("SINV-RET-1", new SaleInvoiceCreateDto
        {
            CustomerId = cust.Id,
            SaleType = SaleType.Credit,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 2m, UnitSalePrice = 50m } // NetTotal = 100m
            }
        });

        var settlementService = GetSettlementService();

        // Fully allocate receipt voucher of 100m
        var rv = await voucherService.CreateReceiptVoucherAsync(new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = arAcc!.Id,
            CustomerId = cust.Id,
            Amount = 100m,
            Narration = "Full payment"
        });

        await settlementService.AllocateReceiptVoucherAsync(new ReceiptAllocationCreateDto
        {
            ReceiptVoucherId = rv.Id,
            SaleInvoiceId = saleInv.Id,
            AllocatedAmount = 100m
        });

        // Customer returns 1 item (50m refund)
        var returnItem = saleInv.Items.First();
        await saleWriter.CreateAndPostSaleReturnAsync("SRET-RET-1", new SaleReturnCreateDto
        {
            OriginalSaleInvoiceId = saleInv.Id,
            Items = new List<SaleReturnItemCreateDto>
            {
                new()
                {
                    OriginalSaleInvoiceItemId = returnItem.Id,
                    Quantity = 1m
                }
            }
        });

        // OutstandingBalance is clamped to 0 (Net 50m - Allocated 100m = -50m -> clamped to 0m)
        var openInvs = await settlementService.GetCustomerOpenInvoicesAsync(cust.Id);
        Assert.Empty(openInvs); // Fully settled / negative outstanding balance excluded from open invoices

        // Creating another RV and trying to allocate to this invoice must fail
        var rvExtra = await voucherService.CreateReceiptVoucherAsync(new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc.Id,
            OffsetAccountId = arAcc.Id,
            CustomerId = cust.Id,
            Amount = 50m,
            Narration = "Extra RV"
        });

        var ex = await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await settlementService.AllocateReceiptVoucherAsync(new ReceiptAllocationCreateDto
            {
                ReceiptVoucherId = rvExtra.Id,
                SaleInvoiceId = saleInv.Id,
                AllocatedAmount = 10m
            });
        });
        Assert.Contains("exceeds invoice outstanding balance", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // 22. CONCURRENCY: CONCURRENT ALLOCATIONS ON SAME SALE INVOICE
    [Fact]
    public async Task ConcurrentAllocations_SameSaleInvoice_PreventsOverAllocation()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var arAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl);

        var custRepo = _fixture.CreateCustomerRepository();
        var cust = await custRepo.AddAsync(new Customer { Name = "Concurrent Cust 1", CustomerCode = "CUST-CC-1", IsActive = true });

        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Concurrency Item 1", ProductCode = "CCI-001", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;

            var b = new ProductBatch
            {
                ProductId = productId,
                BatchNumber = "B-CC-1",
                NormalizedBatchNumber = "B-CC-1",
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                QuantityOnHand = 200m,
                AveragePurchaseCost = 10m,
                InventoryValue = 2000m,
                SuggestedSalePrice = 1000m,
                IsActive = true
            };
            db.ProductBatches.Add(b);
            await db.SaveChangesAsync();
        }

        var saleWriter = _fixture.CreateSaleWriter();
        var saleInv = await saleWriter.CreateAndPostSaleInvoiceAsync("SINV-CC-1", new SaleInvoiceCreateDto
        {
            CustomerId = cust.Id,
            SaleType = SaleType.Credit,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 1m, UnitSalePrice = 1000m } // NetTotal = 1000m
            }
        });

        var rv1 = await voucherService.CreateReceiptVoucherAsync(new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = arAcc!.Id,
            CustomerId = cust.Id,
            Amount = 600m,
            Narration = "RV 1"
        });

        var rv2 = await voucherService.CreateReceiptVoucherAsync(new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc.Id,
            OffsetAccountId = arAcc.Id,
            CustomerId = cust.Id,
            Amount = 600m,
            Narration = "RV 2"
        });

        // Two concurrent allocation attempts for 600m against 1000m invoice (600 + 600 = 1200 > 1000)
        var repo1 = _fixture.CreateSettlementRepository();
        var repo2 = _fixture.CreateSettlementRepository();

        var t1 = Task.Run(async () =>
        {
            try
            {
                await repo1.AllocateReceiptAsync(new ReceiptAllocationCreateDto
                {
                    ReceiptVoucherId = rv1.Id,
                    SaleInvoiceId = saleInv.Id,
                    AllocatedAmount = 600m
                });
                return (Success: true, Error: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Success: false, Error: ex);
            }
        });

        var t2 = Task.Run(async () =>
        {
            try
            {
                await repo2.AllocateReceiptAsync(new ReceiptAllocationCreateDto
                {
                    ReceiptVoucherId = rv2.Id,
                    SaleInvoiceId = saleInv.Id,
                    AllocatedAmount = 600m
                });
                return (Success: true, Error: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Success: false, Error: ex);
            }
        });

        var results = await Task.WhenAll(t1, t2);

        // At most one can succeed, preventing over-allocation
        int successCount = results.Count(r => r.Success);
        Assert.Equal(1, successCount);

        // Invariant: Total allocated on invoice must be exactly 600m, never 1200m
        await using var verifyDb = await _fixture.ContextFactory.CreateDbContextAsync();
        var totalAllocated = await verifyDb.ReceiptVoucherAllocations
            .Where(a => a.SaleInvoiceId == saleInv.Id)
            .SumAsync(a => a.AllocatedAmount);
        Assert.Equal(600m, totalAllocated);
    }

    // 23. CONCURRENCY: CONCURRENT ALLOCATIONS FROM SAME RECEIPT VOUCHER
    [Fact]
    public async Task ConcurrentAllocations_SameReceiptVoucher_PreventsOverAllocation()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var arAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl);

        var custRepo = _fixture.CreateCustomerRepository();
        var cust = await custRepo.AddAsync(new Customer { Name = "Concurrent Cust 2", CustomerCode = "CUST-CC-2", IsActive = true });

        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Concurrency Item 2", ProductCode = "CCI-002", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;

            var b = new ProductBatch
            {
                ProductId = productId,
                BatchNumber = "B-CC-2",
                NormalizedBatchNumber = "B-CC-2",
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                QuantityOnHand = 200m,
                AveragePurchaseCost = 10m,
                InventoryValue = 2000m,
                SuggestedSalePrice = 600m,
                IsActive = true
            };
            db.ProductBatches.Add(b);
            await db.SaveChangesAsync();
        }

        var saleWriter = _fixture.CreateSaleWriter();
        var saleInv1 = await saleWriter.CreateAndPostSaleInvoiceAsync("SINV-CC-2A", new SaleInvoiceCreateDto
        {
            CustomerId = cust.Id,
            SaleType = SaleType.Credit,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 1m, UnitSalePrice = 600m }
            }
        });

        var saleInv2 = await saleWriter.CreateAndPostSaleInvoiceAsync("SINV-CC-2B", new SaleInvoiceCreateDto
        {
            CustomerId = cust.Id,
            SaleType = SaleType.Credit,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 1m, UnitSalePrice = 600m }
            }
        });

        // Receipt Voucher of 1000m
        var rv = await voucherService.CreateReceiptVoucherAsync(new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = arAcc!.Id,
            CustomerId = cust.Id,
            Amount = 1000m,
            Narration = "RV 1000m"
        });

        // Two concurrent allocation attempts for 600m each from the same 1000m RV (600 + 600 = 1200 > 1000)
        var repo1 = _fixture.CreateSettlementRepository();
        var repo2 = _fixture.CreateSettlementRepository();

        var t1 = Task.Run(async () =>
        {
            try
            {
                await repo1.AllocateReceiptAsync(new ReceiptAllocationCreateDto
                {
                    ReceiptVoucherId = rv.Id,
                    SaleInvoiceId = saleInv1.Id,
                    AllocatedAmount = 600m
                });
                return (Success: true, Error: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Success: false, Error: ex);
            }
        });

        var t2 = Task.Run(async () =>
        {
            try
            {
                await repo2.AllocateReceiptAsync(new ReceiptAllocationCreateDto
                {
                    ReceiptVoucherId = rv.Id,
                    SaleInvoiceId = saleInv2.Id,
                    AllocatedAmount = 600m
                });
                return (Success: true, Error: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Success: false, Error: ex);
            }
        });

        var results = await Task.WhenAll(t1, t2);

        int successCount = results.Count(r => r.Success);
        Assert.Equal(1, successCount);

        // Invariant: Total allocated on voucher must be exactly 600m, never 1200m
        await using var verifyDb = await _fixture.ContextFactory.CreateDbContextAsync();
        var totalAllocated = await verifyDb.ReceiptVoucherAllocations
            .Where(a => a.ReceiptVoucherId == rv.Id)
            .SumAsync(a => a.AllocatedAmount);
        Assert.Equal(600m, totalAllocated);
    }

    // 24. CONCURRENCY: CONCURRENT ALLOCATIONS ON SAME PURCHASE INVOICE
    [Fact]
    public async Task ConcurrentAllocations_SamePurchaseInvoice_PreventsOverAllocation()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var apAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsPayableControl);

        var supRepo = _fixture.CreateSupplierRepository();
        var sup = await supRepo.AddAsync(new Supplier { Name = "Concurrent Sup 1", SupplierCode = "SUP-CC-1", IsActive = true });

        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Purch Concurrency Item 1", ProductCode = "PCI-001", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;
        }

        var purchWriter = _fixture.CreatePurchaseWriter();
        var purchInv = await purchWriter.CreateAndPostPurchaseInvoiceAsync("PINV-CC-1", new PurchaseInvoiceCreateDto
        {
            SupplierId = sup.Id,
            SupplierInvoiceNumber = "SUP-CC-INV-1",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items = new List<PurchaseInvoiceItemCreateDto>
            {
                new()
                {
                    ProductId = productId,
                    BatchNumber = "B-PCC-1",
                    ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(2)),
                    Quantity = 10m,
                    PurchaseRate = 100m, // NetTotal = 1000m
                    SuggestedSaleRate = 150m
                }
            }
        });

        var pv1 = await voucherService.CreatePaymentVoucherAsync(new PaymentVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = apAcc!.Id,
            SupplierId = sup.Id,
            Amount = 600m,
            Narration = "PV 1"
        });

        var pv2 = await voucherService.CreatePaymentVoucherAsync(new PaymentVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc.Id,
            OffsetAccountId = apAcc.Id,
            SupplierId = sup.Id,
            Amount = 600m,
            Narration = "PV 2"
        });

        // Two concurrent allocation attempts for 600m each against 1000m purchase invoice
        var repo1 = _fixture.CreateSettlementRepository();
        var repo2 = _fixture.CreateSettlementRepository();

        var t1 = Task.Run(async () =>
        {
            try
            {
                await repo1.AllocatePaymentAsync(new PaymentAllocationCreateDto
                {
                    PaymentVoucherId = pv1.Id,
                    PurchaseInvoiceId = purchInv.Id,
                    AllocatedAmount = 600m
                });
                return (Success: true, Error: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Success: false, Error: ex);
            }
        });

        var t2 = Task.Run(async () =>
        {
            try
            {
                await repo2.AllocatePaymentAsync(new PaymentAllocationCreateDto
                {
                    PaymentVoucherId = pv2.Id,
                    PurchaseInvoiceId = purchInv.Id,
                    AllocatedAmount = 600m
                });
                return (Success: true, Error: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Success: false, Error: ex);
            }
        });

        var results = await Task.WhenAll(t1, t2);

        int successCount = results.Count(r => r.Success);
        Assert.Equal(1, successCount);

        // Invariant: Total allocated on purchase invoice must be exactly 600m, never 1200m
        await using var verifyDb = await _fixture.ContextFactory.CreateDbContextAsync();
        var totalAllocated = await verifyDb.PaymentVoucherAllocations
            .Where(a => a.PurchaseInvoiceId == purchInv.Id)
            .SumAsync(a => a.AllocatedAmount);
        Assert.Equal(600m, totalAllocated);
    }

    // 25. CONCURRENCY: CONCURRENT ALLOCATIONS FROM SAME PAYMENT VOUCHER
    [Fact]
    public async Task ConcurrentAllocations_SamePaymentVoucher_PreventsOverAllocation()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var apAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsPayableControl);

        var supRepo = _fixture.CreateSupplierRepository();
        var sup = await supRepo.AddAsync(new Supplier { Name = "Concurrent Sup 2", SupplierCode = "SUP-CC-2", IsActive = true });

        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Purch Concurrency Item 2", ProductCode = "PCI-002", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;
        }

        var purchWriter = _fixture.CreatePurchaseWriter();
        var purchInv1 = await purchWriter.CreateAndPostPurchaseInvoiceAsync("PINV-CC-2A", new PurchaseInvoiceCreateDto
        {
            SupplierId = sup.Id,
            SupplierInvoiceNumber = "SUP-CC-INV-2A",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items = new List<PurchaseInvoiceItemCreateDto>
            {
                new()
                {
                    ProductId = productId,
                    BatchNumber = "B-PCC-2A",
                    ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(2)),
                    Quantity = 6m,
                    PurchaseRate = 100m, // NetTotal = 600m
                    SuggestedSaleRate = 150m
                }
            }
        });

        var purchInv2 = await purchWriter.CreateAndPostPurchaseInvoiceAsync("PINV-CC-2B", new PurchaseInvoiceCreateDto
        {
            SupplierId = sup.Id,
            SupplierInvoiceNumber = "SUP-CC-INV-2B",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items = new List<PurchaseInvoiceItemCreateDto>
            {
                new()
                {
                    ProductId = productId,
                    BatchNumber = "B-PCC-2B",
                    ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(2)),
                    Quantity = 6m,
                    PurchaseRate = 100m, // NetTotal = 600m
                    SuggestedSaleRate = 150m
                }
            }
        });

        // Payment Voucher for 1000m
        var pv = await voucherService.CreatePaymentVoucherAsync(new PaymentVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = apAcc!.Id,
            SupplierId = sup.Id,
            Amount = 1000m,
            Narration = "PV 1000m"
        });

        // Two concurrent allocation attempts for 600m each from the same 1000m PV
        var repo1 = _fixture.CreateSettlementRepository();
        var repo2 = _fixture.CreateSettlementRepository();

        var t1 = Task.Run(async () =>
        {
            try
            {
                await repo1.AllocatePaymentAsync(new PaymentAllocationCreateDto
                {
                    PaymentVoucherId = pv.Id,
                    PurchaseInvoiceId = purchInv1.Id,
                    AllocatedAmount = 600m
                });
                return (Success: true, Error: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Success: false, Error: ex);
            }
        });

        var t2 = Task.Run(async () =>
        {
            try
            {
                await repo2.AllocatePaymentAsync(new PaymentAllocationCreateDto
                {
                    PaymentVoucherId = pv.Id,
                    PurchaseInvoiceId = purchInv2.Id,
                    AllocatedAmount = 600m
                });
                return (Success: true, Error: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Success: false, Error: ex);
            }
        });

        var results = await Task.WhenAll(t1, t2);

        int successCount = results.Count(r => r.Success);
        Assert.Equal(1, successCount);

        // Invariant: Total allocated on payment voucher must be exactly 600m, never 1200m
        await using var verifyDb = await _fixture.ContextFactory.CreateDbContextAsync();
        var totalAllocated = await verifyDb.PaymentVoucherAllocations
            .Where(a => a.PaymentVoucherId == pv.Id)
            .SumAsync(a => a.AllocatedAmount);
        Assert.Equal(600m, totalAllocated);
    }

    // 26. PROFIT & LOSS: AGGREGATES CHILD ACCOUNTS & EXCLUDES DRAFT AND DATE RANGE
    [Fact]
    public async Task ProfitAndLoss_AggregatesChildAccounts_ExcludesDraftAndDateRange()
    {
        var (accountService, _, _, journalService, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var salesAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.SalesRevenue);
        var cogsAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CostOfGoodsSold);
        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);

        // Create custom child revenue and expense accounts
        var childRev = await accountService.CreateAccountAsync(new AccountCreateDto
        {
            AccountCode = "4050",
            Name = "Delivery Service Revenue",
            AccountType = AccountType.Revenue,
            ParentAccountId = salesAcc!.ParentAccountId,
            AllowPosting = true
        });


        var childExp = await accountService.CreateAccountAsync(new AccountCreateDto
        {
            AccountCode = "5090",
            Name = "Stationery and Packaging",
            AccountType = AccountType.Expense,
            ParentAccountId = cogsAcc!.ParentAccountId,
            AllowPosting = true
        });

        var today = DateOnly.FromDateTime(DateTime.Today);
        var pastDate = today.AddDays(-30);
        var futureDate = today.AddDays(30);

        // 1. Post valid journal within range: Rev 2000, Exp 800, Cash 1200
        var acctWriter = _fixture.CreateAccountingWriter();
        var jWithin = new JournalEntry
        {
            EntryNumber = "JE-PL-WITHIN",
            BusinessDate = today,
            PostedAtUtc = DateTime.UtcNow,
            SourceDocumentType = JournalSourceDocumentType.Manual,
            PostingRole = JournalPostingRole.Primary,
            OperationId = Guid.NewGuid(),
            Narration = "Within Range Operational Entry"
        };
        var linesWithin = new List<JournalEntryLine>
        {
            new() { AccountId = cashAcc!.Id, DebitAmount = 1200m, CreditAmount = 0m, Narration = "Cash Inflow" },
            new() { AccountId = childRev.Id, DebitAmount = 0m, CreditAmount = 2000m, Narration = "Child Rev" },
            new() { AccountId = childExp.Id, DebitAmount = 800m, CreditAmount = 0m, Narration = "Child Exp" }
        };
        await acctWriter.PostManualJournalAsync(jWithin, linesWithin);

        // 2. Insert Draft journal (must be EXCLUDED from P&L)
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var jDraft = new JournalEntry
            {
                EntryNumber = "JE-PL-DRAFT",
                BusinessDate = today,
                PostedAtUtc = DateTime.UtcNow,
                SourceDocumentType = JournalSourceDocumentType.Manual,
                PostingRole = JournalPostingRole.Primary,
                OperationId = Guid.NewGuid(),
                Narration = "Draft Entry",
                Status = JournalEntryStatus.Draft,
                TotalDebit = 5000m,
                TotalCredit = 5000m
            };
            jDraft.Lines.Add(new JournalEntryLine { AccountId = cashAcc.Id, DebitAmount = 5000m, CreditAmount = 0m });
            jDraft.Lines.Add(new JournalEntryLine { AccountId = childRev.Id, DebitAmount = 0m, CreditAmount = 5000m });
            db.JournalEntries.Add(jDraft);
            await db.SaveChangesAsync();
        }

        // 3. Post out-of-range journal (in past, outside [today, today])
        var jOutOfRange = new JournalEntry
        {
            EntryNumber = "JE-PL-OUT",
            BusinessDate = pastDate,
            PostedAtUtc = DateTime.UtcNow,
            SourceDocumentType = JournalSourceDocumentType.Manual,
            PostingRole = JournalPostingRole.Primary,
            OperationId = Guid.NewGuid(),
            Narration = "Out of Range Past Entry"
        };
        var linesOut = new List<JournalEntryLine>
        {
            new() { AccountId = cashAcc.Id, DebitAmount = 1000m, CreditAmount = 0m },
            new() { AccountId = childRev.Id, DebitAmount = 0m, CreditAmount = 1000m }
        };
        await acctWriter.PostManualJournalAsync(jOutOfRange, linesOut);

        // Query P&L for exactly 'today'
        var pl = await journalService.GetProfitAndLossAsync(today, today);

        // Verify Draft and Out-of-range are excluded
        Assert.Equal(2000m, pl.TotalRevenue);
        Assert.Equal(800m, pl.TotalExpense);
        Assert.Equal(1200m, pl.NetProfitOrLoss);
        Assert.True(pl.IsProfit);

        // Verify child revenue and expense lines exist with correct amounts
        var revLine = pl.RevenueLines.SingleOrDefault(r => r.AccountId == childRev.Id);
        Assert.NotNull(revLine);
        Assert.Equal(2000m, revLine.Amount);

        var expLine = pl.ExpenseLines.SingleOrDefault(e => e.AccountId == childExp.Id);
        Assert.NotNull(expLine);
        Assert.Equal(800m, expLine.Amount);
    }

    // 27. BALANCE SHEET: BALANCED, INCLUDES ALL TYPES, EXCLUDES DRAFT AND FUTURE
    [Fact]
    public async Task BalanceSheet_AggregatesAllAccountTypes_IsBalanced_ExcludesDraftAndFuture()
    {
        var (accountService, _, _, journalService, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var capitalAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.OpeningBalanceEquity);
        var salesAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.SalesRevenue);
        var expAcc = await accountService.GetByCodeAsync("5020"); // Salaries & Wages (Non-control expense)

        var today = DateOnly.FromDateTime(DateTime.Today);
        var futureDate = today.AddDays(10);
        var acctWriter = _fixture.CreateAccountingWriter();

        // 1. Initial Capital: Debit Cash 5000, Credit OpeningBalanceEquity 5000
        var jCap = new JournalEntry
        {
            EntryNumber = "JE-BS-CAP",
            BusinessDate = today,
            PostedAtUtc = DateTime.UtcNow,
            SourceDocumentType = JournalSourceDocumentType.Manual,
            PostingRole = JournalPostingRole.Primary,
            OperationId = Guid.NewGuid(),
            Narration = "Capital injection"
        };
        await acctWriter.PostManualJournalAsync(jCap, new List<JournalEntryLine>
        {
            new() { AccountId = cashAcc!.Id, DebitAmount = 5000m, CreditAmount = 0m },
            new() { AccountId = capitalAcc!.Id, DebitAmount = 0m, CreditAmount = 5000m }
        });

        // 2. Operational Revenue/Expense: Debit Cash 1000, Credit Sales 1500, Debit Expense 500
        var jOp = new JournalEntry
        {
            EntryNumber = "JE-BS-OP",
            BusinessDate = today,
            PostedAtUtc = DateTime.UtcNow,
            SourceDocumentType = JournalSourceDocumentType.Manual,
            PostingRole = JournalPostingRole.Primary,
            OperationId = Guid.NewGuid(),
            Narration = "Daily operations"
        };
        await acctWriter.PostManualJournalAsync(jOp, new List<JournalEntryLine>
        {
            new() { AccountId = cashAcc.Id, DebitAmount = 1000m, CreditAmount = 0m },
            new() { AccountId = expAcc!.Id, DebitAmount = 500m, CreditAmount = 0m },
            new() { AccountId = salesAcc!.Id, DebitAmount = 0m, CreditAmount = 1500m }
        });


        // 3. Draft journal (must be excluded from Balance Sheet)
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var jDraft = new JournalEntry
            {
                EntryNumber = "JE-BS-DRAFT",
                BusinessDate = today,
                PostedAtUtc = DateTime.UtcNow,
                SourceDocumentType = JournalSourceDocumentType.Manual,
                PostingRole = JournalPostingRole.Primary,
                OperationId = Guid.NewGuid(),
                Narration = "Draft BS Entry",
                Status = JournalEntryStatus.Draft,
                TotalDebit = 9999m,
                TotalCredit = 9999m
            };
            jDraft.Lines.Add(new JournalEntryLine { AccountId = cashAcc.Id, DebitAmount = 9999m, CreditAmount = 0m });
            jDraft.Lines.Add(new JournalEntryLine { AccountId = capitalAcc.Id, DebitAmount = 0m, CreditAmount = 9999m });
            db.JournalEntries.Add(jDraft);
            await db.SaveChangesAsync();
        }

        // 4. Future posted journal (must be excluded from Balance Sheet as of 'today')
        var jFuture = new JournalEntry
        {
            EntryNumber = "JE-BS-FUTURE",
            BusinessDate = futureDate,
            PostedAtUtc = DateTime.UtcNow,
            SourceDocumentType = JournalSourceDocumentType.Manual,
            PostingRole = JournalPostingRole.Primary,
            OperationId = Guid.NewGuid(),
            Narration = "Future Entry"
        };
        await acctWriter.PostManualJournalAsync(jFuture, new List<JournalEntryLine>
        {
            new() { AccountId = cashAcc.Id, DebitAmount = 2000m, CreditAmount = 0m },
            new() { AccountId = capitalAcc.Id, DebitAmount = 0m, CreditAmount = 2000m }
        });

        // Query Balance Sheet as of 'today'
        var bs = await journalService.GetBalanceSheetAsync(today);

        // Verification:
        // Cash = 5000 + 1000 = 6000 (TotalAssets)
        // Equity = 5000
        // CurrentPeriodEarnings = 1500 (Sales) - 500 (COGS) = 1000
        // TotalLiabilitiesAndEquity = 0 + 5000 + 1000 = 6000
        Assert.True(bs.IsBalanced);
        Assert.Equal(0m, bs.Difference);
        Assert.Equal(6000m, bs.TotalAssets);
        Assert.Equal(0m, bs.TotalLiabilities);
        Assert.Equal(5000m, bs.TotalEquity);
        Assert.Equal(1000m, bs.CurrentPeriodEarnings);
        Assert.Equal(6000m, bs.TotalLiabilitiesAndEquity);
    }


    // 25. SETTLEMENT VOID LIFECYCLE: RECEIPT ALLOCATION VOID RESTORES BALANCES & PRESERVES AUDIT
    [Fact]
    public async Task ReceiptAllocation_Void_RestoresInvoiceOutstandingAndVoucherRemaining_PreservesAuditTrail()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var arAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl);

        var custRepo = _fixture.CreateCustomerRepository();
        var cust = await custRepo.AddAsync(new Customer { Name = "Void Cust 1", CustomerCode = "CUST-VD-1", IsActive = true });

        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Void Prod 1", ProductCode = "VP-001", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;

            var b = new ProductBatch
            {
                ProductId = productId,
                BatchNumber = "B-VD-1",
                NormalizedBatchNumber = "B-VD-1",
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                QuantityOnHand = 200m,
                AveragePurchaseCost = 10m,
                InventoryValue = 2000m,
                SuggestedSalePrice = 1000m,
                IsActive = true
            };
            db.ProductBatches.Add(b);
            await db.SaveChangesAsync();
        }

        var saleWriter = _fixture.CreateSaleWriter();
        var saleInv = await saleWriter.CreateAndPostSaleInvoiceAsync("SINV-VD-1", new SaleInvoiceCreateDto
        {
            CustomerId = cust.Id,
            SaleType = SaleType.Credit,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 1m, UnitSalePrice = 1000m }
            }
        });

        var rv = await voucherService.CreateReceiptVoucherAsync(new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = arAcc!.Id,
            CustomerId = cust.Id,
            Amount = 1000m,
            Narration = "RV 1000"
        });

        var settlementRepo = _fixture.CreateSettlementRepository();

        // 1. Allocate 400m
        var alloc = await settlementRepo.AllocateReceiptAsync(new ReceiptAllocationCreateDto
        {
            ReceiptVoucherId = rv.Id,
            SaleInvoiceId = saleInv.Id,
            AllocatedAmount = 400m
        });

        var openInvoicesBeforeVoid = await settlementRepo.GetCustomerOpenInvoicesAsync(cust.Id);
        Assert.Single(openInvoicesBeforeVoid);
        Assert.Equal(600m, openInvoicesBeforeVoid[0].OutstandingBalance);
        Assert.Equal(400m, openInvoicesBeforeVoid[0].TotalAllocated);

        // 2. Void allocation with mandatory reason
        var voidResult = await settlementRepo.VoidReceiptAllocationAsync(alloc.Id, "Customer requested allocation reversal");
        Assert.Equal(AllocationStatus.Voided, voidResult.Status);
        Assert.NotNull(voidResult.VoidedAtUtc);
        Assert.Equal("Customer requested allocation reversal", voidResult.VoidReason);

        // 3. Verify record was NOT physically deleted in DB (audit preserved)
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var dbAlloc = await db.ReceiptVoucherAllocations.FindAsync(alloc.Id);
            Assert.NotNull(dbAlloc);
            Assert.Equal(AllocationStatus.Voided, dbAlloc.Status);
            Assert.Equal("Customer requested allocation reversal", dbAlloc.VoidReason);
            Assert.NotNull(dbAlloc.VoidedAtUtc);
        }

        // 4. Verify open invoice outstanding balance restored to 1000m
        var openInvoicesAfterVoid = await settlementRepo.GetCustomerOpenInvoicesAsync(cust.Id);
        Assert.Single(openInvoicesAfterVoid);
        Assert.Equal(1000m, openInvoicesAfterVoid[0].OutstandingBalance);
        Assert.Equal(0m, openInvoicesAfterVoid[0].TotalAllocated);

        // 5. Verify voucher unallocated balance restored: can now allocate full 1000m
        var newAlloc = await settlementRepo.AllocateReceiptAsync(new ReceiptAllocationCreateDto
        {
            ReceiptVoucherId = rv.Id,
            SaleInvoiceId = saleInv.Id,
            AllocatedAmount = 1000m
        });
        Assert.Equal(1000m, newAlloc.AllocatedAmount);
        Assert.Equal(AllocationStatus.Active, newAlloc.Status);
    }

    // 26. SETTLEMENT VOID LIFECYCLE: PAYMENT ALLOCATION VOID RESTORES BALANCES & PRESERVES AUDIT
    [Fact]
    public async Task PaymentAllocation_Void_RestoresInvoiceOutstandingAndVoucherRemaining_PreservesAuditTrail()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var apAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsPayableControl);

        var supRepo = _fixture.CreateSupplierRepository();
        var sup = await supRepo.AddAsync(new Supplier { Name = "Void Sup 1", SupplierCode = "SUP-VD-1", IsActive = true });

        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Void Purch Prod 2", ProductCode = "VPP-002", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;
        }

        var purchWriter = _fixture.CreatePurchaseWriter();
        var purchInv = await purchWriter.CreateAndPostPurchaseInvoiceAsync("PINV-VD-1", new PurchaseInvoiceCreateDto
        {
            SupplierId = sup.Id,
            SupplierInvoiceNumber = "SUP-VOID-1",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items = new List<PurchaseInvoiceItemCreateDto>
            {
                new()
                {
                    ProductId = productId,
                    BatchNumber = "B-VOID-1",
                    ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(2)),
                    Quantity = 10m,
                    PurchaseRate = 100m,
                    SuggestedSaleRate = 150m
                }
            }
        });

        var pv = await voucherService.CreatePaymentVoucherAsync(new PaymentVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = apAcc!.Id,
            SupplierId = sup.Id,
            Amount = 1000m,
            Narration = "PV 1000"
        });

        var settlementRepo = _fixture.CreateSettlementRepository();

        // 1. Allocate 400m
        var alloc = await settlementRepo.AllocatePaymentAsync(new PaymentAllocationCreateDto
        {
            PaymentVoucherId = pv.Id,
            PurchaseInvoiceId = purchInv.Id,
            AllocatedAmount = 400m
        });

        var openInvoicesBeforeVoid = await settlementRepo.GetSupplierOpenInvoicesAsync(sup.Id);
        Assert.Single(openInvoicesBeforeVoid);
        Assert.Equal(600m, openInvoicesBeforeVoid[0].OutstandingBalance);
        Assert.Equal(400m, openInvoicesBeforeVoid[0].TotalAllocated);

        // 2. Void allocation with mandatory reason
        var voidResult = await settlementRepo.VoidPaymentAllocationAsync(alloc.Id, "Wrong supplier invoice linked");
        Assert.Equal(AllocationStatus.Voided, voidResult.Status);
        Assert.NotNull(voidResult.VoidedAtUtc);
        Assert.Equal("Wrong supplier invoice linked", voidResult.VoidReason);

        // 3. Verify record was NOT physically deleted in DB
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var dbAlloc = await db.PaymentVoucherAllocations.FindAsync(alloc.Id);
            Assert.NotNull(dbAlloc);
            Assert.Equal(AllocationStatus.Voided, dbAlloc.Status);
            Assert.Equal("Wrong supplier invoice linked", dbAlloc.VoidReason);
            Assert.NotNull(dbAlloc.VoidedAtUtc);
        }

        // 4. Verify open invoice outstanding balance restored to 1000m
        var openInvoicesAfterVoid = await settlementRepo.GetSupplierOpenInvoicesAsync(sup.Id);
        Assert.Single(openInvoicesAfterVoid);
        Assert.Equal(1000m, openInvoicesAfterVoid[0].OutstandingBalance);
        Assert.Equal(0m, openInvoicesAfterVoid[0].TotalAllocated);

        // 5. Verify voucher balance restored: can allocate full 1000m
        var newAlloc = await settlementRepo.AllocatePaymentAsync(new PaymentAllocationCreateDto
        {
            PaymentVoucherId = pv.Id,
            PurchaseInvoiceId = purchInv.Id,
            AllocatedAmount = 1000m
        });
        Assert.Equal(1000m, newAlloc.AllocatedAmount);
        Assert.Equal(AllocationStatus.Active, newAlloc.Status);
    }

    // 27. SETTLEMENT VOID LIFECYCLE: EMPTY REASON REJECTED AND DOUBLE-VOID REJECTED
    [Fact]
    public async Task VoidAllocation_RejectsEmptyReason_And_RejectsDoubleVoid()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var arAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl);

        var custRepo = _fixture.CreateCustomerRepository();
        var cust = await custRepo.AddAsync(new Customer { Name = "Void Cust 2", CustomerCode = "CUST-VD-2", IsActive = true });

        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Void Prod 3", ProductCode = "VP-003", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;

            var b = new ProductBatch
            {
                ProductId = productId,
                BatchNumber = "B-VD-3",
                NormalizedBatchNumber = "B-VD-3",
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                QuantityOnHand = 200m,
                AveragePurchaseCost = 10m,
                InventoryValue = 2000m,
                SuggestedSalePrice = 500m,
                IsActive = true
            };
            db.ProductBatches.Add(b);
            await db.SaveChangesAsync();
        }

        var saleWriter = _fixture.CreateSaleWriter();
        var saleInv = await saleWriter.CreateAndPostSaleInvoiceAsync("SINV-VD-2", new SaleInvoiceCreateDto
        {
            CustomerId = cust.Id,
            SaleType = SaleType.Credit,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 1m, UnitSalePrice = 500m }
            }
        });

        var rv = await voucherService.CreateReceiptVoucherAsync(new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = arAcc!.Id,
            CustomerId = cust.Id,
            Amount = 500m,
            Narration = "RV 500"
        });

        var settlementRepo = _fixture.CreateSettlementRepository();
        var alloc = await settlementRepo.AllocateReceiptAsync(new ReceiptAllocationCreateDto
        {
            ReceiptVoucherId = rv.Id,
            SaleInvoiceId = saleInv.Id,
            AllocatedAmount = 500m
        });

        // Void with null or whitespace reason throws ValidationException
        await Assert.ThrowsAsync<ValidationException>(() =>
            settlementRepo.VoidReceiptAllocationAsync(alloc.Id, "   "));

        // Void with valid reason succeeds
        var voided = await settlementRepo.VoidReceiptAllocationAsync(alloc.Id, "Valid void reason");
        Assert.Equal(AllocationStatus.Voided, voided.Status);

        // Attempting to void again throws ValidationException (already voided)
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            settlementRepo.VoidReceiptAllocationAsync(alloc.Id, "Second attempt"));
        Assert.Contains("already voided", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // 28. SETTLEMENT VOID LIFECYCLE: VOID ALLOCATION GENERATES NO GL JOURNAL ENTRIES
    [Fact]
    public async Task VoidAllocation_GeneratesNoJournalEntries()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var arAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl);

        var custRepo = _fixture.CreateCustomerRepository();
        var cust = await custRepo.AddAsync(new Customer { Name = "Void Cust 3", CustomerCode = "CUST-VD-3", IsActive = true });

        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Void Prod 4", ProductCode = "VP-004", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;

            var b = new ProductBatch
            {
                ProductId = productId,
                BatchNumber = "B-VD-4",
                NormalizedBatchNumber = "B-VD-4",
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                QuantityOnHand = 200m,
                AveragePurchaseCost = 10m,
                InventoryValue = 2000m,
                SuggestedSalePrice = 500m,
                IsActive = true
            };
            db.ProductBatches.Add(b);
            await db.SaveChangesAsync();
        }

        var saleWriter = _fixture.CreateSaleWriter();
        var saleInv = await saleWriter.CreateAndPostSaleInvoiceAsync("SINV-VD-3", new SaleInvoiceCreateDto
        {
            CustomerId = cust.Id,
            SaleType = SaleType.Credit,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 1m, UnitSalePrice = 500m }
            }
        });

        var rv = await voucherService.CreateReceiptVoucherAsync(new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = arAcc!.Id,
            CustomerId = cust.Id,
            Amount = 500m,
            Narration = "RV 500"
        });

        var settlementRepo = _fixture.CreateSettlementRepository();
        var alloc = await settlementRepo.AllocateReceiptAsync(new ReceiptAllocationCreateDto
        {
            ReceiptVoucherId = rv.Id,
            SaleInvoiceId = saleInv.Id,
            AllocatedAmount = 500m
        });

        int journalCountBefore;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            journalCountBefore = await db.JournalEntries.CountAsync();
        }

        // Void the allocation
        await settlementRepo.VoidReceiptAllocationAsync(alloc.Id, "Audit void test");

        int journalCountAfter;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            journalCountAfter = await db.JournalEntries.CountAsync();
        }

        // Absolutely NO GL journal created for allocation void
        Assert.Equal(journalCountBefore, journalCountAfter);
    }

    // 29. SETTLEMENT VOID LIFECYCLE: SALE INVOICE AND VOUCHER CANCELLATION UNBLOCKED AFTER VOID
    [Fact]
    public async Task SaleInvoiceAndReceiptVoucher_Cancellation_BlockedByActiveAllocations_AllowedAfterVoid()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var arAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsReceivableControl);

        var custRepo = _fixture.CreateCustomerRepository();
        var cust = await custRepo.AddAsync(new Customer { Name = "Void Cust 4", CustomerCode = "CUST-VD-4", IsActive = true });

        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Void Prod 5", ProductCode = "VP-005", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;

            var b = new ProductBatch
            {
                ProductId = productId,
                BatchNumber = "B-VD-5",
                NormalizedBatchNumber = "B-VD-5",
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                QuantityOnHand = 200m,
                AveragePurchaseCost = 10m,
                InventoryValue = 2000m,
                SuggestedSalePrice = 500m,
                IsActive = true
            };
            db.ProductBatches.Add(b);
            await db.SaveChangesAsync();
        }

        var saleWriter = _fixture.CreateSaleWriter();
        var saleInv = await saleWriter.CreateAndPostSaleInvoiceAsync("SINV-VD-4", new SaleInvoiceCreateDto
        {
            CustomerId = cust.Id,
            SaleType = SaleType.Credit,
            Items = new List<SaleInvoiceItemCreateDto>
            {
                new() { ProductId = productId, Quantity = 1m, UnitSalePrice = 500m }
            }
        });

        var rv = await voucherService.CreateReceiptVoucherAsync(new ReceiptVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = arAcc!.Id,
            CustomerId = cust.Id,
            Amount = 500m,
            Narration = "RV 500"
        });

        var settlementRepo = _fixture.CreateSettlementRepository();
        var alloc = await settlementRepo.AllocateReceiptAsync(new ReceiptAllocationCreateDto
        {
            ReceiptVoucherId = rv.Id,
            SaleInvoiceId = saleInv.Id,
            AllocatedAmount = 500m
        });

        // 1. Try cancelling RV while active allocation exists -> blocked
        var rvEx = await Assert.ThrowsAsync<ValidationException>(() =>
            voucherService.CancelReceiptVoucherAsync(rv.Id, "Cancel RV test"));
        Assert.Contains("active", rvEx.Message, StringComparison.OrdinalIgnoreCase);

        // 2. Try cancelling SaleInvoice while active allocation exists -> blocked
        var saleEx = await Assert.ThrowsAsync<ValidationException>(() =>
            saleWriter.CancelSaleInvoiceAsync(saleInv.Id, "Cancel Sale test"));
        Assert.Contains("active", saleEx.Message, StringComparison.OrdinalIgnoreCase);

        // 3. Void the allocation
        await settlementRepo.VoidReceiptAllocationAsync(alloc.Id, "Authorized void for cancellation");

        // 4. Now cancel RV -> succeeds cleanly!
        var cancelledRv = await voucherService.CancelReceiptVoucherAsync(rv.Id, "Cancel RV now allowed");
        Assert.Equal(VoucherStatus.Cancelled, cancelledRv.Status);

        // 5. Now cancel SaleInvoice -> succeeds cleanly!
        var cancelledSale = await saleWriter.CancelSaleInvoiceAsync(saleInv.Id, "Cancel Sale now allowed");
        Assert.Equal(SaleInvoiceStatus.Cancelled, cancelledSale.Status);
    }

    // 30. SETTLEMENT VOID LIFECYCLE: PURCHASE INVOICE AND PAYMENT VOUCHER CANCELLATION UNBLOCKED AFTER VOID
    [Fact]
    public async Task PurchaseInvoiceAndPaymentVoucher_Cancellation_BlockedByActiveAllocations_AllowedAfterVoid()
    {
        var (accountService, _, voucherService, _, _, _, setupService) = GetServices();
        await setupService.InitializeDefaultChartOfAccountsAsync();

        var cashAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.CashOnHand);
        var apAcc = await accountService.GetBySystemTypeAsync(SystemAccountType.AccountsPayableControl);

        var supRepo = _fixture.CreateSupplierRepository();
        var sup = await supRepo.AddAsync(new Supplier { Name = "Void Sup 2", SupplierCode = "SUP-VD-2", IsActive = true });

        int productId;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var p = new Product { Name = "Void Purch Prod 6", ProductCode = "VPP-006", IsActive = true };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            productId = p.Id;
        }

        var purchWriter = _fixture.CreatePurchaseWriter();
        var purchInv = await purchWriter.CreateAndPostPurchaseInvoiceAsync("PINV-VD-2", new PurchaseInvoiceCreateDto
        {
            SupplierId = sup.Id,
            SupplierInvoiceNumber = "SUP-VOID-2",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            Items = new List<PurchaseInvoiceItemCreateDto>
            {
                new()
                {
                    ProductId = productId,
                    BatchNumber = "B-VOID-2",
                    ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(2)),
                    Quantity = 5m,
                    PurchaseRate = 100m,
                    SuggestedSaleRate = 150m
                }
            }
        });

        var pv = await voucherService.CreatePaymentVoucherAsync(new PaymentVoucherCreateDto
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            CashOrBankAccountId = cashAcc!.Id,
            OffsetAccountId = apAcc!.Id,
            SupplierId = sup.Id,
            Amount = 500m,
            Narration = "PV 500"
        });

        var settlementRepo = _fixture.CreateSettlementRepository();
        var alloc = await settlementRepo.AllocatePaymentAsync(new PaymentAllocationCreateDto
        {
            PaymentVoucherId = pv.Id,
            PurchaseInvoiceId = purchInv.Id,
            AllocatedAmount = 500m
        });

        // 1. Try cancelling PV while active allocation exists -> blocked
        var pvEx = await Assert.ThrowsAsync<ValidationException>(() =>
            voucherService.CancelPaymentVoucherAsync(pv.Id, "Cancel PV test"));
        Assert.Contains("active", pvEx.Message, StringComparison.OrdinalIgnoreCase);

        // 2. Try cancelling PurchaseInvoice while active allocation exists -> blocked
        var purchEx = await Assert.ThrowsAsync<ValidationException>(() =>
            purchWriter.CancelPurchaseInvoiceAsync(purchInv.Id, "Cancel Purchase test"));
        Assert.Contains("active", purchEx.Message, StringComparison.OrdinalIgnoreCase);

        // 3. Void the allocation
        await settlementRepo.VoidPaymentAllocationAsync(alloc.Id, "Authorized void for cancellation");

        // 4. Now cancel PV -> succeeds cleanly!
        var cancelledPv = await voucherService.CancelPaymentVoucherAsync(pv.Id, "Cancel PV now allowed");
        Assert.Equal(VoucherStatus.Cancelled, cancelledPv.Status);

        // 5. Now cancel PurchaseInvoice -> succeeds cleanly!
        await purchWriter.CancelPurchaseInvoiceAsync(purchInv.Id, "Cancel Purchase now allowed");
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var pInv = await db.PurchaseInvoices.FindAsync(purchInv.Id);
            Assert.NotNull(pInv);
            Assert.Equal(PurchaseInvoiceStatus.Cancelled, pInv.Status);
        }
    }

}

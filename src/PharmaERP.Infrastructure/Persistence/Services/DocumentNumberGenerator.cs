using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaERP.Application.Common.Interfaces;
using PharmaERP.Infrastructure.Persistence.Entities;

namespace PharmaERP.Infrastructure.Persistence.Services;

public class DocumentNumberGenerator : IDocumentNumberGenerator
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<DocumentNumberGenerator> _logger;

    public DocumentNumberGenerator(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<DocumentNumberGenerator> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<string> NextDocumentNumberAsync(DocumentType type, int? year = null, CancellationToken cancellationToken = default)
    {
        int effectiveYear = year ?? DateTime.UtcNow.Year;
        string prefix;
        string sequenceKey;
        bool includeYear;

        switch (type)
        {
            case DocumentType.PurchaseInvoice:
                prefix = "PINV";
                sequenceKey = $"PurchaseInvoice_{effectiveYear}";
                includeYear = true;
                break;

            case DocumentType.PurchaseReturn:
                prefix = "PRET";
                sequenceKey = $"PurchaseReturn_{effectiveYear}";
                includeYear = true;
                break;

            case DocumentType.Customer:
                prefix = "CUST";
                sequenceKey = "Customer";
                includeYear = false;
                break;

            case DocumentType.Supplier:
                prefix = "SUPP";
                sequenceKey = "Supplier";
                includeYear = false;
                break;

            case DocumentType.SaleInvoice:
                prefix = "SINV";
                sequenceKey = $"SaleInvoice_{effectiveYear}";
                includeYear = true;
                break;

            case DocumentType.SaleReturn:
                prefix = "SRET";
                sequenceKey = $"SaleReturn_{effectiveYear}";
                includeYear = true;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported document type.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            // Acquire application-level exclusive lock on the sequence key to guarantee serial increment without deadlocks
            var resourceNameParam = new SqlParameter("@Resource", $"DocSeq_{sequenceKey}");
            var lockModeParam = new SqlParameter("@LockMode", "Exclusive");
            var lockOwnerParam = new SqlParameter("@LockOwner", "Transaction");
            var lockTimeoutParam = new SqlParameter("@LockTimeout", 5000); // 5 seconds
            var resultParam = new SqlParameter
            {
                ParameterName = "@Result",
                SqlDbType = SqlDbType.Int,
                Direction = ParameterDirection.Output
            };

            await context.Database.ExecuteSqlRawAsync(
                "EXEC @Result = sp_getapplock @Resource = @Resource, @LockMode = @LockMode, @LockOwner = @LockOwner, @LockTimeout = @LockTimeout",
                [resultParam, resourceNameParam, lockModeParam, lockOwnerParam, lockTimeoutParam],
                cancellationToken);

            int lockResult = (int)(resultParam.Value ?? -99);
            if (lockResult < 0)
            {
                throw new InvalidOperationException($"Failed to acquire document sequence lock for '{sequenceKey}'. Return code: {lockResult}");
            }

            var sequence = await context.DocumentSequences
                .FirstOrDefaultAsync(s => s.SequenceKey == sequenceKey, cancellationToken);

            if (sequence == null)
            {
                sequence = new DocumentSequence
                {
                    SequenceKey = sequenceKey,
                    LastNumber = 1
                };
                await context.DocumentSequences.AddAsync(sequence, cancellationToken);
            }
            else
            {
                sequence.LastNumber++;
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            long nextVal = sequence.LastNumber;
            return includeYear
                ? $"{prefix}-{effectiveYear}-{nextVal:D6}"
                : $"{prefix}-{nextVal:D5}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to allocate next document sequence number for key '{SequenceKey}'.", sequenceKey);
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}


using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingAndLedgerCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    AccountType = table.Column<int>(type: "int", nullable: false),
                    ParentAccountId = table.Column<int>(type: "int", nullable: true),
                    AllowPosting = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsControlAccount = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsCashAccount = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsBankAccount = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    SystemAccountType = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Accounts_Accounts_ParentAccountId",
                        column: x => x.ParentAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntryNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Narration = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SourceDocumentType = table.Column<int>(type: "int", nullable: false),
                    SourceDocumentId = table.Column<int>(type: "int", nullable: true),
                    SourceDocumentNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PostingRole = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReversesJournalEntryId = table.Column<int>(type: "int", nullable: true),
                    TotalDebit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCredit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalEntries_JournalEntries_ReversesJournalEntryId",
                        column: x => x.ReversesJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "JournalVouchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VoucherNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Narration = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalVouchers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpeningBalanceVouchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VoucherNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Narration = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalanceVouchers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentVouchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VoucherNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CashOrBankAccountId = table.Column<int>(type: "int", nullable: false),
                    OffsetAccountId = table.Column<int>(type: "int", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Narration = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentVouchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentVouchers_Accounts_CashOrBankAccountId",
                        column: x => x.CashOrBankAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentVouchers_Accounts_OffsetAccountId",
                        column: x => x.OffsetAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentVouchers_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptVouchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VoucherNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CashOrBankAccountId = table.Column<int>(type: "int", nullable: false),
                    OffsetAccountId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Narration = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptVouchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiptVouchers_Accounts_CashOrBankAccountId",
                        column: x => x.CashOrBankAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptVouchers_Accounts_OffsetAccountId",
                        column: x => x.OffsetAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptVouchers_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JournalEntryId = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    DebitAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreditAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    Narration = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryLines", x => x.Id);
                    table.CheckConstraint("CK_JournalEntryLine_Amounts", "([DebitAmount] > 0 AND [CreditAmount] = 0) OR ([CreditAmount] > 0 AND [DebitAmount] = 0)");
                    table.CheckConstraint("CK_JournalEntryLine_PartyMutex", "[CustomerId] IS NULL OR [SupplierId] IS NULL");
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_AccountCode",
                table: "Accounts",
                column: "AccountCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ParentAccountId",
                table: "Accounts",
                column: "ParentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_SystemAccountType",
                table: "Accounts",
                column: "SystemAccountType",
                unique: true,
                filter: "[SystemAccountType] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_BusinessDate_PostedAtUtc_Id",
                table: "JournalEntries",
                columns: new[] { "BusinessDate", "PostedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_EntryNumber",
                table: "JournalEntries",
                column: "EntryNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_OperationId",
                table: "JournalEntries",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_ReversesJournalEntryId",
                table: "JournalEntries",
                column: "ReversesJournalEntryId",
                unique: true,
                filter: "[ReversesJournalEntryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_SourceDocumentType_SourceDocumentId_PostingRole",
                table: "JournalEntries",
                columns: new[] { "SourceDocumentType", "SourceDocumentId", "PostingRole" },
                unique: true,
                filter: "[SourceDocumentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_AccountId_JournalEntryId",
                table: "JournalEntryLines",
                columns: new[] { "AccountId", "JournalEntryId" })
                .Annotation("SqlServer:Include", new[] { "DebitAmount", "CreditAmount" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_CustomerId_AccountId_JournalEntryId",
                table: "JournalEntryLines",
                columns: new[] { "CustomerId", "AccountId", "JournalEntryId" },
                filter: "[CustomerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_JournalEntryId",
                table: "JournalEntryLines",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_SupplierId_AccountId_JournalEntryId",
                table: "JournalEntryLines",
                columns: new[] { "SupplierId", "AccountId", "JournalEntryId" },
                filter: "[SupplierId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JournalVouchers_BusinessDate_Id",
                table: "JournalVouchers",
                columns: new[] { "BusinessDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalVouchers_OperationId",
                table: "JournalVouchers",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalVouchers_VoucherNumber",
                table: "JournalVouchers",
                column: "VoucherNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceVouchers_BusinessDate_Id",
                table: "OpeningBalanceVouchers",
                columns: new[] { "BusinessDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceVouchers_OperationId",
                table: "OpeningBalanceVouchers",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceVouchers_VoucherNumber",
                table: "OpeningBalanceVouchers",
                column: "VoucherNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentVouchers_BusinessDate_Id",
                table: "PaymentVouchers",
                columns: new[] { "BusinessDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentVouchers_CashOrBankAccountId",
                table: "PaymentVouchers",
                column: "CashOrBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentVouchers_OffsetAccountId",
                table: "PaymentVouchers",
                column: "OffsetAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentVouchers_OperationId",
                table: "PaymentVouchers",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentVouchers_SupplierId",
                table: "PaymentVouchers",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentVouchers_VoucherNumber",
                table: "PaymentVouchers",
                column: "VoucherNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVouchers_BusinessDate_Id",
                table: "ReceiptVouchers",
                columns: new[] { "BusinessDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVouchers_CashOrBankAccountId",
                table: "ReceiptVouchers",
                column: "CashOrBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVouchers_CustomerId",
                table: "ReceiptVouchers",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVouchers_OffsetAccountId",
                table: "ReceiptVouchers",
                column: "OffsetAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVouchers_OperationId",
                table: "ReceiptVouchers",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVouchers_VoucherNumber",
                table: "ReceiptVouchers",
                column: "VoucherNumber",
                unique: true);

            // 1. Trigger: Enforce Balance and Account Rules on Post
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_JournalEntries_EnforceBalanceOnPost
ON JournalEntries
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1 
        FROM INSERTED i
        LEFT JOIN DELETED d ON i.Id = d.Id
        WHERE i.Status = 2 AND (d.Status IS NULL OR d.Status <> 2)
    )
    BEGIN
        -- Check 1: Minimum 2 lines
        IF EXISTS (
            SELECT i.Id
            FROM INSERTED i
            LEFT JOIN DELETED d ON i.Id = d.Id
            WHERE i.Status = 2 AND (d.Status IS NULL OR d.Status <> 2)
              AND (SELECT COUNT(*) FROM JournalEntryLines l WHERE l.JournalEntryId = i.Id) < 2
        )
        BEGIN
            RAISERROR('Posted JournalEntry must have at least 2 lines.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END;

        -- Check 2: Lines must balance
        IF EXISTS (
            SELECT i.Id
            FROM INSERTED i
            LEFT JOIN DELETED d ON i.Id = d.Id
            WHERE i.Status = 2 AND (d.Status IS NULL OR d.Status <> 2)
              AND (SELECT ISNULL(SUM(l.DebitAmount), 0) FROM JournalEntryLines l WHERE l.JournalEntryId = i.Id)
                  <> (SELECT ISNULL(SUM(l.CreditAmount), 0) FROM JournalEntryLines l WHERE l.JournalEntryId = i.Id)
        )
        BEGIN
            RAISERROR('Posted JournalEntry lines must balance (TotalDebit == TotalCredit).', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END;

        -- Check 3: Header totals must match line sums
        IF EXISTS (
            SELECT i.Id
            FROM INSERTED i
            LEFT JOIN DELETED d ON i.Id = d.Id
            WHERE i.Status = 2 AND (d.Status IS NULL OR d.Status <> 2)
              AND (
                  i.TotalDebit <> (SELECT ISNULL(SUM(l.DebitAmount), 0) FROM JournalEntryLines l WHERE l.JournalEntryId = i.Id)
                  OR i.TotalCredit <> (SELECT ISNULL(SUM(l.CreditAmount), 0) FROM JournalEntryLines l WHERE l.JournalEntryId = i.Id)
              )
        )
        BEGIN
            RAISERROR('JournalEntry header totals must match sum of line amounts.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END;

        -- Check 4: Active and postable accounts only
        IF EXISTS (
            SELECT 1
            FROM INSERTED i
            JOIN JournalEntryLines l ON l.JournalEntryId = i.Id
            JOIN Accounts a ON a.Id = l.AccountId
            LEFT JOIN DELETED d ON i.Id = d.Id
            WHERE i.Status = 2 AND (d.Status IS NULL OR d.Status <> 2)
              AND (a.IsActive = 0 OR a.AllowPosting = 0)
        )
        BEGIN
            RAISERROR('Journal lines can only post to active and postable accounts.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END;

        -- Check 5: AR control account lines must specify CustomerId (SystemAccountType = 2)
        IF EXISTS (
            SELECT 1
            FROM INSERTED i
            JOIN JournalEntryLines l ON l.JournalEntryId = i.Id
            JOIN Accounts a ON a.Id = l.AccountId
            LEFT JOIN DELETED d ON i.Id = d.Id
            WHERE i.Status = 2 AND (d.Status IS NULL OR d.Status <> 2)
              AND a.SystemAccountType = 2
              AND (l.CustomerId IS NULL OR l.SupplierId IS NOT NULL)
        )
        BEGIN
            RAISERROR('AccountsReceivableControl lines require a CustomerId and cannot have a SupplierId.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END;

        -- Check 6: AP control account lines must specify SupplierId (SystemAccountType = 3)
        IF EXISTS (
            SELECT 1
            FROM INSERTED i
            JOIN JournalEntryLines l ON l.JournalEntryId = i.Id
            JOIN Accounts a ON a.Id = l.AccountId
            LEFT JOIN DELETED d ON i.Id = d.Id
            WHERE i.Status = 2 AND (d.Status IS NULL OR d.Status <> 2)
              AND a.SystemAccountType = 3
              AND (l.SupplierId IS NULL OR l.CustomerId IS NOT NULL)
        )
        BEGIN
            RAISERROR('AccountsPayableControl lines require a SupplierId and cannot have a CustomerId.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END;

        -- Check 7: Manual journals cannot post directly to AR (2), AP (3), Inventory (4), COGS (8)
        IF EXISTS (
            SELECT 1
            FROM INSERTED i
            JOIN JournalEntryLines l ON l.JournalEntryId = i.Id
            JOIN Accounts a ON a.Id = l.AccountId
            LEFT JOIN DELETED d ON i.Id = d.Id
            WHERE i.Status = 2 AND (d.Status IS NULL OR d.Status <> 2)
              AND i.SourceDocumentType = 1
              AND a.SystemAccountType IN (2, 3, 4, 8)
        )
        BEGIN
            RAISERROR('Manual journals cannot post directly to AR, AP, Inventory, or COGS control accounts.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END;
    END;
END;
");

            // 2. Trigger: Prevent Posted Journal Header Modifications / Deletions
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_JournalEntries_PreventPostedHeaderModifications
ON JournalEntries
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    -- Prevent DELETE of posted entries
    IF EXISTS (SELECT 1 FROM DELETED WHERE Status = 2)
    BEGIN
        RAISERROR('Posted journal entries cannot be deleted.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END;

    -- Prevent modifying status from Posted back to Draft
    IF EXISTS (
        SELECT 1 
        FROM DELETED d
        JOIN INSERTED i ON d.Id = i.Id
        WHERE d.Status = 2 AND i.Status <> 2
    )
    BEGIN
        RAISERROR('Posted journal entry status cannot be changed from Posted.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END;

    -- Prevent modifying posted financial/identity attributes
    IF EXISTS (
        SELECT 1 
        FROM DELETED d
        JOIN INSERTED i ON d.Id = i.Id
        WHERE d.Status = 2 
          AND (
              d.TotalDebit <> i.TotalDebit
              OR d.TotalCredit <> i.TotalCredit
              OR d.BusinessDate <> i.BusinessDate
              OR d.EntryNumber <> i.EntryNumber
              OR d.PostingRole <> i.PostingRole
              OR d.SourceDocumentType <> i.SourceDocumentType
              OR ISNULL(d.SourceDocumentId, -1) <> ISNULL(i.SourceDocumentId, -1)
              OR ISNULL(d.SourceDocumentNumber, '') <> ISNULL(i.SourceDocumentNumber, '')
          )
    )
    BEGIN
        RAISERROR('Posted journal entries are immutable and cannot be modified.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END;
END;
");

            // 3. Trigger: Prevent Inserting, Updating, or Deleting lines of Posted Journals
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_JournalEntryLines_PreventPostedModifications
ON JournalEntryLines
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    -- Block INSERT into posted journal
    IF EXISTS (
        SELECT 1 
        FROM INSERTED i
        JOIN JournalEntries je ON je.Id = i.JournalEntryId
        WHERE je.Status = 2
    ) AND NOT EXISTS (SELECT 1 FROM DELETED)
    BEGIN
        RAISERROR('Cannot insert lines into an already posted journal entry.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END;

    -- Block DELETE of lines belonging to posted journal
    IF EXISTS (
        SELECT 1 
        FROM DELETED d
        JOIN JournalEntries je ON je.Id = d.JournalEntryId
        WHERE je.Status = 2
    ) AND NOT EXISTS (SELECT 1 FROM INSERTED)
    BEGIN
        RAISERROR('Cannot delete lines from an already posted journal entry.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END;

    -- Block UPDATE of lines belonging to posted journal
    IF EXISTS (SELECT 1 FROM INSERTED) AND EXISTS (SELECT 1 FROM DELETED)
    BEGIN
        IF EXISTS (
            SELECT 1 
            FROM DELETED d
            JOIN JournalEntries je ON je.Id = d.JournalEntryId
            WHERE je.Status = 2
        ) OR EXISTS (
            SELECT 1 
            FROM INSERTED i
            JOIN JournalEntries je ON je.Id = i.JournalEntryId
            WHERE je.Status = 2
        )
        BEGIN
            RAISERROR('Cannot modify lines of an already posted journal entry.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END;
    END;
END;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_JournalEntries_EnforceBalanceOnPost;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_JournalEntries_PreventPostedHeaderModifications;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_JournalEntryLines_PreventPostedModifications;");

            migrationBuilder.DropTable(
                name: "JournalEntryLines");

            migrationBuilder.DropTable(
                name: "JournalVouchers");

            migrationBuilder.DropTable(
                name: "OpeningBalanceVouchers");

            migrationBuilder.DropTable(
                name: "PaymentVouchers");

            migrationBuilder.DropTable(
                name: "ReceiptVouchers");

            migrationBuilder.DropTable(
                name: "JournalEntries");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}

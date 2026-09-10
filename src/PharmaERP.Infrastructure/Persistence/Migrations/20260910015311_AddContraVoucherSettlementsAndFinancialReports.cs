using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContraVoucherSettlementsAndFinancialReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContraVouchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VoucherNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceAccountId = table.Column<int>(type: "int", nullable: false),
                    DestinationAccountId = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_ContraVouchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContraVouchers_Accounts_DestinationAccountId",
                        column: x => x.DestinationAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContraVouchers_Accounts_SourceAccountId",
                        column: x => x.SourceAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentVoucherAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentVoucherId = table.Column<int>(type: "int", nullable: false),
                    PurchaseInvoiceId = table.Column<int>(type: "int", nullable: false),
                    AllocatedAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AllocatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentVoucherAllocations", x => x.Id);
                    table.CheckConstraint("CK_PaymentVoucherAllocations_Amount_Positive", "[AllocatedAmount] > 0");
                    table.ForeignKey(
                        name: "FK_PaymentVoucherAllocations_PaymentVouchers_PaymentVoucherId",
                        column: x => x.PaymentVoucherId,
                        principalTable: "PaymentVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentVoucherAllocations_PurchaseInvoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptVoucherAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceiptVoucherId = table.Column<int>(type: "int", nullable: false),
                    SaleInvoiceId = table.Column<int>(type: "int", nullable: false),
                    AllocatedAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AllocatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptVoucherAllocations", x => x.Id);
                    table.CheckConstraint("CK_ReceiptVoucherAllocations_Amount_Positive", "[AllocatedAmount] > 0");
                    table.ForeignKey(
                        name: "FK_ReceiptVoucherAllocations_ReceiptVouchers_ReceiptVoucherId",
                        column: x => x.ReceiptVoucherId,
                        principalTable: "ReceiptVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptVoucherAllocations_SaleInvoices_SaleInvoiceId",
                        column: x => x.SaleInvoiceId,
                        principalTable: "SaleInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContraVouchers_BusinessDate_Id",
                table: "ContraVouchers",
                columns: new[] { "BusinessDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ContraVouchers_DestinationAccountId",
                table: "ContraVouchers",
                column: "DestinationAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ContraVouchers_OperationId",
                table: "ContraVouchers",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContraVouchers_SourceAccountId",
                table: "ContraVouchers",
                column: "SourceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ContraVouchers_VoucherNumber",
                table: "ContraVouchers",
                column: "VoucherNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentVoucherAllocations_PaymentVoucherId_PurchaseInvoiceId",
                table: "PaymentVoucherAllocations",
                columns: new[] { "PaymentVoucherId", "PurchaseInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentVoucherAllocations_PurchaseInvoiceId",
                table: "PaymentVoucherAllocations",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVoucherAllocations_ReceiptVoucherId_SaleInvoiceId",
                table: "ReceiptVoucherAllocations",
                columns: new[] { "ReceiptVoucherId", "SaleInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVoucherAllocations_SaleInvoiceId",
                table: "ReceiptVoucherAllocations",
                column: "SaleInvoiceId");

            // Update Trigger: Prevent Posted Journal Header Modifications including strict Narration immutability
            migrationBuilder.Sql(@"
CREATE OR ALTER TRIGGER TR_JournalEntries_PreventPostedHeaderModifications
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

    -- Prevent modifying posted financial/identity attributes including Narration and OperationId
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
              OR (d.Narration <> i.Narration OR (d.Narration IS NULL AND i.Narration IS NOT NULL) OR (d.Narration IS NOT NULL AND i.Narration IS NULL))
              OR d.OperationId <> i.OperationId
          )
    )
    BEGIN
        RAISERROR('Posted journal entries are immutable and cannot be modified.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END;
END;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert Trigger to previous definition without Narration check
            migrationBuilder.Sql(@"
CREATE OR ALTER TRIGGER TR_JournalEntries_PreventPostedHeaderModifications
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

            migrationBuilder.DropTable(
                name: "ContraVouchers");

            migrationBuilder.DropTable(
                name: "PaymentVoucherAllocations");

            migrationBuilder.DropTable(
                name: "ReceiptVoucherAllocations");
        }
    }
}

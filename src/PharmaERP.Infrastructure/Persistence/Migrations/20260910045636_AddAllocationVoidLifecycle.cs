using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAllocationVoidLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "ReceiptVoucherAllocations",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "ReceiptVoucherAllocations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAtUtc",
                table: "ReceiptVoucherAllocations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "PaymentVoucherAllocations",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "PaymentVoucherAllocations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAtUtc",
                table: "PaymentVoucherAllocations",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "ReceiptVoucherAllocations");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "ReceiptVoucherAllocations");

            migrationBuilder.DropColumn(
                name: "VoidedAtUtc",
                table: "ReceiptVoucherAllocations");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "PaymentVoucherAllocations");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "PaymentVoucherAllocations");

            migrationBuilder.DropColumn(
                name: "VoidedAtUtc",
                table: "PaymentVoucherAllocations");
        }
    }
}

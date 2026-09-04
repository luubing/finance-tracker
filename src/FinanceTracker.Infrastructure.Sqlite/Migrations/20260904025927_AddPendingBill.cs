using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceTracker.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingBill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingBills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TransactionTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingBills", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingBills_Source_TransactionTime",
                table: "PendingBills",
                columns: new[] { "Source", "TransactionTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingBills");
        }
    }
}

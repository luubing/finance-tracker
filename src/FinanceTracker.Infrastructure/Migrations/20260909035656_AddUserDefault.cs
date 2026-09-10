using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "PaymentChannels");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "Categories");

            migrationBuilder.CreateTable(
                name: "UserDefaults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefaultExpenseCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultIncomeCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultPaymentChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultLedgerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDefaults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserDefaults_Categories_DefaultExpenseCategoryId",
                        column: x => x.DefaultExpenseCategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserDefaults_Categories_DefaultIncomeCategoryId",
                        column: x => x.DefaultIncomeCategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserDefaults_Ledgers_DefaultLedgerId",
                        column: x => x.DefaultLedgerId,
                        principalTable: "Ledgers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserDefaults_PaymentChannels_DefaultPaymentChannelId",
                        column: x => x.DefaultPaymentChannelId,
                        principalTable: "PaymentChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserDefaults_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserDefaults_DefaultExpenseCategoryId",
                table: "UserDefaults",
                column: "DefaultExpenseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDefaults_DefaultIncomeCategoryId",
                table: "UserDefaults",
                column: "DefaultIncomeCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDefaults_DefaultLedgerId",
                table: "UserDefaults",
                column: "DefaultLedgerId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDefaults_DefaultPaymentChannelId",
                table: "UserDefaults",
                column: "DefaultPaymentChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDefaults_UserId",
                table: "UserDefaults",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserDefaults");

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "PaymentChannels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "Ledgers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "Categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVChargeSwap.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCounterCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CollectedByOperatorId",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeferredByOperatorId",
                table: "invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DueAtUtc",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_CollectedByOperatorId",
                table: "payments",
                column: "CollectedByOperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_CustomerId_DueAtUtc",
                table: "invoices",
                columns: new[] { "CustomerId", "DueAtUtc" },
                filter: " \"Status\" = 'Pending' AND \"DueAtUtc\" IS NOT NULL ");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_DeferredByOperatorId",
                table: "invoices",
                column: "DeferredByOperatorId");

            migrationBuilder.AddForeignKey(
                name: "FK_invoices_users_DeferredByOperatorId",
                table: "invoices",
                column: "DeferredByOperatorId",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payments_users_CollectedByOperatorId",
                table: "payments",
                column: "CollectedByOperatorId",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Recover completed services that predate synchronous invoicing.
            // Keep existing invoices and use midpoint-to-even rounding, as in checkout.
            migrationBuilder.Sql("""
                WITH usage AS (
                    SELECT session."ReservationId", reservation."CustomerId", session."CompletedAtUtc",
                           session."EndMeterKwh" - session."StartMeterKwh" AS kwh,
                           COALESCE(session."AppliedKwhRate", 3858) AS rate
                    FROM charging_sessions session
                    JOIN reservations reservation ON reservation."Id" = session."ReservationId"
                    WHERE session."CompletedAtUtc" IS NOT NULL
                      AND NOT EXISTS (SELECT 1 FROM invoices WHERE "ReservationId" = session."ReservationId")
                ), amounts AS (
                    SELECT *, kwh * rate * 100 AS cents FROM usage
                ), rounded AS (
                    SELECT *, CASE WHEN cents - trunc(cents) = 0.5 AND mod(trunc(cents), 2) = 0
                        THEN trunc(cents) / 100 ELSE round(cents) / 100 END AS amount
                    FROM amounts
                )
                INSERT INTO invoices ("Id", "ReservationId", "CustomerId", "TotalKwh", "KwhRate",
                                      "EnergyAmount", "TotalAmount", "Status", "CreatedAtUtc")
                SELECT gen_random_uuid(), "ReservationId", "CustomerId", kwh, rate,
                       amount, amount, 'Pending', "CompletedAtUtc"
                FROM rounded
                ON CONFLICT ("ReservationId") DO NOTHING;

                INSERT INTO invoices ("Id", "ReservationId", "CustomerId", "TotalKwh", "KwhRate",
                                      "EnergyAmount", "SwapRate", "TotalAmount", "Status", "CreatedAtUtc")
                SELECT gen_random_uuid(), swap."ReservationId", reservation."CustomerId", 0, 0,
                       0, swap."AppliedRate", swap."AppliedRate", 'Pending', swap."CompletedAtUtc"
                FROM battery_swaps swap
                JOIN reservations reservation ON reservation."Id" = swap."ReservationId"
                WHERE NOT EXISTS (SELECT 1 FROM invoices WHERE "ReservationId" = swap."ReservationId")
                ON CONFLICT ("ReservationId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_invoices_users_DeferredByOperatorId",
                table: "invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_payments_users_CollectedByOperatorId",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_CollectedByOperatorId",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_invoices_CustomerId_DueAtUtc",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "IX_invoices_DeferredByOperatorId",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "CollectedByOperatorId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "DeferredByOperatorId",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "DueAtUtc",
                table: "invoices");
        }
    }
}

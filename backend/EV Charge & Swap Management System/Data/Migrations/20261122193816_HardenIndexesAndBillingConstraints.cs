using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVChargeSwap.Data.Migrations
{
    /// <inheritdoc />
    public partial class HardenIndexesAndBillingConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "tariffs",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldPrecision: 8,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "SwapRate",
                table: "invoices",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldPrecision: 8,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "AppliedKwhRate",
                table: "charging_sessions",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldPrecision: 8,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "AppliedRate",
                table: "battery_swaps",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldPrecision: 8,
                oldScale: 2);

            migrationBuilder.CreateIndex(
                name: "ix_reservations_active_location",
                table: "reservations",
                column: "LocationId",
                filter: " \"Status\" IN ('Held', 'CheckedIn') ");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_BatteryId",
                table: "reservations",
                column: "BatteryId");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_ChargingBayId",
                table: "reservations",
                column: "ChargingBayId");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_CustomerId_CreatedAtUtc",
                table: "reservations",
                columns: new[] { "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_InvoiceId",
                table: "payments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending_retry",
                table: "outbox_messages",
                columns: new[] { "RetryCount", "CreatedAtUtc" },
                filter: "\"ProcessedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId_CreatedAtUtc_Id",
                table: "notifications",
                columns: new[] { "UserId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_locations_is_active_latitude_longitude",
                table: "locations",
                columns: new[] { "is_active", "latitude", "longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_locations_is_active_operating_status_name_id",
                table: "locations",
                columns: new[] { "is_active", "operating_status", "name", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_pending_customer",
                table: "invoices",
                column: "CustomerId",
                filter: " \"Status\" = 'Pending' ");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_deferral",
                table: "invoices",
                sql: " (\"DueAtUtc\" IS NULL) = (\"DeferredByOperatorId\" IS NULL) ");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_non_negative",
                table: "invoices",
                sql: " \"TotalKwh\" >= 0 AND \"EnergyAmount\" >= 0 AND \"TotalAmount\" >= 0 ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reservations_active_location",
                table: "reservations");

            migrationBuilder.DropIndex(
                name: "IX_reservations_BatteryId",
                table: "reservations");

            migrationBuilder.DropIndex(
                name: "IX_reservations_ChargingBayId",
                table: "reservations");

            migrationBuilder.DropIndex(
                name: "IX_reservations_CustomerId_CreatedAtUtc",
                table: "reservations");

            migrationBuilder.DropIndex(
                name: "IX_payments_InvoiceId",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_outbox_pending_retry",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "IX_notifications_UserId_CreatedAtUtc_Id",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_locations_is_active_latitude_longitude",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "IX_locations_is_active_operating_status_name_id",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "ix_invoices_pending_customer",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_deferral",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_non_negative",
                table: "invoices");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "tariffs",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "SwapRate",
                table: "invoices",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "AppliedKwhRate",
                table: "charging_sessions",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "AppliedRate",
                table: "battery_swaps",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVChargeSwap.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveInvoiceParkingFees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hold the same lock as the column drops so no legacy writer can race the check.
            migrationBuilder.Sql("""
                LOCK TABLE invoices IN ACCESS EXCLUSIVE MODE;
                DO $invoice_upgrade$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM invoices
                        WHERE "IdleMinutes" <> 0 OR "IdleAmount" <> 0 OR "TotalAmount" <> "EnergyAmount"
                    ) THEN
                        RAISE EXCEPTION 'Existing invoices contain parking charges. Review those invoices before removing parking-fee columns.';
                    END IF;
                END;
                $invoice_upgrade$;
                """);

            migrationBuilder.DropColumn(
                name: "IdleAmount",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "IdleFeeRate",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "IdleMinutes",
                table: "invoices");

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalKwh",
                table: "invoices",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldPrecision: 8,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalAmount",
                table: "invoices",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "EnergyAmount",
                table: "invoices",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE invoices IN ACCESS EXCLUSIVE MODE;
                DO $invoice_rollback$
                BEGIN
                    IF EXISTS (SELECT 1 FROM invoices WHERE "TotalKwh" <> round("TotalKwh", 2)) THEN
                        RAISE EXCEPTION 'Existing invoices require three decimal places for energy consumption. Rolling back would round their recorded usage.';
                    END IF;
                END;
                $invoice_rollback$;
                """);

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalKwh",
                table: "invoices",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,3)",
                oldPrecision: 12,
                oldScale: 3);

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalAmount",
                table: "invoices",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(15,2)",
                oldPrecision: 15,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "EnergyAmount",
                table: "invoices",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(15,2)",
                oldPrecision: 15,
                oldScale: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "IdleAmount",
                table: "invoices",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IdleFeeRate",
                table: "invoices",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "IdleMinutes",
                table: "invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}

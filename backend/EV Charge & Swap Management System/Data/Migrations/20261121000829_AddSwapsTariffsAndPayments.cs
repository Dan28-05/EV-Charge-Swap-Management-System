using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVChargeSwap.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSwapsTariffsAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SwapRate",
                table: "invoices",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AppliedKwhRate",
                table: "charging_sessions",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustodianCustomerId",
                table: "batteries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "battery_swaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnedBatteryId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedBatteryId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionResult = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AppliedRate = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_battery_swaps", x => x.Id);
                    table.CheckConstraint("ck_battery_swaps_distinct", " \"ReturnedBatteryId\" <> \"IssuedBatteryId\" ");
                    table.ForeignKey(
                        name: "FK_battery_swaps_batteries_IssuedBatteryId",
                        column: x => x.IssuedBatteryId,
                        principalTable: "batteries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_battery_swaps_batteries_ReturnedBatteryId",
                        column: x => x.ReturnedBatteryId,
                        principalTable: "batteries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_battery_swaps_reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_battery_swaps_users_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.Id);
                    table.CheckConstraint("ck_payments_amount", " \"Amount\" >= 0 ");
                    table.ForeignKey(
                        name: "FK_payments_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tariffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceType = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    EffectiveFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tariffs", x => x.Id);
                    table.CheckConstraint("ck_tariffs_price", " \"UnitPrice\" >= 0 ");
                    table.ForeignKey(
                        name: "FK_tariffs_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tariffs_users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_webhook_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_webhook_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_webhook_events_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_batteries_CustodianCustomerId",
                table: "batteries",
                column: "CustodianCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_battery_swaps_IssuedBatteryId",
                table: "battery_swaps",
                column: "IssuedBatteryId");

            migrationBuilder.CreateIndex(
                name: "IX_battery_swaps_OperatorId",
                table: "battery_swaps",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_battery_swaps_ReservationId",
                table: "battery_swaps",
                column: "ReservationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_battery_swaps_ReturnedBatteryId",
                table: "battery_swaps",
                column: "ReturnedBatteryId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_webhook_events_PaymentId",
                table: "payment_webhook_events",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "ux_payments_pending",
                table: "payments",
                column: "InvoiceId",
                unique: true,
                filter: " \"Status\" = 'Pending' ");

            migrationBuilder.CreateIndex(
                name: "ux_payments_succeeded",
                table: "payments",
                column: "InvoiceId",
                unique: true,
                filter: " \"Status\" = 'Succeeded' ");

            migrationBuilder.CreateIndex(
                name: "IX_tariffs_CreatedById",
                table: "tariffs",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_tariffs_LocationId_ServiceType_EffectiveFromUtc",
                table: "tariffs",
                columns: new[] { "LocationId", "ServiceType", "EffectiveFromUtc" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_batteries_users_CustodianCustomerId",
                table: "batteries",
                column: "CustodianCustomerId",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_batteries_users_CustodianCustomerId",
                table: "batteries");

            migrationBuilder.DropTable(
                name: "battery_swaps");

            migrationBuilder.DropTable(
                name: "payment_webhook_events");

            migrationBuilder.DropTable(
                name: "tariffs");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropIndex(
                name: "IX_batteries_CustodianCustomerId",
                table: "batteries");

            migrationBuilder.DropColumn(
                name: "SwapRate",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "AppliedKwhRate",
                table: "charging_sessions");

            migrationBuilder.DropColumn(
                name: "CustodianCustomerId",
                table: "batteries");
        }
    }
}

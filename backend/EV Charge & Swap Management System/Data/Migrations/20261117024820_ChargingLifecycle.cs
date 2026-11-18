using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVChargeSwap.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChargingLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NoChargeConfirmedAtUtc",
                table: "reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NoChargeConfirmedById",
                table: "reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VehicleArrivedAtUtc",
                table: "reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VehicleArrivedById",
                table: "reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VehicleDepartedAtUtc",
                table: "reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VehicleDepartedById",
                table: "reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "charging_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChargingBayId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedById = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartMeterKwh = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    CompletedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndMeterKwh = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charging_sessions", x => x.Id);
                    table.CheckConstraint("ck_charging_sessions_completion", "(\"CompletedAtUtc\" IS NULL AND \"EndMeterKwh\" IS NULL AND \"CompletedById\" IS NULL) OR (\"CompletedAtUtc\" IS NOT NULL AND \"EndMeterKwh\" IS NOT NULL AND \"CompletedById\" IS NOT NULL)");
                    table.CheckConstraint("ck_charging_sessions_end_meter", "\"EndMeterKwh\" IS NULL OR \"EndMeterKwh\" >= \"StartMeterKwh\"");
                    table.CheckConstraint("ck_charging_sessions_start_meter", "\"StartMeterKwh\" >= 0");
                    table.ForeignKey(
                        name: "FK_charging_sessions_charging_bays_ChargingBayId",
                        column: x => x.ChargingBayId,
                        principalTable: "charging_bays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_charging_sessions_reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_charging_sessions_users_CompletedById",
                        column: x => x.CompletedById,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_charging_sessions_users_StartedById",
                        column: x => x.StartedById,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reservations_NoChargeConfirmedById",
                table: "reservations",
                column: "NoChargeConfirmedById");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_VehicleArrivedById",
                table: "reservations",
                column: "VehicleArrivedById");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_VehicleDepartedById",
                table: "reservations",
                column: "VehicleDepartedById");

            migrationBuilder.CreateIndex(
                name: "IX_charging_sessions_ChargingBayId",
                table: "charging_sessions",
                column: "ChargingBayId");

            migrationBuilder.CreateIndex(
                name: "IX_charging_sessions_CompletedById",
                table: "charging_sessions",
                column: "CompletedById");

            migrationBuilder.CreateIndex(
                name: "IX_charging_sessions_ReservationId",
                table: "charging_sessions",
                column: "ReservationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_charging_sessions_StartedById",
                table: "charging_sessions",
                column: "StartedById");

            migrationBuilder.AddForeignKey(
                name: "FK_reservations_users_NoChargeConfirmedById",
                table: "reservations",
                column: "NoChargeConfirmedById",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reservations_users_VehicleArrivedById",
                table: "reservations",
                column: "VehicleArrivedById",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reservations_users_VehicleDepartedById",
                table: "reservations",
                column: "VehicleDepartedById",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reservations_users_NoChargeConfirmedById",
                table: "reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_reservations_users_VehicleArrivedById",
                table: "reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_reservations_users_VehicleDepartedById",
                table: "reservations");

            migrationBuilder.DropTable(
                name: "charging_sessions");

            migrationBuilder.DropIndex(
                name: "IX_reservations_NoChargeConfirmedById",
                table: "reservations");

            migrationBuilder.DropIndex(
                name: "IX_reservations_VehicleArrivedById",
                table: "reservations");

            migrationBuilder.DropIndex(
                name: "IX_reservations_VehicleDepartedById",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "NoChargeConfirmedAtUtc",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "NoChargeConfirmedById",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "VehicleArrivedAtUtc",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "VehicleArrivedById",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "VehicleDepartedAtUtc",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "VehicleDepartedById",
                table: "reservations");
        }
    }
}

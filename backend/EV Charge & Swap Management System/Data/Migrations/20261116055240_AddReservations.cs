using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVChargeSwap.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "batteries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Serial = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batteries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_batteries_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "charging_bays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PowerKw = table.Column<int>(type: "integer", nullable: false),
                    EquipmentState = table.Column<int>(type: "integer", nullable: false),
                    ParkingState = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charging_bays", x => x.Id);
                    table.CheckConstraint("ck_charging_bays_power", " \"PowerKw\" > 0 ");
                    table.ForeignKey(
                        name: "FK_charging_bays_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "operator_assignments",
                columns: table => new
                {
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operator_assignments", x => new { x.LocationId, x.OperatorId });
                    table.ForeignKey(
                        name: "FK_operator_assignments_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_operator_assignments_users_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_hours",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceType = table.Column<int>(type: "integer", nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false),
                    OpensAtMinute = table.Column<int>(type: "integer", nullable: false),
                    ClosesAtMinute = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_hours", x => x.Id);
                    table.CheckConstraint("ck_service_hours_interval", " \"OpensAtMinute\" >= 0 AND \"OpensAtMinute\" < \"ClosesAtMinute\" AND \"ClosesAtMinute\" <= 1440 ");
                    table.ForeignKey(
                        name: "FK_service_hours_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChargingBayId = table.Column<Guid>(type: "uuid", nullable: true),
                    BatteryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CheckedInAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CheckedInById = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosingReason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ClosedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservations", x => x.Id);
                    table.CheckConstraint("ck_reservations_expiry", " \"ExpiresAtUtc\" > \"CreatedAtUtc\" ");
                    table.CheckConstraint("ck_reservations_resource", "(\"ChargingBayId\" IS NULL) <> (\"BatteryId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_reservations_batteries_BatteryId",
                        column: x => x.BatteryId,
                        principalTable: "batteries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reservations_charging_bays_ChargingBayId",
                        column: x => x.ChargingBayId,
                        principalTable: "charging_bays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reservations_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reservations_users_CheckedInById",
                        column: x => x.CheckedInById,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reservations_users_ClosedById",
                        column: x => x.ClosedById,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reservations_users_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_batteries_LocationId",
                table: "batteries",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_batteries_Serial",
                table: "batteries",
                column: "Serial",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_charging_bays_LocationId_Code",
                table: "charging_bays",
                columns: new[] { "LocationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operator_assignments_OperatorId",
                table: "operator_assignments",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_CheckedInById",
                table: "reservations",
                column: "CheckedInById");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_ClosedById",
                table: "reservations",
                column: "ClosedById");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_Code",
                table: "reservations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reservations_LocationId",
                table: "reservations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_Status_ExpiresAtUtc",
                table: "reservations",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_reservations_active_battery",
                table: "reservations",
                column: "BatteryId",
                unique: true,
                filter: " \"Status\" IN ('Held', 'CheckedIn')  AND \"BatteryId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_reservations_active_bay",
                table: "reservations",
                column: "ChargingBayId",
                unique: true,
                filter: " \"Status\" IN ('Held', 'CheckedIn')  AND \"ChargingBayId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_reservations_active_customer",
                table: "reservations",
                column: "CustomerId",
                unique: true,
                filter: " \"Status\" IN ('Held', 'CheckedIn') ");

            migrationBuilder.CreateIndex(
                name: "IX_service_hours_LocationId_ServiceType_Day_OpensAtMinute_Clos~",
                table: "service_hours",
                columns: new[] { "LocationId", "ServiceType", "Day", "OpensAtMinute", "ClosesAtMinute" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operator_assignments");

            migrationBuilder.DropTable(
                name: "reservations");

            migrationBuilder.DropTable(
                name: "service_hours");

            migrationBuilder.DropTable(
                name: "batteries");

            migrationBuilder.DropTable(
                name: "charging_bays");
        }
    }
}

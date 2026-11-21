using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVChargeSwap.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incident_reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChargingBayId = table.Column<Guid>(type: "uuid", nullable: true),
                    BatteryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChargingSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ResolvedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReplacementChargingBayId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReplacementBatteryId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_reports", x => x.Id);
                    table.CheckConstraint("ck_incidents_resolution", "((\"Resolution\" IS NULL AND \"ResolvedAtUtc\" IS NULL AND \"ResolvedById\" IS NULL)\n    OR (\"Resolution\" IN ('Reassigned', 'Cancelled', 'Recorded')\n        AND \"Resolution\" IS NOT NULL AND \"ResolvedAtUtc\" IS NOT NULL AND \"ResolvedById\" IS NOT NULL))\nAND (CASE WHEN \"Resolution\" = 'Reassigned' THEN\n    (\"ChargingBayId\" IS NOT NULL AND \"ReplacementChargingBayId\" IS NOT NULL AND \"ReplacementBatteryId\" IS NULL)\n    OR (\"BatteryId\" IS NOT NULL AND \"ReplacementBatteryId\" IS NOT NULL AND \"ReplacementChargingBayId\" IS NULL)\n    ELSE \"ReplacementChargingBayId\" IS NULL AND \"ReplacementBatteryId\" IS NULL END)");
                    table.CheckConstraint("ck_incidents_resource", "(\"ChargingBayId\" IS NULL) <> (\"BatteryId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_incident_reports_batteries_BatteryId",
                        column: x => x.BatteryId,
                        principalTable: "batteries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_incident_reports_batteries_ReplacementBatteryId",
                        column: x => x.ReplacementBatteryId,
                        principalTable: "batteries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_incident_reports_charging_bays_ChargingBayId",
                        column: x => x.ChargingBayId,
                        principalTable: "charging_bays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_incident_reports_charging_bays_ReplacementChargingBayId",
                        column: x => x.ReplacementChargingBayId,
                        principalTable: "charging_bays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_incident_reports_charging_sessions_ChargingSessionId",
                        column: x => x.ChargingSessionId,
                        principalTable: "charging_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_incident_reports_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_incident_reports_reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_incident_reports_users_ReportedById",
                        column: x => x.ReportedById,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_incident_reports_users_ResolvedById",
                        column: x => x.ResolvedById,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_incident_reports_BatteryId",
                table: "incident_reports",
                column: "BatteryId");

            migrationBuilder.CreateIndex(
                name: "IX_incident_reports_ChargingBayId",
                table: "incident_reports",
                column: "ChargingBayId");

            migrationBuilder.CreateIndex(
                name: "IX_incident_reports_ChargingSessionId",
                table: "incident_reports",
                column: "ChargingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_incident_reports_LocationId_ResolvedAtUtc_ReportedAtUtc",
                table: "incident_reports",
                columns: new[] { "LocationId", "ResolvedAtUtc", "ReportedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_incident_reports_ReplacementBatteryId",
                table: "incident_reports",
                column: "ReplacementBatteryId");

            migrationBuilder.CreateIndex(
                name: "IX_incident_reports_ReplacementChargingBayId",
                table: "incident_reports",
                column: "ReplacementChargingBayId");

            migrationBuilder.CreateIndex(
                name: "IX_incident_reports_ReportedById",
                table: "incident_reports",
                column: "ReportedById");

            migrationBuilder.CreateIndex(
                name: "IX_incident_reports_ReservationId",
                table: "incident_reports",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_incident_reports_ResolvedById",
                table: "incident_reports",
                column: "ResolvedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incident_reports");
        }
    }
}

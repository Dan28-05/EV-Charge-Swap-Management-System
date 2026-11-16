using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVChargeSwap.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAvailableChargingBaysToLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "available_charging_bays",
                table: "locations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_locations_available_charging_bays",
                table: "locations",
                sql: "available_charging_bays IS NULL OR (offers_car_charging AND available_charging_bays BETWEEN 0 AND 10)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_locations_available_charging_bays",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "available_charging_bays",
                table: "locations");
        }
    }
}

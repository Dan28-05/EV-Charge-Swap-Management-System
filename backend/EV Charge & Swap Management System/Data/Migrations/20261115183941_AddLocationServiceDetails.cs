using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVChargeSwap.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationServiceDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "battery_capacity_kwh",
                table: "locations",
                type: "numeric(3,1)",
                precision: 3,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "charging_power_kw",
                table: "locations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_open_24_hours",
                table: "locations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_locations_battery_capacity",
                table: "locations",
                sql: "battery_capacity_kwh IS NULL OR (offers_battery_swap AND battery_capacity_kwh = 1.5)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_locations_charging_power",
                table: "locations",
                sql: "charging_power_kw IS NULL OR (offers_car_charging AND charging_power_kw IN (30, 60, 150, 250))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_locations_battery_capacity",
                table: "locations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_locations_charging_power",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "battery_capacity_kwh",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "charging_power_kw",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "is_open_24_hours",
                table: "locations");
        }
    }
}

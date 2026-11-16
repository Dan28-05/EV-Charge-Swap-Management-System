using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVChargeSwap.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAvailableBatteriesToLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "available_batteries",
                table: "locations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_locations_available_batteries",
                table: "locations",
                sql: "available_batteries IS NULL OR (offers_battery_swap AND available_batteries BETWEEN 0 AND 20)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_locations_available_batteries",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "available_batteries",
                table: "locations");
        }
    }
}

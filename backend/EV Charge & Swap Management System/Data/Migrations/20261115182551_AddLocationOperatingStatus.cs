using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVChargeSwap.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationOperatingStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "operating_status",
                table: "locations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddCheckConstraint(
                name: "ck_locations_operating_status",
                table: "locations",
                sql: "operating_status IN ('Unknown', 'Open', 'Closed', 'Maintenance')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_locations_operating_status",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "operating_status",
                table: "locations");
        }
    }
}

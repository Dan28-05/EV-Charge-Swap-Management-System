using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVChargeSwap.Data.Migrations
{
    /// <inheritdoc />
    public partial class OperatorResourceUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAtUtc",
                table: "service_hours",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedById",
                table: "service_hours",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StateUpdatedAtUtc",
                table: "charging_bays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StateUpdatedById",
                table: "charging_bays",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StateUpdatedAtUtc",
                table: "batteries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StateUpdatedById",
                table: "batteries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_hours_UpdatedById",
                table: "service_hours",
                column: "UpdatedById");

            migrationBuilder.CreateIndex(
                name: "IX_charging_bays_StateUpdatedById",
                table: "charging_bays",
                column: "StateUpdatedById");

            migrationBuilder.CreateIndex(
                name: "IX_batteries_StateUpdatedById",
                table: "batteries",
                column: "StateUpdatedById");

            migrationBuilder.AddForeignKey(
                name: "FK_batteries_users_StateUpdatedById",
                table: "batteries",
                column: "StateUpdatedById",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_charging_bays_users_StateUpdatedById",
                table: "charging_bays",
                column: "StateUpdatedById",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_service_hours_users_UpdatedById",
                table: "service_hours",
                column: "UpdatedById",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_batteries_users_StateUpdatedById",
                table: "batteries");

            migrationBuilder.DropForeignKey(
                name: "FK_charging_bays_users_StateUpdatedById",
                table: "charging_bays");

            migrationBuilder.DropForeignKey(
                name: "FK_service_hours_users_UpdatedById",
                table: "service_hours");

            migrationBuilder.DropIndex(
                name: "IX_service_hours_UpdatedById",
                table: "service_hours");

            migrationBuilder.DropIndex(
                name: "IX_charging_bays_StateUpdatedById",
                table: "charging_bays");

            migrationBuilder.DropIndex(
                name: "IX_batteries_StateUpdatedById",
                table: "batteries");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "service_hours");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                table: "service_hours");

            migrationBuilder.DropColumn(
                name: "StateUpdatedAtUtc",
                table: "charging_bays");

            migrationBuilder.DropColumn(
                name: "StateUpdatedById",
                table: "charging_bays");

            migrationBuilder.DropColumn(
                name: "StateUpdatedAtUtc",
                table: "batteries");

            migrationBuilder.DropColumn(
                name: "StateUpdatedById",
                table: "batteries");
        }
    }
}

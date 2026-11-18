using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVChargeSwap.Data.Migrations
{
    /// <inheritdoc />
    public partial class OperatorAssignmentHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM operator_assignments GROUP BY "OperatorId" HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'An Operator has multiple existing assignments. Resolve them before applying OperatorAssignmentHistory.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropPrimaryKey(
                name: "PK_operator_assignments",
                table: "operator_assignments");

            migrationBuilder.DropIndex(
                name: "IX_operator_assignments_OperatorId",
                table: "operator_assignments");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "operator_assignments",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssignedAtUtc",
                table: "operator_assignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedByAdminId",
                table: "operator_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EndedAtUtc",
                table: "operator_assignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_operator_assignments",
                table: "operator_assignments",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_operator_assignments_AssignedByAdminId",
                table: "operator_assignments",
                column: "AssignedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_operator_assignments_LocationId",
                table: "operator_assignments",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_operator_assignments_OperatorId_AssignedAtUtc",
                table: "operator_assignments",
                columns: new[] { "OperatorId", "AssignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_operator_active_assignment",
                table: "operator_assignments",
                column: "OperatorId",
                unique: true,
                filter: "\"EndedAtUtc\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_operator_assignments_users_AssignedByAdminId",
                table: "operator_assignments",
                column: "AssignedByAdminId",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_operator_assignments_users_AssignedByAdminId",
                table: "operator_assignments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_operator_assignments",
                table: "operator_assignments");

            migrationBuilder.DropIndex(
                name: "IX_operator_assignments_AssignedByAdminId",
                table: "operator_assignments");

            migrationBuilder.DropIndex(
                name: "IX_operator_assignments_LocationId",
                table: "operator_assignments");

            migrationBuilder.DropIndex(
                name: "IX_operator_assignments_OperatorId_AssignedAtUtc",
                table: "operator_assignments");

            migrationBuilder.DropIndex(
                name: "ux_operator_active_assignment",
                table: "operator_assignments");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "operator_assignments");

            migrationBuilder.DropColumn(
                name: "AssignedAtUtc",
                table: "operator_assignments");

            migrationBuilder.DropColumn(
                name: "AssignedByAdminId",
                table: "operator_assignments");

            migrationBuilder.DropColumn(
                name: "EndedAtUtc",
                table: "operator_assignments");

            migrationBuilder.AddPrimaryKey(
                name: "PK_operator_assignments",
                table: "operator_assignments",
                columns: new[] { "LocationId", "OperatorId" });

            migrationBuilder.CreateIndex(
                name: "IX_operator_assignments_OperatorId",
                table: "operator_assignments",
                column: "OperatorId");
        }
    }
}

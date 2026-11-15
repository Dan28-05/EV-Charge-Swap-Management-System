using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVChargeSwap.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContactVerificationAndPhoneLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "verification_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    destination = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_challenges", x => x.id);
                    table.ForeignKey(
                        name: "FK_verification_challenges_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_users_phone_number",
                table: "users",
                column: "phone_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_verification_challenges_user_id_channel_created_at_utc",
                table: "verification_challenges",
                columns: new[] { "user_id", "channel", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "verification_challenges");

            migrationBuilder.DropIndex(
                name: "ux_users_phone_number",
                table: "users");
        }
    }
}

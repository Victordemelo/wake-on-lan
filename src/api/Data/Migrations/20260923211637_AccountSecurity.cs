using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RemoteWake.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccountSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WakeAttempts_Machines_MachineId",
                table: "WakeAttempts");

            migrationBuilder.AlterColumn<Guid>(
                name: "MachineId",
                table: "WakeAttempts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "WakeAttempts",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MachineName",
                table: "WakeAttempts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "WakeAttempts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Existing history belongs to the owner of its machine. Keep the machine name so
            // each record survives if the machine is removed later.
            migrationBuilder.Sql("""
                UPDATE "WakeAttempts" AS attempt
                SET "OwnerId" = machine."OwnerId", "MachineName" = machine."Name"
                FROM "Machines" AS machine
                WHERE attempt."MachineId" = machine."Id";
                """);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PasswordChangedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingTotpSecret",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TotpEnabledAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TotpLastStep",
                table: "Users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TotpSecret",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryCodes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SecurityEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WakeAttempts_OwnerId_RequestedAt",
                table: "WakeAttempts",
                columns: new[] { "OwnerId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryCodes_UserId",
                table: "RecoveryCodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_UserId_CreatedAt",
                table: "SecurityEvents",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_TokenHash",
                table: "UserSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId",
                table: "UserSessions",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_WakeAttempts_Machines_MachineId",
                table: "WakeAttempts",
                column: "MachineId",
                principalTable: "Machines",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_WakeAttempts_Users_OwnerId",
                table: "WakeAttempts",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Records of removed machines cannot exist in the previous schema.
            migrationBuilder.Sql("""DELETE FROM "WakeAttempts" WHERE "MachineId" IS NULL;""");

            migrationBuilder.DropForeignKey(
                name: "FK_WakeAttempts_Machines_MachineId",
                table: "WakeAttempts");

            migrationBuilder.DropForeignKey(
                name: "FK_WakeAttempts_Users_OwnerId",
                table: "WakeAttempts");

            migrationBuilder.DropTable(
                name: "RecoveryCodes");

            migrationBuilder.DropTable(
                name: "SecurityEvents");

            migrationBuilder.DropTable(
                name: "UserSessions");

            migrationBuilder.DropIndex(
                name: "IX_WakeAttempts_OwnerId_RequestedAt",
                table: "WakeAttempts");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "WakeAttempts");

            migrationBuilder.DropColumn(
                name: "MachineName",
                table: "WakeAttempts");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "WakeAttempts");

            migrationBuilder.DropColumn(
                name: "PasswordChangedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PendingTotpSecret",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TotpEnabledAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TotpLastStep",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TotpSecret",
                table: "Users");

            migrationBuilder.AlterColumn<Guid>(
                name: "MachineId",
                table: "WakeAttempts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WakeAttempts_Machines_MachineId",
                table: "WakeAttempts",
                column: "MachineId",
                principalTable: "Machines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

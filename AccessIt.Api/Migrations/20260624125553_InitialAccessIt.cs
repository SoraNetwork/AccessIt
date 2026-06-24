using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessIt.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccessIt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DingTalkUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DingTalkUnionId = table.Column<string>(type: "TEXT", nullable: true),
                    DingTalkOpenId = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Mobile = table.Column<string>(type: "TEXT", nullable: true),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastDirectorySyncAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", nullable: false),
                    DetailsJson = table.Column<string>(type: "TEXT", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FaceAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", nullable: false),
                    PublicToken = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HikiotAuthorizationStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HikiotAuthorizationStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HikiotConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TeamNo = table.Column<string>(type: "TEXT", nullable: true),
                    DefaultDepartmentNo = table.Column<string>(type: "TEXT", nullable: true),
                    AccountNo = table.Column<string>(type: "TEXT", nullable: true),
                    AuthorizedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ProtectedAppAccessToken = table.Column<string>(type: "TEXT", nullable: true),
                    ProtectedRefreshAppToken = table.Column<string>(type: "TEXT", nullable: true),
                    AppTokenExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProtectedUserAccessToken = table.Column<string>(type: "TEXT", nullable: true),
                    ProtectedRefreshUserToken = table.Column<string>(type: "TEXT", nullable: true),
                    UserTokenExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AuthorizedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastErrorAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    NeedsReauthorization = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HikiotConnections", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "HikiotConnections",
                columns: new[] { "Id", "AccountNo", "AppTokenExpiresAtUtc", "AuthorizedAtUtc", "AuthorizedByUserId", "DefaultDepartmentNo", "LastError", "LastErrorAtUtc", "NeedsReauthorization", "ProtectedAppAccessToken", "ProtectedRefreshAppToken", "ProtectedRefreshUserToken", "ProtectedUserAccessToken", "TeamNo", "UserTokenExpiresAtUtc" },
                values: new object[] { 1, null, null, null, null, null, null, null, true, null, null, null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_DingTalkUnionId",
                table: "ApplicationUsers",
                column: "DingTalkUnionId",
                unique: true,
                filter: "\"DingTalkUnionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_DingTalkUserId",
                table: "ApplicationUsers",
                column: "DingTalkUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredAtUtc_Action",
                table: "AuditEvents",
                columns: new[] { "OccurredAtUtc", "Action" });

            migrationBuilder.CreateIndex(
                name: "IX_FaceAssets_PublicToken",
                table: "FaceAssets",
                column: "PublicToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HikiotAuthorizationStates_State",
                table: "HikiotAuthorizationStates",
                column: "State",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationUsers");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "FaceAssets");

            migrationBuilder.DropTable(
                name: "HikiotAuthorizationStates");

            migrationBuilder.DropTable(
                name: "HikiotConnections");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessIt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessPeople : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessPeople",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Mobile = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    HikiotPersonNo = table.Column<string>(type: "TEXT", nullable: true),
                    DeviceEmployeeNo = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CardNo = table.Column<string>(type: "TEXT", nullable: true),
                    FaceAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PermanentValid = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableBeginTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EnableEndTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    QrShareToken = table.Column<string>(type: "TEXT", nullable: true),
                    QrContent = table.Column<string>(type: "TEXT", nullable: true),
                    QrRevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastIssueResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessPeople", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessPeople_FaceAssets_FaceAssetId",
                        column: x => x.FaceAssetId,
                        principalTable: "FaceAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PersonSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccessPersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UnionId = table.Column<string>(type: "TEXT", nullable: true),
                    SyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonSources_AccessPeople_AccessPersonId",
                        column: x => x.AccessPersonId,
                        principalTable: "AccessPeople",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessPeople_FaceAssetId",
                table: "AccessPeople",
                column: "FaceAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessPeople_HikiotPersonNo",
                table: "AccessPeople",
                column: "HikiotPersonNo",
                unique: true,
                filter: "\"HikiotPersonNo\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccessPeople_NormalizedName",
                table: "AccessPeople",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_AccessPeople_QrShareToken",
                table: "AccessPeople",
                column: "QrShareToken",
                unique: true,
                filter: "\"QrShareToken\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PersonSources_AccessPersonId_SourceType",
                table: "PersonSources",
                columns: new[] { "AccessPersonId", "SourceType" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonSources_SourceType_ExternalId",
                table: "PersonSources",
                columns: new[] { "SourceType", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonSources");

            migrationBuilder.DropTable(
                name: "AccessPeople");
        }
    }
}

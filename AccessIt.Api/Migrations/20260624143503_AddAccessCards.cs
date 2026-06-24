using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessIt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccessPersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CardNo = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsVirtual = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessCards_AccessPeople_AccessPersonId",
                        column: x => x.AccessPersonId,
                        principalTable: "AccessPeople",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessCards_AccessPersonId",
                table: "AccessCards",
                column: "AccessPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessCards_CardNo",
                table: "AccessCards",
                column: "CardNo",
                unique: true);

            // 将旧版单卡字段无损迁移为一人多卡记录。
            migrationBuilder.Sql("""
                INSERT OR IGNORE INTO "AccessCards" ("Id", "AccessPersonId", "CardNo", "IsVirtual", "CreatedAtUtc")
                SELECT lower(hex(randomblob(16))), "Id", "CardNo", 0, CURRENT_TIMESTAMP
                FROM "AccessPeople"
                WHERE "CardNo" IS NOT NULL AND trim("CardNo") <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessCards");
        }
    }
}

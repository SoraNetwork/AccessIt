using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessIt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitorListIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AccessPeople_Kind_EnableEndTimeUtc_CreatedAtUtc",
                table: "AccessPeople",
                columns: new[] { "Kind", "EnableEndTimeUtc", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccessPeople_Kind_EnableEndTimeUtc_CreatedAtUtc",
                table: "AccessPeople");
        }
    }
}

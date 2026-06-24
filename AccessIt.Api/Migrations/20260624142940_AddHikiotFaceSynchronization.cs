using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessIt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHikiotFaceSynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HikiotFaceUrl",
                table: "AccessPeople",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HikiotFaceUrl",
                table: "AccessPeople");
        }
    }
}

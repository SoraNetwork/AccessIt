using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessIt.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAllDayTemplateId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AllDayTemplateId",
                table: "AccessDevices",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllDayTemplateId",
                table: "AccessDevices");
        }
    }
}

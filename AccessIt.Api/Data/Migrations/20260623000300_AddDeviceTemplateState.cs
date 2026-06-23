using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using AccessIt.Api.Data;

#nullable disable

namespace AccessIt.Api.Data.Migrations;

[DbContext(typeof(AccessItDbContext))]
[Migration("20260623000300_AddDeviceTemplateState")]
public partial class AddDeviceTemplateState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<bool>(name: "HasAllDayTemplate", table: "AccessDevices", type: "INTEGER", nullable: false, defaultValue: false);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "HasAllDayTemplate", table: "AccessDevices");
}

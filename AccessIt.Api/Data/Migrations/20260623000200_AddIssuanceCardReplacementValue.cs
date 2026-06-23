using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using AccessIt.Api.Data;

#nullable disable

namespace AccessIt.Api.Data.Migrations;

[DbContext(typeof(AccessItDbContext))]
[Migration("20260623000200_AddIssuanceCardReplacementValue")]
public partial class AddIssuanceCardReplacementValue : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(name: "CardNoOverride", table: "IssuanceJobs", type: "TEXT", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "CardNoOverride", table: "IssuanceJobs");
}

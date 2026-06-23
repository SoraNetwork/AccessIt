using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using AccessIt.Api.Data;

#nullable disable

namespace AccessIt.Api.Data.Migrations;

[DbContext(typeof(AccessItDbContext))]
[Migration("20260623000400_AddStandardAuthorityIssueState")]
public partial class AddStandardAuthorityIssueState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "HikiotAuthorityConfigId", table: "DeviceGrants", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<long>(name: "HikiotPersonDeviceId", table: "DeviceGrants", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<string>(name: "HikiotIssueBatchNo", table: "DeviceGrants", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<int>(name: "HikiotInfoStatus", table: "DeviceGrants", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "HikiotIsSupported", table: "DeviceGrants", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "HikiotIsSending", table: "DeviceGrants", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<string>(name: "HikiotLastFailedReason", table: "DeviceGrants", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "HikiotStatusCheckedAtUtc", table: "DeviceGrants", type: "TEXT", nullable: true);
        migrationBuilder.CreateTable(
            name: "HikiotIssueBatches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                BatchNo = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                AccessPersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                DeviceSerial = table.Column<string>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                FailureReason = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                CheckedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_HikiotIssueBatches", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_HikiotIssueBatches_BatchNo", table: "HikiotIssueBatches", column: "BatchNo", unique: true);
        migrationBuilder.CreateIndex(name: "IX_HikiotIssueBatches_AccessPersonId_CreatedAtUtc", table: "HikiotIssueBatches", columns: new[] { "AccessPersonId", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "HikiotIssueBatches");
        migrationBuilder.DropColumn(name: "HikiotAuthorityConfigId", table: "DeviceGrants");
        migrationBuilder.DropColumn(name: "HikiotPersonDeviceId", table: "DeviceGrants");
        migrationBuilder.DropColumn(name: "HikiotIssueBatchNo", table: "DeviceGrants");
        migrationBuilder.DropColumn(name: "HikiotInfoStatus", table: "DeviceGrants");
        migrationBuilder.DropColumn(name: "HikiotIsSupported", table: "DeviceGrants");
        migrationBuilder.DropColumn(name: "HikiotIsSending", table: "DeviceGrants");
        migrationBuilder.DropColumn(name: "HikiotLastFailedReason", table: "DeviceGrants");
        migrationBuilder.DropColumn(name: "HikiotStatusCheckedAtUtc", table: "DeviceGrants");
    }
}

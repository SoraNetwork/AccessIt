using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using AccessIt.Api.Data;

#nullable disable

namespace AccessIt.Api.Data.Migrations;

[DbContext(typeof(AccessItDbContext))]
[Migration("20260623000100_AddHikiotTeamPeople")]
public partial class AddHikiotTeamPeople : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "HikiotPersonNo", table: "AccessPeople", type: "TEXT", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<string>(name: "HikiotDepartmentNo", table: "AccessPeople", type: "TEXT", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<string>(name: "HikiotJobNumber", table: "AccessPeople", type: "TEXT", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<string>(name: "HikiotJobPosition", table: "AccessPeople", type: "TEXT", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<int>(name: "HikiotSex", table: "AccessPeople", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<long>(name: "HikiotFaceIdentificationId", table: "AccessPeople", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<long>(name: "HikiotIdentificationId", table: "AccessCards", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<long>(name: "HikiotIdentificationId", table: "FaceAssets", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<string>(name: "DefaultDepartmentNo", table: "HikiotConnections", type: "TEXT", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_AccessPeople_HikiotPersonNo", table: "AccessPeople", column: "HikiotPersonNo", unique: true, filter: "\"HikiotPersonNo\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_AccessPeople_HikiotPersonNo", table: "AccessPeople");
        migrationBuilder.DropColumn(name: "HikiotPersonNo", table: "AccessPeople");
        migrationBuilder.DropColumn(name: "HikiotDepartmentNo", table: "AccessPeople");
        migrationBuilder.DropColumn(name: "HikiotJobNumber", table: "AccessPeople");
        migrationBuilder.DropColumn(name: "HikiotJobPosition", table: "AccessPeople");
        migrationBuilder.DropColumn(name: "HikiotSex", table: "AccessPeople");
        migrationBuilder.DropColumn(name: "HikiotFaceIdentificationId", table: "AccessPeople");
        migrationBuilder.DropColumn(name: "HikiotIdentificationId", table: "AccessCards");
        migrationBuilder.DropColumn(name: "HikiotIdentificationId", table: "FaceAssets");
        migrationBuilder.DropColumn(name: "DefaultDepartmentNo", table: "HikiotConnections");
    }
}

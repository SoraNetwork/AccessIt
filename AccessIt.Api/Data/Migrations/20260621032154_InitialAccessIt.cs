using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessIt.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccessIt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceSerial = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    GroupNo = table.Column<string>(type: "TEXT", nullable: true),
                    GroupName = table.Column<string>(type: "TEXT", nullable: true),
                    IsManaged = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsUserInfo = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsCardInfo = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsFace = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsPassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsPurePassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsRemoteOpen = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsUserRightPlanTemplate = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccessPeople",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    EmployeeNo = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    DingTalkUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Mobile = table.Column<string>(type: "TEXT", nullable: true),
                    PermanentValid = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableBeginTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EnableEndTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MaxOpenDoorTime = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessPeople", x => x.Id);
                });

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

            migrationBuilder.CreateTable(
                name: "IssuanceJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccessPersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AccessDeviceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ParentJobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RelatedEntityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", nullable: true),
                    FailureMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuanceJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PersonNumberSequences",
                columns: table => new
                {
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    LastValue = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonNumberSequences", x => x.Kind);
                });

            migrationBuilder.CreateTable(
                name: "SyncConflicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SyncRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccessPersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EmployeeNo = table.Column<string>(type: "TEXT", nullable: false),
                    FieldName = table.Column<string>(type: "TEXT", nullable: false),
                    LocalValue = table.Column<string>(type: "TEXT", nullable: true),
                    RemoteValue = table.Column<string>(type: "TEXT", nullable: true),
                    Resolution = table.Column<int>(type: "INTEGER", nullable: false),
                    ResolvedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncConflicts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccessDeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RemoteCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NewCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ConflictCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureMessage = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VisitorQrShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccessPersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OpaqueToken = table.Column<string>(type: "TEXT", nullable: false),
                    QrCodeContent = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IssuedToHostUserId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitorQrShares", x => x.Id);
                });

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

            migrationBuilder.CreateTable(
                name: "DeviceGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccessPersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccessDeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceGrants_AccessDevices_AccessDeviceId",
                        column: x => x.AccessDeviceId,
                        principalTable: "AccessDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeviceGrants_AccessPeople_AccessPersonId",
                        column: x => x.AccessPersonId,
                        principalTable: "AccessPeople",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DevicePasswords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccessPersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProtectedValue = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevicePasswords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DevicePasswords_AccessPeople_AccessPersonId",
                        column: x => x.AccessPersonId,
                        principalTable: "AccessPeople",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FaceAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccessPersonId = table.Column<Guid>(type: "TEXT", nullable: false),
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
                    table.ForeignKey(
                        name: "FK_FaceAssets_AccessPeople_AccessPersonId",
                        column: x => x.AccessPersonId,
                        principalTable: "AccessPeople",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "HikiotConnections",
                columns: new[] { "Id", "AccountNo", "AppTokenExpiresAtUtc", "AuthorizedAtUtc", "AuthorizedByUserId", "LastError", "LastErrorAtUtc", "NeedsReauthorization", "ProtectedAppAccessToken", "ProtectedRefreshAppToken", "ProtectedRefreshUserToken", "ProtectedUserAccessToken", "TeamNo", "UserTokenExpiresAtUtc" },
                values: new object[] { 1, null, null, null, null, null, null, true, null, null, null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_AccessCards_AccessPersonId",
                table: "AccessCards",
                column: "AccessPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessCards_CardNo",
                table: "AccessCards",
                column: "CardNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessDevices_DeviceSerial",
                table: "AccessDevices",
                column: "DeviceSerial",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessPeople_EmployeeNo",
                table: "AccessPeople",
                column: "EmployeeNo",
                unique: true);

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
                name: "IX_DeviceGrants_AccessDeviceId",
                table: "DeviceGrants",
                column: "AccessDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceGrants_AccessPersonId_AccessDeviceId",
                table: "DeviceGrants",
                columns: new[] { "AccessPersonId", "AccessDeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DevicePasswords_AccessPersonId",
                table: "DevicePasswords",
                column: "AccessPersonId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaceAssets_AccessPersonId",
                table: "FaceAssets",
                column: "AccessPersonId");

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

            migrationBuilder.CreateIndex(
                name: "IX_IssuanceJobs_Status_NextAttemptAtUtc",
                table: "IssuanceJobs",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflicts_SyncRunId_Resolution",
                table: "SyncConflicts",
                columns: new[] { "SyncRunId", "Resolution" });

            migrationBuilder.CreateIndex(
                name: "IX_VisitorQrShares_ExpiresAtUtc",
                table: "VisitorQrShares",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_VisitorQrShares_OpaqueToken",
                table: "VisitorQrShares",
                column: "OpaqueToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessCards");

            migrationBuilder.DropTable(
                name: "ApplicationUsers");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "DeviceGrants");

            migrationBuilder.DropTable(
                name: "DevicePasswords");

            migrationBuilder.DropTable(
                name: "FaceAssets");

            migrationBuilder.DropTable(
                name: "HikiotAuthorizationStates");

            migrationBuilder.DropTable(
                name: "HikiotConnections");

            migrationBuilder.DropTable(
                name: "IssuanceJobs");

            migrationBuilder.DropTable(
                name: "PersonNumberSequences");

            migrationBuilder.DropTable(
                name: "SyncConflicts");

            migrationBuilder.DropTable(
                name: "SyncRuns");

            migrationBuilder.DropTable(
                name: "VisitorQrShares");

            migrationBuilder.DropTable(
                name: "AccessDevices");

            migrationBuilder.DropTable(
                name: "AccessPeople");
        }
    }
}

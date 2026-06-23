using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Domain;

namespace AccessIt.Api.Data;

public class AccessItDbContext(DbContextOptions<AccessItDbContext> options) : DbContext(options)
{
    public DbSet<AccessPerson> AccessPeople => Set<AccessPerson>();
    public DbSet<AccessDevice> AccessDevices => Set<AccessDevice>();
    public DbSet<DeviceGrant> DeviceGrants => Set<DeviceGrant>();
    public DbSet<AccessCard> AccessCards => Set<AccessCard>();
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
    public DbSet<HikiotConnection> HikiotConnections => Set<HikiotConnection>();
    public DbSet<HikiotAuthorizationState> HikiotAuthorizationStates => Set<HikiotAuthorizationState>();
    public DbSet<FaceAsset> FaceAssets => Set<FaceAsset>();
    public DbSet<DevicePassword> DevicePasswords => Set<DevicePassword>();
    public DbSet<IssuanceJob> IssuanceJobs => Set<IssuanceJob>();
    public DbSet<HikiotIssueBatch> HikiotIssueBatches => Set<HikiotIssueBatch>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();
    public DbSet<VisitorQrShare> VisitorQrShares => Set<VisitorQrShare>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<PersonNumberSequence> PersonNumberSequences => Set<PersonNumberSequence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccessPerson>(entity =>
        {
            entity.HasIndex(x => x.EmployeeNo).IsUnique();
            entity.Property(x => x.EmployeeNo).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(32).IsRequired();
            entity.Property(x => x.DingTalkUserId).HasMaxLength(128);
            entity.Property(x => x.HikiotPersonNo).HasMaxLength(32);
            entity.Property(x => x.HikiotDepartmentNo).HasMaxLength(32);
            entity.Property(x => x.HikiotJobNumber).HasMaxLength(32);
            entity.Property(x => x.HikiotJobPosition).HasMaxLength(32);
            entity.HasIndex(x => x.HikiotPersonNo).IsUnique().HasFilter("\"HikiotPersonNo\" IS NOT NULL");
        });

        modelBuilder.Entity<AccessDevice>(entity =>
        {
            entity.HasIndex(x => x.DeviceSerial).IsUnique();
            entity.Property(x => x.DeviceSerial).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<DeviceGrant>(entity =>
        {
            entity.HasIndex(x => new { x.AccessPersonId, x.AccessDeviceId }).IsUnique();
            entity.HasOne(x => x.AccessPerson).WithMany(x => x.DeviceGrants).HasForeignKey(x => x.AccessPersonId);
            entity.HasOne(x => x.AccessDevice).WithMany(x => x.DeviceGrants).HasForeignKey(x => x.AccessDeviceId);
        });

        modelBuilder.Entity<HikiotIssueBatch>(entity =>
        {
            entity.HasIndex(x => x.BatchNo).IsUnique();
            entity.HasIndex(x => new { x.AccessPersonId, x.CreatedAtUtc });
            entity.Property(x => x.BatchNo).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<AccessCard>(entity =>
        {
            entity.HasIndex(x => x.CardNo).IsUnique();
            entity.Property(x => x.CardNo).HasMaxLength(64).IsRequired();
            entity.HasOne(x => x.AccessPerson).WithMany(x => x.Cards).HasForeignKey(x => x.AccessPersonId);
        });

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.HasIndex(x => x.DingTalkUserId).IsUnique();
            entity.HasIndex(x => x.DingTalkUnionId).IsUnique().HasFilter("\"DingTalkUnionId\" IS NOT NULL");
            entity.Property(x => x.DingTalkUserId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<HikiotConnection>().HasData(new HikiotConnection { Id = 1, NeedsReauthorization = true });

        modelBuilder.Entity<HikiotAuthorizationState>(entity =>
        {
            entity.HasIndex(x => x.State).IsUnique();
            entity.Property(x => x.State).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<FaceAsset>(entity =>
        {
            entity.HasIndex(x => x.PublicToken).IsUnique();
            entity.HasOne(x => x.AccessPerson).WithMany(x => x.FaceAssets).HasForeignKey(x => x.AccessPersonId);
        });

        modelBuilder.Entity<DevicePassword>(entity =>
        {
            entity.HasIndex(x => x.AccessPersonId).IsUnique();
            entity.HasOne(x => x.AccessPerson).WithOne().HasForeignKey<DevicePassword>(x => x.AccessPersonId);
        });

        modelBuilder.Entity<IssuanceJob>(entity =>
        {
            entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        });

        modelBuilder.Entity<SyncConflict>(entity =>
        {
            entity.HasIndex(x => new { x.SyncRunId, x.Resolution });
        });

        modelBuilder.Entity<VisitorQrShare>(entity =>
        {
            entity.HasIndex(x => x.OpaqueToken).IsUnique();
            entity.HasIndex(x => x.ExpiresAtUtc);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasIndex(x => new { x.OccurredAtUtc, x.Action });
            entity.Property(x => x.Action).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<PersonNumberSequence>().HasKey(x => x.Kind);
    }
}

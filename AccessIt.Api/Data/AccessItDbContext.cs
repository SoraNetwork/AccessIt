using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Domain;
using AccessIt.Api.Services;

namespace AccessIt.Api.Data;

public class AccessItDbContext(DbContextOptions<AccessItDbContext> options) : DbContext(options)
{
    // 登录与用户管理
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();

    // HIKIoT 连接（AT/UT token 缓存）与授权 state
    public DbSet<HikiotConnection> HikiotConnections => Set<HikiotConnection>();
    public DbSet<HikiotAuthorizationState> HikiotAuthorizationStates => Set<HikiotAuthorizationState>();

    // 通用基础设施
    public DbSet<FaceAsset> FaceAssets => Set<FaceAsset>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AccessPerson> AccessPeople => Set<AccessPerson>();
    public DbSet<PersonSource> PersonSources => Set<PersonSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasIndex(x => new { x.OccurredAtUtc, x.Action });
            entity.Property(x => x.Action).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<AccessPerson>(entity =>
        {
            entity.HasIndex(x => x.NormalizedName);
            entity.HasIndex(x => x.HikiotPersonNo).IsUnique().HasFilter("\"HikiotPersonNo\" IS NOT NULL");
            entity.HasIndex(x => x.QrShareToken).IsUnique().HasFilter("\"QrShareToken\" IS NOT NULL");
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.DeviceEmployeeNo).HasMaxLength(32).IsRequired();
            entity.HasOne(x => x.FaceAsset).WithMany().HasForeignKey(x => x.FaceAssetId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<PersonSource>(entity =>
        {
            entity.HasIndex(x => new { x.SourceType, x.ExternalId }).IsUnique();
            entity.HasIndex(x => new { x.AccessPersonId, x.SourceType });
            entity.Property(x => x.ExternalId).HasMaxLength(128).IsRequired();
            entity.HasOne(x => x.AccessPerson).WithMany(x => x.Sources).HasForeignKey(x => x.AccessPersonId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

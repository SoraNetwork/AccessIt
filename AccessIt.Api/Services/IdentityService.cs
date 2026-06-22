using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AccessIt.Api.Configuration;
using AccessIt.Api.Data;
using AccessIt.Api.DingTalk;
using AccessIt.Api.Domain;

namespace AccessIt.Api.Services;

public interface IIdentityService
{
    Task<ApplicationUser> SignInAsync(DingTalkProfile profile, CancellationToken cancellationToken = default);
    Task<DirectorySyncResult> SyncDirectoryAsync(IReadOnlyList<DingTalkDirectoryEntry> entries, CancellationToken cancellationToken = default);
}

public sealed record DirectorySyncResult(int Created, int Updated, int Deactivated);

public sealed class IdentityService(AccessItDbContext db, IOptions<DingTalkOptions> options) : IIdentityService
{
    public async Task<ApplicationUser> SignInAsync(DingTalkProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.UserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Name);
        ApplicationUser? user = null;
        if (!string.IsNullOrWhiteSpace(profile.UnionId))
            user = await db.ApplicationUsers.SingleOrDefaultAsync(x => x.DingTalkUnionId == profile.UnionId, cancellationToken);
        user ??= await db.ApplicationUsers.SingleOrDefaultAsync(x => x.DingTalkUserId == profile.UserId, cancellationToken);

        if (user is null)
        {
            user = new ApplicationUser
            {
                DingTalkUserId = profile.UserId,
                DingTalkUnionId = profile.UnionId,
                Name = profile.Name.Trim(),
                Mobile = profile.Mobile,
                Role = IsBootstrapAdmin(profile.Name) ? ApplicationRole.SuperAdmin : ApplicationRole.None
            };
            db.ApplicationUsers.Add(user);
        }
        else
        {
            user.DingTalkUserId = profile.UserId;
            user.DingTalkUnionId ??= profile.UnionId;
            user.Name = profile.Name.Trim();
            user.Mobile = profile.Mobile;
        }
        user.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<DirectorySyncResult> SyncDirectoryAsync(IReadOnlyList<DingTalkDirectoryEntry> entries, CancellationToken cancellationToken = default)
    {
        var existing = await db.ApplicationUsers.ToListAsync(cancellationToken);
        var created = 0;
        var updated = 0;
        foreach (var entry in entries.Where(x => !string.IsNullOrWhiteSpace(x.UserId) && !string.IsNullOrWhiteSpace(x.Name)))
        {
            var user = !string.IsNullOrWhiteSpace(entry.UnionId)
                ? existing.SingleOrDefault(x => x.DingTalkUnionId == entry.UnionId)
                : null;
            user ??= existing.SingleOrDefault(x => x.DingTalkUserId == entry.UserId);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    DingTalkUserId = entry.UserId,
                    DingTalkUnionId = entry.UnionId,
                    Name = entry.Name.Trim(),
                    Mobile = entry.Mobile,
                    Role = IsBootstrapAdmin(entry.Name) ? ApplicationRole.SuperAdmin : ApplicationRole.None,
                    IsActive = entry.IsActive,
                    LastDirectorySyncAtUtc = DateTime.UtcNow
                };
                db.ApplicationUsers.Add(user);
                existing.Add(user);
                created++;
            }
            else
            {
                user.DingTalkUserId = entry.UserId;
                user.DingTalkUnionId ??= entry.UnionId;
                user.Name = entry.Name.Trim();
                user.Mobile = entry.Mobile;
                user.IsActive = entry.IsActive;
                user.LastDirectorySyncAtUtc = DateTime.UtcNow;
                updated++;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return new DirectorySyncResult(created, updated, 0);
    }

    private bool IsBootstrapAdmin(string name) => options.Value.BootstrapAdminNames.Any(x => string.Equals(x.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
}

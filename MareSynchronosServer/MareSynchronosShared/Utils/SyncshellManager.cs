using System.Security.Cryptography;
using MareSynchronosServer.Hubs;
using MareSynchronosShared.Data;
using MareSynchronosShared.Models;
using MareSynchronosShared.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShoninSync.API.Dto.Group;

namespace MareSynchronosServer.Utils;

public class SyncshellManager
{
    private readonly IDbContextFactory<MareDbContext> _dbContextFactory;
    private readonly ILogger<SyncshellManager> _logger;
    private readonly PairDataFetcher _pairDataFetcher;

    public SyncshellManager(
        IDbContextFactory<MareDbContext> dbContextFactory,
        ILogger<SyncshellManager> logger,
        PairDataFetcher pairDataFetcher
        )
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _pairDataFetcher = pairDataFetcher;
    }

    public async Task JoinSyncshell(string gid, string uid)
    {
        using var db = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var aliasOrGid = gid;
        var group = await db.Groups.Include(g => g.Owner).AsNoTracking().SingleOrDefaultAsync(g => g.GID == aliasOrGid || g.Alias == aliasOrGid).ConfigureAwait(false);
        var groupGid = group?.GID ?? string.Empty;
        var existingPair = await db.GroupPairs.AsNoTracking().SingleOrDefaultAsync(g => g.GroupGID == groupGid && g.GroupUserUID == uid).ConfigureAwait(false);
        var existingUserCount = await db.GroupPairs.AsNoTracking().CountAsync(g => g.GroupGID == groupGid).ConfigureAwait(false);
        var joinedGroups = await db.GroupPairs.CountAsync(g => g.GroupUserUID == uid).ConfigureAwait(false);
        var isBanned = await db.GroupBans.AnyAsync(g => g.GroupGID == groupGid && g.BannedUserUID == uid).ConfigureAwait(false);
        
        // get all pairs before we join
        var allUserPairs = await _pairDataFetcher.GetAllPairInfo(uid).ConfigureAwait(false);
        
        GroupPair newPair = new()
        {
            GroupGID = group.GID,
            GroupUserUID = uid,
        };

        var preferredPermissions = await db.GroupPairPreferredPermissions.SingleOrDefaultAsync(u => u.UserUID == uid && u.GroupGID == group.GID).ConfigureAwait(false);
        if (preferredPermissions == null)
        {
            GroupPairPreferredPermission newPerms = new()
            {
                GroupGID = group.GID,
                UserUID = uid,
                DisableSounds = false,
                DisableVFX = false,
                DisableAnimations = false,
                IsPaused = false
            };

            db.Add(newPerms);
            preferredPermissions = newPerms;
        }
        else
        {
            preferredPermissions.DisableSounds = false;
            preferredPermissions.DisableVFX = false;
            preferredPermissions.DisableAnimations = false;
            preferredPermissions.IsPaused = false;
            db.Update(preferredPermissions);
        }

        await db.GroupPairs.AddAsync(newPair).ConfigureAwait(false);
        await db.SaveChangesAsync().ConfigureAwait(false);
        
        var self = db.Users.Single(u => u.UID == uid);
        var groupPairs = await db.GroupPairs.Include(p => p.GroupUser)
            .Where(p => p.GroupGID == group.GID && p.GroupUserUID != uid).ToListAsync().ConfigureAwait(false);
        var userPairsAfterJoin = await _pairDataFetcher.GetAllPairInfo(uid).ConfigureAwait(false);
        
        foreach (var pair in groupPairs)
        {
            var perms = userPairsAfterJoin.TryGetValue(pair.GroupUserUID, out var userinfo);
            // check if we have had prior permissions to that pair, if not add them
            var ownPermissionsToOther = userinfo?.OwnPermissions ?? null;
            if (ownPermissionsToOther == null)
            {
                var existingPermissionsOnDb = await db.Permissions.SingleOrDefaultAsync(p => p.UserUID == uid && p.OtherUserUID == pair.GroupUserUID).ConfigureAwait(false);

                if (existingPermissionsOnDb == null)
                {
                    ownPermissionsToOther = new()
                    {
                        UserUID = uid,
                        OtherUserUID = pair.GroupUserUID,
                        DisableAnimations = preferredPermissions.DisableAnimations,
                        DisableSounds = preferredPermissions.DisableSounds,
                        DisableVFX = preferredPermissions.DisableVFX,
                        IsPaused = preferredPermissions.IsPaused,
                        Sticky = false
                    };

                    await db.Permissions.AddAsync(ownPermissionsToOther).ConfigureAwait(false);
                }
                else
                {
                    existingPermissionsOnDb.DisableAnimations = preferredPermissions.DisableAnimations;
                    existingPermissionsOnDb.DisableSounds = preferredPermissions.DisableSounds;
                    existingPermissionsOnDb.DisableVFX = preferredPermissions.DisableVFX;
                    existingPermissionsOnDb.IsPaused = false;
                    existingPermissionsOnDb.Sticky = false;

                    db.Update(existingPermissionsOnDb);

                    ownPermissionsToOther = existingPermissionsOnDb;
                }
            }
            else if (!ownPermissionsToOther.Sticky)
            {
                ownPermissionsToOther = await db.Permissions.SingleAsync(u => u.UserUID == uid && u.OtherUserUID == pair.GroupUserUID).ConfigureAwait(false);

                // update the existing permission only if it was not set to sticky
                ownPermissionsToOther.DisableAnimations = preferredPermissions.DisableAnimations;
                ownPermissionsToOther.DisableVFX = preferredPermissions.DisableVFX;
                ownPermissionsToOther.DisableSounds = preferredPermissions.DisableSounds;
                ownPermissionsToOther.IsPaused = false;

                db.Update(ownPermissionsToOther);
            }

            // get others permissionset to self and eventually update it
            var otherPermissionToSelf = userinfo?.OtherPermissions ?? null;
            if (otherPermissionToSelf == null)
            {
                var otherExistingPermsOnDb = await db.Permissions.SingleOrDefaultAsync(p => p.UserUID == pair.GroupUserUID && p.OtherUserUID == uid).ConfigureAwait(false);

                if (otherExistingPermsOnDb == null)
                {
                    var otherPreferred = await db.GroupPairPreferredPermissions.SingleAsync(u => u.GroupGID == group.GID && u.UserUID == pair.GroupUserUID).ConfigureAwait(false);
                    otherExistingPermsOnDb = new()
                    {
                        UserUID = pair.GroupUserUID,
                        OtherUserUID = uid,
                        DisableAnimations = otherPreferred.DisableAnimations,
                        DisableSounds = otherPreferred.DisableSounds,
                        DisableVFX = otherPreferred.DisableVFX,
                        IsPaused = otherPreferred.IsPaused,
                        Sticky = false
                    };

                    await db.AddAsync(otherExistingPermsOnDb).ConfigureAwait(false);
                }
                else if (!otherExistingPermsOnDb.Sticky)
                {
                    var otherPreferred = await db.GroupPairPreferredPermissions.SingleAsync(u => u.GroupGID == group.GID && u.UserUID == pair.GroupUserUID).ConfigureAwait(false);
                    otherExistingPermsOnDb.DisableAnimations = otherPreferred.DisableAnimations;
                    otherExistingPermsOnDb.DisableSounds = otherPreferred.DisableSounds;
                    otherExistingPermsOnDb.DisableVFX = otherPreferred.DisableVFX;
                    otherExistingPermsOnDb.IsPaused = otherPreferred.IsPaused;

                    db.Update(otherExistingPermsOnDb);
                }

                otherPermissionToSelf = otherExistingPermsOnDb;
            }
            else if (!otherPermissionToSelf.Sticky)
            {
                var otherPreferred = await db.GroupPairPreferredPermissions.SingleAsync(u => u.GroupGID == group.GID && u.UserUID == pair.GroupUserUID).ConfigureAwait(false);
                otherPermissionToSelf.DisableAnimations = otherPreferred.DisableAnimations;
                otherPermissionToSelf.DisableSounds = otherPreferred.DisableSounds;
                otherPermissionToSelf.DisableVFX = otherPreferred.DisableVFX;
                otherPermissionToSelf.IsPaused = otherPreferred.IsPaused;

                db.Update(otherPermissionToSelf);
            }
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
    }
    
    public static async Task<CreatedSyncshell> CreateSyncshell(MareDbContext dbContext, string userUid, CancellationToken cancellationToken = default, string vanityId = null, string pw = null)
    {
        var gid = StringUtils.GenerateRandomString(12);
        while (await dbContext.Groups.AnyAsync(g => g.GID == "MSS-" + gid, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            gid = StringUtils.GenerateRandomString(12);
        }
        gid = "MSS-" + gid;

        var passwd = pw ?? StringUtils.GenerateRandomString(16);
        using var sha = SHA256.Create();
        var hashedPw = StringUtils.Sha256String(passwd);

        UserDefaultPreferredPermission defaultPermissions = await dbContext.UserDefaultPreferredPermissions.SingleAsync(u => u.UserUID == userUid, cancellationToken: cancellationToken).ConfigureAwait(false);

        Group newGroup = new()
        {
            GID = gid,
            Alias = vanityId,
            HashedPassword = hashedPw,
            InvitesEnabled = true,
            OwnerUID = userUid,
            PreferDisableAnimations = defaultPermissions.DisableGroupAnimations,
            PreferDisableSounds = defaultPermissions.DisableGroupSounds,
            PreferDisableVFX = defaultPermissions.DisableGroupVFX
        };

        GroupPair initialPair = new()
        {
            GroupGID = newGroup.GID,
            GroupUserUID = userUid,
            IsPinned = true,
        };

        GroupPairPreferredPermission initialPrefPermissions = new()
        {
            UserUID = userUid,
            GroupGID = newGroup.GID,
            DisableSounds = defaultPermissions.DisableGroupSounds,
            DisableAnimations = defaultPermissions.DisableGroupAnimations,
            DisableVFX = defaultPermissions.DisableGroupAnimations
        };

        await dbContext.Groups.AddAsync(newGroup, cancellationToken).ConfigureAwait(false);
        await dbContext.GroupPairs.AddAsync(initialPair, cancellationToken).ConfigureAwait(false);
        await dbContext.GroupPairPreferredPermissions.AddAsync(initialPrefPermissions, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CreatedSyncshell(newGroup, initialPrefPermissions, initialPair, passwd, gid);
    }
    
    public record CreatedSyncshell(Group NewGroup, GroupPairPreferredPermission InitialPrefPermissions, GroupPair InitialPair, string Passwd, string GID);
}
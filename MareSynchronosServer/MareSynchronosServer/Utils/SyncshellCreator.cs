using System.Security.Cryptography;
using MareSynchronosShared.Data;
using MareSynchronosShared.Models;
using MareSynchronosShared.Utils;
using Microsoft.EntityFrameworkCore;

namespace MareSynchronosServer.Utils;

public static class SyncshellCreator
{
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
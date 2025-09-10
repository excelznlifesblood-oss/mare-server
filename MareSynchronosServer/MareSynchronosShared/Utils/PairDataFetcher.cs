using MareSynchronosShared.Data;
using MareSynchronosShared.Models;
using Microsoft.EntityFrameworkCore;
using ShoninSync.API.Data;

namespace MareSynchronosServer.Hubs;

public class PairDataFetcher
{
    private readonly IDbContextFactory<MareDbContext> _dbContextFactory;

    public PairDataFetcher(IDbContextFactory<MareDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<Dictionary<string, UserInfo>> GetAllPairInfo(string uid)
    {
        using var db = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var clientPairs = from cp in db.ClientPairs.AsNoTracking().Where(u => u.UserUID == uid)
            join cp2 in db.ClientPairs.AsNoTracking().Where(u => u.OtherUserUID == uid)
                on new
                {
                    UserUID = cp.UserUID,
                    OtherUserUID = cp.OtherUserUID
                }
                equals new
                {
                    UserUID = cp2.OtherUserUID,
                    OtherUserUID = cp2.UserUID
                } into joined
            from c in joined.DefaultIfEmpty()
            where cp.UserUID == uid
            select new
            {
                UserUID = cp.UserUID,
                OtherUserUID = cp.OtherUserUID,
                Gid = string.Empty,
                Synced = c != null
            };


        var groupPairs = from gp in db.GroupPairs.AsNoTracking().Where(u => u.GroupUserUID == uid)
            join gp2 in db.GroupPairs.AsNoTracking().Where(u => u.GroupUserUID != uid)
                on new
                {
                    GID = gp.GroupGID
                }
                equals new
                {
                    GID = gp2.GroupGID
                }
            select new
            {
                UserUID = gp.GroupUserUID,
                OtherUserUID = gp2.GroupUserUID,
                Gid = Convert.ToString(gp2.GroupGID),
                Synced = true
            };

        var allPairs = clientPairs.Concat(groupPairs);

        var result = from user in allPairs
            join u in db.Users.AsNoTracking() on user.OtherUserUID equals u.UID
            join o in db.Permissions.AsNoTracking().Where(u => u.UserUID == uid)
                on new { UserUID = user.UserUID, OtherUserUID = user.OtherUserUID }
                equals new { UserUID = o.UserUID, OtherUserUID = o.OtherUserUID }
                into ownperms
            from ownperm in ownperms.DefaultIfEmpty()
            join p in db.Permissions.AsNoTracking().Where(u => u.OtherUserUID == uid)
                on new { UserUID = user.OtherUserUID, OtherUserUID = user.UserUID }
                equals new { UserUID = p.UserUID, OtherUserUID = p.OtherUserUID }
                into otherperms
            from otherperm in otherperms.DefaultIfEmpty()
            where user.UserUID == uid
                  && u.UID == user.OtherUserUID
                  && ownperm.UserUID == user.UserUID && ownperm.OtherUserUID == user.OtherUserUID
                  && (otherperm == null || (otherperm.OtherUserUID == user.UserUID && otherperm.UserUID == user.OtherUserUID))
            select new
            {
                UserUID = user.UserUID,
                OtherUserUID = user.OtherUserUID,
                OtherUserAlias = u.Alias,
                GID = user.Gid,
                Synced = user.Synced,
                OwnPermissions = ownperm,
                OtherPermissions = otherperm
            };

        var resultList = await result.AsNoTracking().ToListAsync().ConfigureAwait(false);
        return resultList.GroupBy(g => g.OtherUserUID, StringComparer.Ordinal).ToDictionary(g => g.Key, g =>
        {
            return new UserInfo(g.First().OtherUserAlias,
                g.SingleOrDefault(p => string.IsNullOrEmpty(p.GID))?.Synced ?? false,
                g.Max(p => p.Synced),
                g.Select(p => string.IsNullOrEmpty(p.GID) ? Constants.IndividualKeyword : p.GID).ToList(),
                g.First().OwnPermissions,
                g.First().OtherPermissions);
        }, StringComparer.Ordinal);
    }
}
using MareSynchronosServer.Utils;
using MareSynchronosShared.Data;
using MareSynchronosShared.Services;
using MareSynchronosShared.Utils.Configuration;
using Microsoft.EntityFrameworkCore;

namespace MareSynchronosServer.Services;

public class CommunitySyncshellAutoGenerate: BackgroundService
{
    private readonly ILogger<CommunitySyncshellAutoGenerate> _logger;
    private readonly IDbContextFactory<MareDbContext> _dbContextFactory;
    private readonly IConfigurationService<ServerConfiguration> _configurationService;

    public CommunitySyncshellAutoGenerate(
        ILogger<CommunitySyncshellAutoGenerate> logger,
        IDbContextFactory<MareDbContext> dbContextFactory,
        IConfigurationService<ServerConfiguration> configurationService
        )
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _configurationService = configurationService;
    }
    
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Chara Data Cleanup Service started");
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var db = await _dbContextFactory.CreateDbContextAsync(stoppingToken).ConfigureAwait(false);
            var configs = _configurationService.GetValueOrDefault<IList<CommunitySyncshellConfig>>(
                nameof(ServerConfiguration.CommunitySyncshellConfigs),
                new List<CommunitySyncshellConfig>());
            var vanityIds = configs.Select(x => x.VanityId).ToList();
            var groups = await db.Groups.Where(x => vanityIds.Contains(x.Alias)).ToListAsync(stoppingToken).ConfigureAwait(false);
            var adminUser = await db.Users.FirstOrDefaultAsync(x => x.IsAdmin, cancellationToken: stoppingToken).ConfigureAwait(false);

            if (adminUser != null)
            {
                foreach (var config in configs)
                {
                    var group = groups.FirstOrDefault(x => x.Alias.Equals(config.VanityId, StringComparison.OrdinalIgnoreCase));
                    if (group == null)
                    {
                        //We need to add a new syncshell.
                        var (newGroup, initialPrefPermissions, initialPair, passwd, gid) = await SyncshellManager
                            .CreateSyncshell(db, adminUser.UID, stoppingToken, config.VanityId, config.PW).ConfigureAwait(false);
                    }
                }
            }
            
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken).ConfigureAwait(false);
        }
    }
}
using MareSynchronosServer.Hubs;
using MareSynchronosShared.Data;
using MareSynchronosShared.Services;
using MareSynchronosShared.Utils;
using MareSynchronosShared.Utils.Configuration;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ShoninSync.API.SignalR;

namespace MareSynchronosServer.Services;

public class TemporaryUserExpiryService: BackgroundService
{
    private readonly ILogger<TemporaryUserExpiryService> _logger;
    private readonly IDbContextFactory<MareDbContext> _dbContextFactory;
    private readonly IHubContext<MareHub, IMareHub> _hubContext;
    private readonly IConfigurationService<ServerConfiguration> _mareClientConfigurationService;

    public TemporaryUserExpiryService(
        ILogger<TemporaryUserExpiryService> logger,
        IDbContextFactory<MareDbContext> dbContextFactory,
        IHubContext<MareHub, IMareHub> hubContext,
        IConfigurationService<ServerConfiguration> mareClientConfigurationService)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _hubContext = hubContext;
        _mareClientConfigurationService = mareClientConfigurationService;
    }
    
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Temporary User Expiration Service started");
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Temporary User Expiration Service is running at: {time}", DateTimeOffset.Now);
            using var db = await _dbContextFactory.CreateDbContextAsync(stoppingToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var users = await db.Users
                .Where(x => x.IsLimitedUser && x.LimitedUserExpiry.Value <= now)
                .ToListAsync(stoppingToken).ConfigureAwait(false);
            foreach (var user in users)
            {
                var maxGroupsByUser = _mareClientConfigurationService.GetValueOrDefault(nameof(ServerConfiguration.MaxGroupUserCount), 3);
                await SharedDbFunctions.PurgeUser(_logger, user, db, maxGroupsByUser).ConfigureAwait(false);
                await _hubContext.Clients.User(user.UID).Client_ExpireTemporaryUser(user.UID).ConfigureAwait(false);
            }
            
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
using MareSynchronosShared.Utils;

namespace MareSynchronosServices.Tools;

public class MessageDispatcherInitializer: IHostedService
{
    private readonly IMessageDispatcher _dispatcher;

    public MessageDispatcherInitializer(IMessageDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _dispatcher.Initialize().ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
using System.Text.Json;
using System.Text.Json.Serialization;
using MareSynchronosServer.Hubs;
using MareSynchronosShared.Services;
using MareSynchronosShared.Utils;
using MareSynchronosShared.Utils.Configuration;
using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ShoninSync.API.Dto.Group;
using ShoninSync.API.Dto.User;
using ShoninSync.API.SignalR;
using StackExchange.Redis.Extensions.Core.Abstractions;

namespace MareSynchronosServer.Services;

public class DiscordBotCommunicationReciever: BackgroundService
{
    private readonly IConfigurationService<ServerConfiguration> _serverConfiguration;
    private readonly IHubContext<MareHub, IMareHub> _hubContext;
    private readonly IRedisDatabase _redis;
    private IConnection _connection;
    private IChannel _clientGroupSendFullInfoChannel;
    
    public DiscordBotCommunicationReciever(
        IConfigurationService<ServerConfiguration> serverConfiguration, 
        IHubContext<MareHub, IMareHub> hubContext,
        IRedisDatabase redis
    )
    {
        _serverConfiguration = serverConfiguration;
        _hubContext = hubContext;
        _redis = redis;
    }



    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = _serverConfiguration.GetValue<RabbitMQConfiguration>(nameof(ServerConfiguration.RabbitMQ));
        var factory = new ConnectionFactory
        {
            UserName = config.User,
            Password = config.Password,
            VirtualHost = config.Vhost,
            HostName = config.Hostname
        };
        _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        _clientGroupSendFullInfoChannel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await _clientGroupSendFullInfoChannel.ExchangeDeclareAsync(MessageDispatcherConstants.EXCHANGE_NAME, ExchangeType.Direct, cancellationToken: cancellationToken).ConfigureAwait(false);
        await _clientGroupSendFullInfoChannel.QueueDeclareAsync(MessageDispatcherConstants.QUEUE_NAME, false, false, false, null, cancellationToken: cancellationToken).ConfigureAwait(false);
        await _clientGroupSendFullInfoChannel.QueueBindAsync(queue: MessageDispatcherConstants.QUEUE_NAME, MessageDispatcherConstants.EXCHANGE_NAME, "Client_GroupSendFullInfo", cancellationToken: cancellationToken).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }
    
    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
                var groupSendInfoConsumer = new AsyncEventingBasicConsumer(_clientGroupSendFullInfoChannel);
        groupSendInfoConsumer.ReceivedAsync += async (sender, args) =>
        {
            var body = args.Body.ToArray();
            var json = System.Text.Encoding.UTF8.GetString(body);
            var messages = JsonSerializer.Deserialize<List<Message>>(json);

            foreach (var message in messages)
            {
                switch (message.Type)
                {
                    case AsynchronousSignalROperation.SendGroupFullInfo:
                        var fullInfoDto =
                            JsonSerializer.Deserialize<MessageDispatchDetails<GroupFullInfoDto>>(
                                message.Payload);
                        await _hubContext.Clients.User(fullInfoDto.UserUID).Client_GroupSendFullInfo(fullInfoDto.Dto)
                            .ConfigureAwait(false);
                        break;
                    case AsynchronousSignalROperation.SendGroupPairJoined:
                        var groupPairJoinedDto = JsonSerializer.Deserialize<MessageDispatchDetails<GroupPairFullInfoDto>>(message.Payload);
                        await _hubContext.Clients.User(groupPairJoinedDto.UserUID).Client_GroupPairJoined(groupPairJoinedDto.Dto)
                            .ConfigureAwait(false);
                        break;
                    case AsynchronousSignalROperation.SendOnlineUserNotifications:
                        var dto = JsonSerializer.Deserialize<MessageDispatchDetails<OnlineUserNotificationDto>>(message.Payload);
                        var groupUserIdent = await GetUserIdent(dto.Dto.PairUID).ConfigureAwait(false);
                        var selfUserIdent = await GetUserIdent(dto.Dto.UserUID).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(groupUserIdent) && !string.IsNullOrEmpty(selfUserIdent))
                        {
                            await _hubContext.Clients.User(dto.UserUID)
                                .Client_UserSendOnline(new(dto.Dto.Pair, groupUserIdent)).ConfigureAwait(false);

                            await _hubContext.Clients.User(dto.UserUID)
                                .Client_UserSendOnline(new(dto.Dto.Self, selfUserIdent)).ConfigureAwait(false);
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            await _clientGroupSendFullInfoChannel.BasicAckAsync(args.DeliveryTag, false, stoppingToken).ConfigureAwait(false);
        };
        await _clientGroupSendFullInfoChannel.BasicConsumeAsync(MessageDispatcherConstants.QUEUE_NAME, false, groupSendInfoConsumer, cancellationToken: stoppingToken).ConfigureAwait(false);
  
    }
    
    private async Task<string> GetUserIdent(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return string.Empty;
        return await _redis.GetAsync<string>("UID:" + uid).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _connection?.Dispose();
        _clientGroupSendFullInfoChannel?.Dispose();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
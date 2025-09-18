using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json;
using MareSynchronosShared.Services;
using MareSynchronosShared.Utils;
using RabbitMQ.Client;
using ShoninSync.API.Dto.Group;
using ShoninSync.API.Dto.User;

namespace MareSynchronosServices.Tools;

public class MessageDispatcher: IDisposable, IMessageDispatcher
{
    private readonly IConnectionFactory _connectionFactory;
    private IConnection _connection;

    public MessageDispatcher(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Called automatically by a hosted service. You don't need to call this.
    /// </summary>
    public async Task Initialize()
    {
        _connection ??= await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
    }
    

    public async Task DispatchMessages(List<Message> messages)
    {
        var json = JsonSerializer.Serialize(messages);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        using var channel = await _connection.CreateChannelAsync().ConfigureAwait(false);
        await channel.ExchangeDeclareAsync(MessageDispatcherConstants.EXCHANGE_NAME, ExchangeType.Direct).ConfigureAwait(false);
        await channel.QueueDeclareAsync(MessageDispatcherConstants.QUEUE_NAME, false, false, false, null).ConfigureAwait(false);
        await channel.QueueBindAsync(queue: MessageDispatcherConstants.QUEUE_NAME, MessageDispatcherConstants.EXCHANGE_NAME, MessageDispatcherConstants.ROUTING_KEY).ConfigureAwait(false);
        var props = new BasicProperties();
        await channel.BasicPublishAsync(MessageDispatcherConstants.EXCHANGE_NAME, MessageDispatcherConstants.ROUTING_KEY, true, props, bytes).ConfigureAwait(false);
    }


    
    

    public void Dispose()
    {
        if (_connection != null)
        {
            _connection.Dispose();
        }
    }
}
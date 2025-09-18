using ShoninSync.API.Data;
using ShoninSync.API.Dto.Group;
using ShoninSync.API.Dto.User;

namespace MareSynchronosShared.Utils;

public interface IMessageDispatcher
{
    public Task Initialize();
    public Task DispatchMessages(List<Message> messages);
}


public class DummyMessageDispatcher: IMessageDispatcher
{
    public Task Initialize()
    {
        return Task.CompletedTask;
    }

    public Task DispatchMessages(List<Message> messages)
    {
        return Task.CompletedTask;
    }
}

public class MessageDispatchDetails<T>
{
    public T Dto { get; set; }
    public string UserUID { get; set; }
    public string GroupGID { get; set; }
}

public class OnlineUserNotificationDto
{
    public string UserUID { get; set; }
    public string PairUID { get; set; }
    public UserData Self { get; set; }
    public UserData Pair { get; set; }
}

public class Message
{
    public AsynchronousSignalROperation Type { get; set; }
    public string Payload { get; set; }
}

public enum AsynchronousSignalROperation
{
    SendGroupFullInfo,
    SendGroupPairJoined,
    SendOnlineUserNotifications,
}
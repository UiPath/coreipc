namespace UiPath.Ipc.Tests;

public interface IStudioOperations : IStudioAgentOperations
{
    Task<bool> SetOffline(bool value);
}

public interface IStudioAgentOperations
{
    Task<RobotInfo> GetRobotInfoCore(StudioAgentMessage message, CancellationToken ct = default);
}

public interface IStudioEvents
{
    Task OnRobotInfoChanged(RobotInfoChangedArgs args);
}

public class RobotInfo
{
    public bool Offline { get; set; }
}

public sealed class RobotInfoChangedArgs
{
    public required RobotInfo LatestInfo { get; init; }
}

public class StudioAgentMessage : ClientProcessMessage
{
    public Guid MasterJobId { get; set; }
}

public class ClientProcessMessage : Message
{
    public int MockClientPid { get; set; } = 123;
}
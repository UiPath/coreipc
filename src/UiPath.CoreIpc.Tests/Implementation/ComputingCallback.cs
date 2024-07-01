namespace UiPath.Ipc.Tests;

public interface IComputingCallback
{
    Task<string> GetId(Message message);
    Task<string> GetThreadName();
}
public class ComputingCallback : IComputingCallback
{
    public string Id { get; set; }
    public async Task<string> GetId(Message message)
    {
        message.Client.ShouldBeNull();
        return Id;
    }

    public async Task<string> GetThreadName() => Thread.CurrentThread.Name;
}
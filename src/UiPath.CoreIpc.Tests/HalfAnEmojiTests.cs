namespace UiPath.Ipc.Tests;

public interface IHalfAnEmojiProbe
{
    Task<bool> Throw();
    Task<bool> ThrowWithData();
    Task<string> Return();
    Task<bool> Receive(string text);
}

public sealed class HalfAnEmojiProbe : IHalfAnEmojiProbe
{
    // Static readonly, so it can't go in [InlineData]: the compiler stores attribute strings as UTF-8, where the halves would already be U+FFFD.
    public static readonly string Text = "whole \ud83d\ude00, lone low \ude00, lone high at the end \ud83d";
    public static readonly string Replaced = "whole \ud83d\ude00, lone low \ufffd, lone high at the end \ufffd";
    // Sent by default, so the server needs no setting.
    public const string DataKey = "UiPath.ErrorInfo.Error";

    public string? Received { get; private set; }

    public Task<bool> Throw() => throw new InvalidOperationException(Text);

    public Task<bool> ThrowWithData()
    {
        var exception = new InvalidOperationException("data");
        exception.Data[DataKey] = Text;
        throw exception;
    }

    public Task<string> Return() => Task.FromResult(Text);

    public Task<bool> Receive(string text)
    {
        Received = text;
        return Task.FromResult(true);
    }
}

public sealed class HalfAnEmojiTests
{
    [Fact]
    public async Task Thrown_TheCallerGetsTheError_WithTheHalvesReplaced()
    {
        await using var pair = await CreatePair();
        var ex = await pair.Proxy.Throw().ShouldThrowAsync<RemoteException>();
        ex.Message.ShouldBe(HalfAnEmojiProbe.Replaced);
    }

    [Fact]
    public async Task Returned_TheCallerGetsTheResult_WithTheHalvesReplaced()
    {
        await using var pair = await CreatePair();
        (await pair.Proxy.Return()).ShouldBe(HalfAnEmojiProbe.Replaced);
    }

    [Fact]
    public async Task InExceptionData_TheCallerGetsTheValue_WithTheHalvesReplaced()
    {
        await using var pair = await CreatePair();
        var ex = await pair.Proxy.ThrowWithData().ShouldThrowAsync<RemoteException>();
        ex.Data[HalfAnEmojiProbe.DataKey].ShouldBe(HalfAnEmojiProbe.Replaced);
    }

    [Fact]
    public async Task InAnArgument_TheServiceGetsIt_WithTheHalvesReplaced()
    {
        var service = new HalfAnEmojiProbe();
        await using var pair = await CreatePair(service);
        (await pair.Proxy.Receive(HalfAnEmojiProbe.Text)).ShouldBeTrue();
        service.Received.ShouldBe(HalfAnEmojiProbe.Replaced);
    }

    private static Task<IpcPair<IHalfAnEmojiProbe>> CreatePair(HalfAnEmojiProbe? service = null)
        => IpcPair<IHalfAnEmojiProbe>.Create(service ?? new HalfAnEmojiProbe());
}

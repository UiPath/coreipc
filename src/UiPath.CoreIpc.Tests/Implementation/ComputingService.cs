using Microsoft.Extensions.Logging;

namespace UiPath.CoreIpc.Tests;

public interface IInvalid : IDisposable
{
}

public interface IDuplicateMessage
{
    Task Test(Message message1, Message message2);
}

public interface IUploadNotification
{
    Task Upload(Stream stream);
}

public interface IDerivedStreamDownload
{
    Task<MemoryStream> Download();
}

public interface IDuplicateStreams
{
    Task<bool> Upload(Stream stream, Stream stream2);
}

public interface IDerivedStreamUpload
{
    Task<bool> Upload(MemoryStream stream);
}

public interface IMessageFirst
{
    Task Test(Message message1, int x);
}

public interface IInvalidCancellationToken
{
    Task Test(CancellationToken token, int x);
}

public interface IComputingServiceBase
{
    Task<float> AddFloat(float x, float y, CancellationToken cancellationToken = default);
}
public interface IComputingService : IComputingServiceBase
{
    Task<ComplexNumber> AddComplexNumber(ComplexNumber x, ComplexNumber y, CancellationToken cancellationToken = default);
    Task<ComplexNumber> AddComplexNumbers(IEnumerable<ComplexNumber> numbers, CancellationToken cancellationToken = default);
    Task<string> SendMessage(SystemMessage message, CancellationToken cancellationToken = default);
    Task<bool> Infinite(CancellationToken cancellationToken = default);
    Task InfiniteVoid(CancellationToken cancellationToken = default);
    Task<string> GetCallbackThreadName(Message message = null, CancellationToken cancellationToken = default);
}

public struct ComplexNumber
{
    public float A { get; set; }
    public float B { get; set; }

    public ComplexNumber(float a, float b)
    {
        A = a;
        B = b;
    }
}

public enum TextStyle
{
    TitleCase,
    Upper
}

public class ConvertTextArgs
{
    public TextStyle TextStyle { get; set; } = TextStyle.Upper;

    public string Text { get; set; } = string.Empty;
}

public class ComputingService : IComputingService
{
    private readonly ILogger<ComputingService> _logger;

    public ComputingService(ILogger<ComputingService> logger) // inject dependencies in constructor
    {
        _logger = logger;
    }

    public async Task<ComplexNumber> AddComplexNumber(ComplexNumber x, ComplexNumber y, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"{nameof(AddComplexNumber)} called.");
        return new ComplexNumber(x.A + y.A, x.B + y.B);
    }

    public async Task<ComplexNumber> AddComplexNumbers(IEnumerable<ComplexNumber> numbers, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"{nameof(AddComplexNumbers)} called.");
        var result = new ComplexNumber(0, 0);
        foreach (ComplexNumber number in numbers)
        {
            result = new ComplexNumber(result.A + number.A, result.B + number.B);
        }
        return result;
    }

    public async Task<float> AddFloat(float x, float y, CancellationToken cancellationToken = default)
    {
        //Trace.WriteLine($"{nameof(AddFloat)} called.");
        _logger.LogInformation($"{nameof(AddFloat)} called.");
        return x + y;
    }

    public async Task<bool> Infinite(CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return true;
    }

    public Task InfiniteVoid(CancellationToken cancellationToken = default) =>Task.Delay(Timeout.Infinite, cancellationToken);

    public async Task<string> SendMessage(SystemMessage message, CancellationToken cancellationToken = default)
    {
        await Task.Delay(message.Delay, cancellationToken);
        var client = message.Client;
        var callback = message.GetCallback<IComputingCallback>();
        var clientId = await callback.GetId(message);
        string returnValue = "";
        client.Impersonate(() => returnValue = client.GetUserName() + "_" + clientId + "_" + message.Text);
        return returnValue;
    }

    public async Task<string> GetCallbackThreadName(Message message, CancellationToken cancellationToken = default) => await message.GetCallback<IComputingCallback>().GetThreadName();
}
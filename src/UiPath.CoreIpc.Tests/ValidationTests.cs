using MessagePack;

namespace UiPath.CoreIpc.Tests;

public class ValidationTests
{
    class JobFailedException : Exception
    {
        public JobFailedException(Error error) : base("Job has failed.", new RemoteException(error))
        {
        }
    }
    static MessagePackSerializerOptions Contractless = MessagePack.Resolvers.ContractlessStandardResolver.Options;
    static byte[] Serialize<T>(T value) => MessagePackSerializer.Serialize(value, Contractless);
    static T Deserialize<T>(byte[] data) => MessagePackSerializer.Deserialize<T>(data, Contractless);
    void Roundtrip<T>(T value) => Deserialize<T>(Serialize(value)).ShouldBe(value);
    [Fact]
    public void Request()
    {
        Request request = new("endpoint", 42, "method", 43);
        Roundtrip(request);
    }
    [Fact]
    public void Response()
    {
        Response response = new(42, new("message", "stack", "type", new("innerMessage", "innerStack", "innerType", null)));
        Roundtrip(response);
    }
    [Fact]
    public void ErrorFromRemoteException()
    {
        var innerError = new InvalidDataException("invalid").ToError();
        var error = new JobFailedException(innerError).ToError();
        error.Type.ShouldBe(typeof(JobFailedException).FullName);
        error.Message.ShouldBe("Job has failed.");
        error.InnerError.Type.ShouldBe(typeof(InvalidDataException).FullName);
        error.InnerError.Message.ShouldBe("invalid");
    }
#if DEBUG
    [Fact]
    public void MethodsMustReturnTask() => new Action(() => new NamedPipeClientBuilder<IInvalid>("").ValidateAndBuild()).ShouldThrow<ArgumentException>().Message.ShouldStartWith("Method does not return Task!");
    [Fact]
    public void DuplicateMessageParameters() => new Action(() => new NamedPipeClientBuilder<IDuplicateMessage>("").ValidateAndBuild()).ShouldThrow<ArgumentException>().Message.ShouldStartWith("The message must be the last parameter before the cancellation token!");
    [Fact]
    public void TheMessageMustBeTheLastBeforeTheToken() => new Action(() => new NamedPipeClientBuilder<IMessageFirst>("").ValidateAndBuild()).ShouldThrow<ArgumentException>().Message.ShouldStartWith("The message must be the last parameter before the cancellation token!");
    [Fact]
    public void CancellationTokenMustBeLast() => new Action(() => new NamedPipeClientBuilder<IInvalidCancellationToken>("").ValidateAndBuild()).ShouldThrow<ArgumentException>().Message.ShouldStartWith("The CancellationToken parameter must be the last!");
    [Fact]
    public void UploadMustReturn() => new Action(() => new NamedPipeClientBuilder<IUploadNotification>("").ValidateAndBuild()).ShouldThrow<ArgumentException>().Message.ShouldStartWith("Upload methods must return a value!");
    [Fact]
    public void DuplicateStreams() => new Action(() => new NamedPipeClientBuilder<IDuplicateStreams>("").ValidateAndBuild()).ShouldThrow<ArgumentException>().Message.ShouldStartWith("Only one Stream parameter is allowed!");
    [Fact]
    public void UploadDerivedStream() => new Action(() => new NamedPipeClientBuilder<IDerivedStreamUpload>("").ValidateAndBuild()).ShouldThrow<ArgumentException>().Message.ShouldStartWith("Stream parameters must be typed as Stream!");
    [Fact]
    public void DownloadDerivedStream() => new Action(() => new NamedPipeClientBuilder<IDerivedStreamDownload>("").ValidateAndBuild()).ShouldThrow<ArgumentException>().Message.ShouldStartWith("Stream parameters must be typed as Stream!");
    [Fact]
    public void TheCallbackContractMustBeAnInterface() => new Action(() => new NamedPipeClientBuilder<ISystemService, ValidationTests>("", IpcHelpers.ConfigureServices()).ValidateAndBuild()).ShouldThrow<ArgumentOutOfRangeException>().Message.ShouldStartWith("The contract must be an interface!");
    [Fact]
    public void TheServiceContractMustBeAnInterface() => new Action(() => new ServiceHostBuilder(IpcHelpers.ConfigureServices()).AddEndpoint<ValidationTests>().ValidateAndBuild()).ShouldThrow<ArgumentOutOfRangeException>().Message.ShouldStartWith("The contract must be an interface!");
#endif
}
namespace UiPath.Ipc.Tests;

public class ValidationTests
{
    class JobFailedException : Exception
    {
        public JobFailedException(Error error) : base("Job has failed.", new RemoteException(error))
        {
        }
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
    [Fact]
    public void SerializeDefaultValueToString() => new IpcJsonSerializer().Serialize(new Message<int>(0)).ShouldBe("{\"Payload\":0}");
    [Fact]
    public void SerializeNullToString() => new IpcJsonSerializer().Serialize(new Message<string>(null)).ShouldBe("{\"Payload\":null}");
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
    public void TheCallbackContractMustBeAnInterface()
    {
        var action = () =>
        {
            Callback.Set<ValidationTests>(IpcHelpers.ConfigureServices());
            _ = new NamedPipeClientBuilder<ISystemService>(pipeName: "").ValidateAndBuild();
        };
        
        action
            .ShouldThrow<ArgumentOutOfRangeException>()
            .Message.ShouldStartWith("The contract must be an interface!");
    }

    [Fact]
    public void TheServiceContractMustBeAnInterface() => new Action(() => new ServiceHostBuilder(IpcHelpers.ConfigureServices()).AddEndpoint<ValidationTests>().ValidateAndBuild()).ShouldThrow<ArgumentOutOfRangeException>().Message.ShouldStartWith("The contract must be an interface!");
#endif
}
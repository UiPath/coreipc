using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace UiPath.Ipc.Tests;

public interface ISystemService
{
    Task FireAndForget(CancellationToken cancellationToken = default);
    Task VoidThreadName(CancellationToken cancellationToken = default);
    Task VoidSyncThrow(CancellationToken cancellationToken = default);
    Task<string> GetThreadName(CancellationToken cancellationToken = default);
    Task<string> ConvertText(string text, TextStyle style, CancellationToken cancellationToken = default);
    Task<string> ConvertTextWithArgs(ConvertTextArgs args, CancellationToken cancellationToken = default);
    Task<Guid> EchoGuid(Guid guid, CancellationToken cancellationToken = default);
    Task<byte[]> ReverseBytes(byte[] input, CancellationToken cancellationToken = default);
    Task<bool> SlowOperation(CancellationToken cancellationToken = default);
    Task<string> UnexpectedCallback(SystemMessage message, CancellationToken cancellationToken = default);
    Task<bool> Infinite(Message? message = null, CancellationToken cancellationToken = default);
    Task<string> ImpersonateCaller(Message message = null, CancellationToken cancellationToken = default);
    Task<string> SendMessage(SystemMessage message, CancellationToken cancellationToken = default);
    Task<string> Upload(Stream stream, int delay = 0, CancellationToken cancellationToken = default);
    Task<Stream> Download(string text, CancellationToken cancellationToken = default);
    Task<Stream> Echo(Stream input, CancellationToken cancellationToken = default);
    Task<string> UploadNoRead(Stream memoryStream, int delay = 0, CancellationToken cancellationToken = default);
    Task<bool> CancelIoPipe(CancelIoPipeMessage message = null, CancellationToken cancellationToken = default);
    Task<bool> Delay(int delay = 0, CancellationToken cancellationToken = default);
}

public interface IUnexpectedCallback
{
    Task<string> Call();
}

public class SystemMessage : Message
{
    public string Text { get; set; }
    public int Delay { get; set; }
}
public class CancelIoPipeMessage : Message
{
    public int[]? MsDelays { get; set; }
}
public class SystemService : ISystemService
{
    public SystemService()
    {
    }

    public async Task<bool> Infinite(Message? message = null, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return true;
    }
    public async Task<string> ConvertTextWithArgs(ConvertTextArgs args, CancellationToken cancellationToken = default)
        => await ConvertText(args.Text, args.TextStyle, cancellationToken);

    public async Task<string> ConvertText(string text, TextStyle style, CancellationToken cancellationToken = default)
    {
        switch (style)
        {
            case TextStyle.TitleCase:
                return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text);
            case TextStyle.Upper:
                return CultureInfo.InvariantCulture.TextInfo.ToUpper(text);
            default:
                return text;
        }
    }

    public async Task<string> SendMessage(SystemMessage message, CancellationToken cancellationToken = default)
    {
        var client = message.Client;
        var callback = message.GetCallback<ISystemCallback>();
        var clientId = await callback.GetId(message);
        string returnValue = "";
        client.Impersonate(() => returnValue = client.GetUserName() + "_" + clientId + "_" + message.Text);
        return returnValue;
    }

    public bool FireAndForgetDone { get; set; }

    public async Task FireAndForget(CancellationToken cancellationToken = default)
    {
        const int Timeout =
#if CI
            400;
#else
            40;
#endif
        await Task.Delay(Timeout);
        FireAndForgetDone = true;
    }

    public async Task<Guid> EchoGuid(Guid guid, CancellationToken cancellationToken = default)
    {
        return guid;
    }

    public async Task<byte[]> ReverseBytes(byte[] input, CancellationToken cancellationToken = default)
    {
        return input.Reverse().ToArray();
    }

    public async Task<string> UnexpectedCallback(SystemMessage message, CancellationToken cancellationToken = default)
    {
        if (message.Delay != 0)
        {
            await Task.Delay(message.Delay, cancellationToken);
        }

        return await message.GetCallback<IUnexpectedCallback>().Call();        
    }

    public async Task<bool> SlowOperation(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("SlowOperation " + Thread.CurrentThread.Name);
        try
        {
            for(int i = 0; i < 5; i++)
            {
                await Task.Delay(1000, cancellationToken);
                Console.WriteLine("SlowOperation "+Thread.CurrentThread.Name);
                if(cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("SlowOperation Cancelled.");
                    return false;
                }
            }
        }
        catch(Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
        Console.WriteLine("SlowOperation finished. "+ (cancellationToken.IsCancellationRequested ? "cancelled " : "") + Thread.CurrentThread.Name);
        return true;
    }

    public string ThreadName;

    public Task VoidSyncThrow(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public async Task VoidThreadName(CancellationToken cancellationToken = default) => ThreadName = Thread.CurrentThread.Name;

    public async Task<string> GetThreadName(CancellationToken cancellationToken = default) => Thread.CurrentThread.Name;

    public async Task<string> ImpersonateCaller(Message message = null, CancellationToken cancellationToken = default)
    {
        var client = message.Client;
        string returnValue = "";
        client.Impersonate(() => returnValue = client.GetUserName());
        return returnValue;
    }

    public async Task<string> Upload(Stream stream, int delay = 0, CancellationToken cancellationToken = default)
    {
        await Task.Delay(delay);
        return await new StreamReader(stream).ReadToEndAsync();
    }

    public async Task<string> UploadNoRead(Stream stream, int delay = 0, CancellationToken cancellationToken = default)
    {
        await Task.Delay(delay);
        return "";
    }

    public async Task<Stream> Download(string text, CancellationToken cancellationToken = default) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    public async Task<Stream> Echo(Stream input, CancellationToken cancellationToken = default)
    {
        var result = new MemoryStream();
        await input.CopyToAsync(result);
        result.Position = 0;
        return result;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CancelIoEx(IntPtr handle, IntPtr lpOverlapped);
    public async Task<bool> CancelIoPipe(CancelIoPipeMessage message = null, CancellationToken cancellationToken = default)
    {
        Debug.WriteLine("###################### CancelIoPipe");
        await Task.Delay(50);
#if WINDOWS
            var pipeStream = (message.Client as ServerConnection).Connection.Network as PipeStream;
            var canceled = CancelIoEx(pipeStream.SafePipeHandle.DangerousGetHandle(), IntPtr.Zero);
            
            foreach (var msDelay in message.MsDelays ?? [])
            {
                await Task.Delay(msDelay);
                canceled = CancelIoEx(pipeStream.SafePipeHandle.DangerousGetHandle(), IntPtr.Zero);
            }

            await Task.Delay(50);
            return canceled;
#else
        return false;
#endif
    }

    public async Task<bool> Delay(int delay = 0, CancellationToken cancellationToken = default)
    {
        await Task.Delay(delay, cancellationToken);
        return true;
    }
}
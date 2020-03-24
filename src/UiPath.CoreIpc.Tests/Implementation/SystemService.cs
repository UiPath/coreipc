using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UiPath.CoreIpc;

namespace UiPath.CoreIpc.Tests
{
    public class SystemService : ISystemService
    {
        public SystemService()
        {
        }

        public Task Infinite(CancellationToken cancellationToken = default) => Task.Delay(Timeout.Infinite, cancellationToken);

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

        public bool DidNothing { get; set; }

        public async Task<OneWay> DoNothing(CancellationToken cancellationToken = default)
        {
            await Task.Delay(1);
            DidNothing = true;
            return OneWay.Instance;
        }

        public async Task<Guid> GetGuid(Guid guid, CancellationToken cancellationToken = default)
        {
            //throw new Exception("sssss");
            return guid;
        }

        public async Task<byte[]> ReverseBytes(byte[] input, CancellationToken cancellationToken = default)
        {
            return input.Reverse().ToArray();
        }

        public string MessageText;

        public async Task<string> MissingCallback(SystemMessage message, CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(message.Delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                MessageText = message.Text;
                throw;
            }
            var domainName = "";
            var client = message.Client;
            //client.RunAs(() => domainName = "test");
            //try
            //{
                message.GetCallback<IDisposable>();
            //}
            //catch(Exception ex)
            //{
            //    Console.WriteLine(ex.ToString());
            //}
            return client.GetUserName() +" " + domainName;
        }

        public async Task SlowOperation(CancellationToken cancellationToken = default)
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
                    }
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            Console.WriteLine("SlowOperation finished. "+ (cancellationToken.IsCancellationRequested ? "cancelled " : "") + Thread.CurrentThread.Name);
        }

        public string ThreadName;

        public Task<OneWay> VoidSyncThrow(CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public async Task<OneWay> VoidThreadName(CancellationToken cancellationToken = default)
        {
            ThreadName = Thread.CurrentThread.Name;
            return OneWay.Instance;
        }

        public async Task<string> GetThreadName(CancellationToken cancellationToken = default) => Thread.CurrentThread.Name;

        public async Task<string> ImpersonateCaller(Message message = null, CancellationToken cancellationToken = default)
        {
            var client = message.Client;
            string returnValue = "";
            client.Impersonate(() => returnValue = client.GetUserName());
            return returnValue;
        }
    }
}
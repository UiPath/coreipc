using System.Text;
using System.Text.Json;
using System.Diagnostics;
using UiPath.CoreIpc.NamedPipe;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers;
using System.Text.Json.Serialization;

namespace UiPath.CoreIpc.Tests;
class Client
{
    static List<AddressDto> _addressDtos = new();
    static Response _response = new() { Data = _addressDtos };
    static IpcJsonSerializer Serializer = new();
    static MemoryStream _stream= new();
    static JsonSerializerOptions _options = new() { Converters = { new JsonDocumentConverter() } };
    struct Response
    {
        public object Data { get; set; }
    }
    static void SystemTextJson()
    {
        _stream.Position = 0;
        JsonSerializer.Serialize(_stream, _response);
        _stream.SetLength(_stream.Position);
        _stream.Position = 0;
        var response = JsonSerializer.Deserialize<Response>(_stream, _options);
        using var element = (JsonDocument)response.Data;
        var obj = element.Deserialize<List<AddressDto>>();
    }
    static void WithNewtonsoft()
    {
        _stream.Position = 0;
        Serializer.Serialize(_response, _stream);
        _stream.SetLength(_stream.Position);
        _stream.Position = 0;
        var response = Serializer.Deserialize<Response>(_stream);
        var obj = ((Newtonsoft.Json.Linq.JToken)response.Data).ToObject<List<AddressDto>>();
    }
    static async Task Main(string[] args)
    {
        for (int index = 0; index < 1000; ++index)
        {
            _addressDtos.Add(new AddressDto
            {
                Id = index,
                City = Guid.NewGuid().ToString(),
                Number = index,
                Country = Guid.NewGuid().ToString(),
                ZipCode = Guid.NewGuid().ToString(),
            });
        }
        SystemTextJson();
        WithNewtonsoft();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        while (true)
        {
            var stopWatch = Stopwatch.StartNew();
            for (int index = 0; index < 200; ++index)
            {
                //WithNewtonsoft();
                SystemTextJson();
            }
            stopWatch.Stop();
            var gcStats = GC.GetGCMemoryInfo();
            Console.WriteLine(stopWatch.ElapsedMilliseconds + "  "+ gcStats.PauseTimePercentage);
        }
        return;
        Console.WriteLine(typeof(int).Assembly);
        Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
        var source = new CancellationTokenSource();
        try
        {
            await await Task.WhenAny(RunTestsAsync(source.Token), Task.Run(() =>
            {
                Console.ReadLine();
                Console.WriteLine("Cancelling...");
                source.Cancel();
            }));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
        Console.ReadLine();
    }
    private static async Task RunTestsAsync(CancellationToken cancellationToken)
    {
        var serviceProvider = ConfigureServices();

        var systemClient =
            new NamedPipeClientBuilder<ISystemService>("test")
            //.SerializeParametersAsObjects()
            .RequestTimeout(TimeSpan.FromSeconds(12))
            .Logger(serviceProvider)
            .AllowImpersonation()
            .ValidateAndBuild();
        while (true)
        {
            var watch = Stopwatch.StartNew();
            JobResult jobResult;
            for (int i = 0; i < 60; i++)
            {
                jobResult = await systemClient.GetJobResult();
            }
            watch.Stop();
            var gcStats = GC.GetGCMemoryInfo();
            Console.WriteLine($"{watch.ElapsedMilliseconds} {gcStats.GenerationInfo[2].SizeAfterBytes/1_000_000}  {gcStats.PauseTimePercentage}");
        }
    }

    private static IServiceProvider ConfigureServices() =>
        new ServiceCollection()
            .AddIpcWithLogging()
            .BuildServiceProvider();
    sealed class JsonDocumentConverter : JsonConverter<object>
    {
        public sealed override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => JsonDocument.ParseValue(ref reader);
        public sealed override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) => throw new NotImplementedException();
    }
}
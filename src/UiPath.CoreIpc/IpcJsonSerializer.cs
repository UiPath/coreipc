using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading.Tasks;
namespace UiPath.CoreIpc
{
    public interface ISerializer
    {
        Task<T> DeserializeAsync<T>(Stream json);
        object Deserialize(object json, Type type);
        void Serialize(object obj, Stream stream);
        string Serialize(object obj);
        object Deserialize(string json, Type type);
    }
    class IpcJsonSerializer : ISerializer, IArrayPool<char>
    {
        static readonly JsonLoadSettings LoadSettings = new(){ LineInfoHandling = LineInfoHandling.Ignore };
        static readonly JsonSerializer DefaultSerializer = CreateDefaultSerializer();
        private static JsonSerializer CreateDefaultSerializer()
        {
            var serializer = JsonSerializer.CreateDefault();
            serializer.DefaultValueHandling = DefaultValueHandling.Ignore;
            serializer.NullValueHandling = NullValueHandling.Ignore;
            serializer.PreserveReferencesHandling = PreserveReferencesHandling.None;
            return serializer;
        }
        public async Task<T> DeserializeAsync<T>(Stream json)
        {
            JToken jToken;
            var streamReader = new StreamReader(json, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: Math.Min(4096, (int)json.Length));
            using (var reader = new JsonTextReader(streamReader) { ArrayPool = this })
            {
                jToken = await JToken.LoadAsync(reader, LoadSettings);
            }
            return jToken.ToObject<T>(DefaultSerializer);
        }
        public object Deserialize(object json, Type type) => json switch
        {
            JToken token => token.ToObject(type, DefaultSerializer),
            {} => type.IsAssignableFrom(json.GetType()) ? json : JToken.FromObject(json, DefaultSerializer).ToObject(type, DefaultSerializer),
            null => null,
        };
        public void Serialize(object obj, Stream stream)
        {
            using var writer = new JsonTextWriter(new StreamWriter(stream)) { ArrayPool = this, CloseOutput = false };
            DefaultSerializer.Serialize(writer, obj);
            writer.Flush();
        }
        public char[] Rent(int minimumLength) => ArrayPool<char>.Shared.Rent(minimumLength);
        public void Return(char[] array) => ArrayPool<char>.Shared.Return(array);
        public string Serialize(object obj) => JsonConvert.SerializeObject(obj);
        public object Deserialize(string json, Type type) => JsonConvert.DeserializeObject(json, type);
    }
}
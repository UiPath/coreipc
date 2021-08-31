using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Buffers;
using System.IO;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    public interface ISerializer
    {
        Task<T> DeserializeAsync<T>(Stream json);
        object Deserialize(string json, Type type);
        object Deserialize(object json, Type type);
        string Serialize(object obj);
        void Serialize(object obj, Stream stream);
    }
    class IpcJsonSerializer : ISerializer
    {
        /// <summary>
        /// The <see cref="char"/> array pool to use for each <see cref="JsonTextReader"/> instance.
        /// </summary>
        static readonly IArrayPool<char> JsonCharArrayPool = new JsonArrayPool<char>(ArrayPool<char>.Shared);
        public object Deserialize(string json, Type type) => JsonConvert.DeserializeObject(json, type);
        public async Task<T> DeserializeAsync<T>(Stream json)
        {
            var reader = new JsonTextReader(new StreamReader(json)) { ArrayPool = JsonCharArrayPool };
            var jToken = await JToken.LoadAsync(reader);
            return jToken.ToObject<T>();
        }
        public object Deserialize(object json, Type type) => json switch
        {
            JToken token => token.ToObject(type),
            {} => type.IsAssignableFrom(json.GetType()) ? json : JToken.FromObject(json).ToObject(type),
            null => null,
        };
        public string Serialize(object obj) => JsonConvert.SerializeObject(obj);
        public void Serialize(object obj, Stream stream)
        {
            var serializer = JsonSerializer.CreateDefault();
            var writer = new JsonTextWriter(new StreamWriter(stream)) { ArrayPool = JsonCharArrayPool };
            serializer.Serialize(writer, obj);
            writer.Flush();
        }
        class JsonArrayPool<T> : IArrayPool<T>
        {
            private readonly ArrayPool<T> _arrayPool;
            internal JsonArrayPool(ArrayPool<T> arrayPool) => _arrayPool = arrayPool ?? throw new ArgumentNullException(nameof(arrayPool));
            public T[] Rent(int minimumLength) => _arrayPool.Rent(minimumLength);
            public void Return(T[] array) => _arrayPool.Return(array ?? throw new ArgumentNullException(nameof(array)));
        }
    }
}
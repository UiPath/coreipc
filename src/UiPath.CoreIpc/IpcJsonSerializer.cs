using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
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
        public object Deserialize(string json, Type type) => JsonConvert.DeserializeObject(json, type);
        public async Task<T> DeserializeAsync<T>(Stream json)
        {
            var reader = new JsonTextReader(new StreamReader(json));
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
            var streamWriter = new StreamWriter(stream);
            serializer.Serialize(streamWriter, obj);
            streamWriter.Flush();
        }
    }
}
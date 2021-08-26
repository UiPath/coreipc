using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace UiPath.CoreIpc
{
    public interface ISerializer
    {
        object Deserialize(Stream json, Type type);
        object Deserialize(string json, Type type);
        object Deserialize(object json, Type type);
        string Serialize(object obj);
        void Serialize(object obj, Stream stream);
    }
    class IpcJsonSerializer : ISerializer
    {
        public object Deserialize(string json, Type type) => JsonConvert.DeserializeObject(json, type);
        public object Deserialize(Stream json, Type type)
        {
            var serializer = JsonSerializer.CreateDefault();
            var streamReader = new StreamReader(json);
            return serializer.Deserialize(streamReader, type);
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
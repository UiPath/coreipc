using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    public interface ISerializer
    {
        object Deserialize(Stream json, Type type);
        object Deserialize(string json, Type type);
        string Serialize(object obj);
        Task Serialize(object obj, Stream stream);
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
        public string Serialize(object obj) => JsonConvert.SerializeObject(obj);
        public Task Serialize(object obj, Stream stream)
        {
            var serializer = JsonSerializer.CreateDefault();
            var streamWriter = new StreamWriter(stream);
            serializer.Serialize(streamWriter, obj);
            return streamWriter.FlushAsync();
        }
    }
}
using Newtonsoft.Json;
using System;

namespace UiPath.CoreIpc
{
    public interface ISerializer
    {
        object Deserialize(string json, Type type);
        string Serialize(object obj);
    }
    class JsonSerializer : ISerializer
    {
        public object Deserialize(string json, Type type) => JsonConvert.DeserializeObject(json, type);
        public string Serialize(object obj) => JsonConvert.SerializeObject(obj);
    }
}
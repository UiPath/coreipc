using Newtonsoft.Json;
using System;
using System.Threading;

namespace UiPath.CoreIpc
{
    public interface ISerializer
    {
        object Deserialize(string json, Type type);
        string Serialize(object obj);
    }
    class JsonSerializer : ISerializer
    {
        public object Deserialize(string json, Type type)
        {
            if(type == typeof(CancellationToken))
            {
                return CancellationToken.None;
            }
            return JsonConvert.DeserializeObject(json, type);
        }

        public string Serialize(object obj)
        {
            if(obj is CancellationToken)
            {
                return "";
            }
            return JsonConvert.SerializeObject(obj);
        }
    }
}
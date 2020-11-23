using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;

namespace UiPath.CoreIpc
{
    public interface ISerializer
    {
        object Deserialize(string json, Type type);
        string Serialize(object obj);
    }

    public class JsonSerializer : ISerializer
    {
        public object Deserialize(string json, Type type)
        {
            if(type == typeof(CancellationToken))
            {
                return CancellationToken.None;
            }

            if (type == typeof(Version))
            {
                return DeserializeVersion(json);
            }

            return JsonConvert.DeserializeObject(json, type);
        }

        public string Serialize(object obj)
        {
            if(obj is CancellationToken)
            {
                return "";
            }

            if (obj is Version)
            {
                return JsonConvert.SerializeObject(obj, new VersionConverter());
            }

            return JsonConvert.SerializeObject(obj);
        }

        private object DeserializeVersion(string json)
        {
            var type = typeof(Version);
            try
            {
                return JsonConvert.DeserializeObject(json, type);
            }
            catch (JsonSerializationException)
            {
                return JsonConvert.DeserializeObject(json, type, new VersionConverter());
            }
        }
    }

    internal class VersionConverter : JsonConverter<Version>
    {
        public override Version ReadJson(JsonReader reader, Type objectType, Version existingValue, bool hasExistingValue, Newtonsoft.Json.JsonSerializer serializer)
        {
            var dict = serializer.Deserialize<Dictionary<string, int>>(reader);
            dict.TryGetValue("Build", out int build);
            dict.TryGetValue("Revision", out int revision);
            return new Version(dict["Major"], dict["Minor"], build, revision);
        }

        public override void WriteJson(JsonWriter writer, Version value, Newtonsoft.Json.JsonSerializer serializer)
        {
            serializer.Serialize(writer, value == null ? "null" : value.ToString());
        }
    }
}
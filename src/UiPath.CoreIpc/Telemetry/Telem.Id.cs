using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;

namespace UiPath.Ipc;

partial class Telemetry
{
    [JsonConverter(typeof(JsonConverter))]
    public abstract class Id
    {
        private string _value = null!;
        public required string Value
        {
            get => _value;
            init => _value = value;
        }

        public override string ToString() => Value;

        protected class JsonConverter : Newtonsoft.Json.JsonConverter
        {
            public override bool CanConvert(Type objectType)
            => objectType == typeof(string) || typeof(Id).IsAssignableFrom(objectType);

            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            {
                switch (value)
                {
                    case null:
                        writer.WriteValue(null as string);
                        break;
                    case Id id:
                        writer.WriteValue(id.Value);
                        break;
                    default:
                        throw new InvalidCastException();
                }
            }

            public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            {
                if (!typeof(Id).IsAssignableFrom(objectType))
                {
                    throw new InvalidCastException();
                }

                if (reader.Value is null)
                {
                    return null;
                }

                if (reader.Value is not string underlyingValue)
                {
                    throw new FormatException();
                }

                if (objectType == typeof(Id))
                {
                    return new UntypedId(underlyingValue);
                }

                var result = (System.Runtime.Serialization.FormatterServices.GetUninitializedObject(objectType) as Id)!;
                result._value = underlyingValue;
                return result;
            }
        }
    }

    public sealed class UntypedId : Id
    {
        [SetsRequiredMembers]
        public UntypedId(string value) => Value = value;
    }

    //[JsonConverter(typeof(JsonConverter))]
    public sealed class Id<TRecord> : Id where TRecord : RecordBase
    {
        [return: NotNullIfNotNull(nameof(value))]
        public static implicit operator Id<TRecord>?(string? value) => value is null ? null : new(value);
        [return: NotNullIfNotNull(nameof(id))]
        public static implicit operator string?(Id<TRecord>? id) => id?.Value;

        [SetsRequiredMembers]
        public Id(string value) => Value = value;
    }
}

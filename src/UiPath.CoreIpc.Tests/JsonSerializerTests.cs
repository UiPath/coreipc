using System;
using Xunit;

namespace UiPath.CoreIpc.Tests
{
    public class JsonSerializerTests
    {
        private readonly JsonSerializer _jsonSerializer;

        public JsonSerializerTests()
        {
            _jsonSerializer = new JsonSerializer();
        }

        [Fact]
        public void Deserialize_SystemVersionJson()
        {
            const string JsonVersion1 = "\"1.2.3.65537\"";
            Version version1 = (Version)_jsonSerializer.Deserialize(JsonVersion1, typeof(Version));
            Assert.Equal(1, version1.Major);
            Assert.Equal(2, version1.Minor);
            Assert.Equal(3, version1.Build);
            Assert.Equal(65537, version1.Revision);
            Assert.Equal(1, version1.MajorRevision);
            Assert.Equal(1, version1.MinorRevision);

            const string JsonVersion2 = "{\"Major\":1,\"Minor\":2,\"Build\":3,\"Revision\":65537}";
            Version version2 = (Version)_jsonSerializer.Deserialize(JsonVersion2, typeof(Version));
            Assert.Equal(1, version2.Major);
            Assert.Equal(2, version2.Minor);
            Assert.Equal(3, version2.Build);
            Assert.Equal(65537, version2.Revision);
            Assert.Equal(1, version2.MajorRevision);
            Assert.Equal(1, version2.MinorRevision);
        }

        [Fact]
        public void Deserialize_SystemVersionJsonWithMajorMinorRevision_DiscardsMajorMinorRevision()
        {
            const string JsonVersion = "{\"Major\":1,\"Minor\":2,\"Build\":3,\"Revision\":4,\"MajorRevision\":9,\"MinorRevision\":8}";
            Version version = (Version)_jsonSerializer.Deserialize(JsonVersion, typeof(Version));
            Assert.Equal(1, version.Major);
            Assert.Equal(2, version.Minor);
            Assert.Equal(3, version.Build);
            Assert.Equal(4, version.Revision);
            Assert.Equal(0, version.MajorRevision);
            Assert.Equal(4, version.MinorRevision);
        }

        [Fact]
        public void Deserialize_SystemVersionJsonWithoutBuildRevision()
        {
            const string JsonVersion = "{\"Major\":2,\"Minor\":3}";
            Version version = (Version)_jsonSerializer.Deserialize(JsonVersion, typeof(Version));
            Assert.Equal(2, version.Major);
            Assert.Equal(3, version.Minor);
            Assert.Equal(0, version.Build);
            Assert.Equal(0, version.Revision);
            Assert.Equal(0, version.MajorRevision);
            Assert.Equal(0, version.MinorRevision);
        }

        [Fact]
        public void Deserialize_SystemVersionJsonWithoutRevision()
        {
            const string JsonVersion = "{\"Major\":1,\"Minor\":2,\"Build\":3}";
            Version version = (Version)_jsonSerializer.Deserialize(JsonVersion, typeof(Version));
            Assert.Equal(1, version.Major);
            Assert.Equal(2, version.Minor);
            Assert.Equal(3, version.Build);
            Assert.Equal(0, version.Revision);
            Assert.Equal(0, version.MajorRevision);
            Assert.Equal(0, version.MinorRevision);
        }

        [Fact]
        public void Serialize_SystemVersion()
        {
            Version version = new Version(1, 2, 3, 65537);
            string jsonVersion = _jsonSerializer.Serialize(version);
            Assert.Equal("\"1.2.3.65537\"", jsonVersion);
        }

        [Fact]
        public void Serialize_SystemVersionNull()
        {
            Version version = null;
            string jsonVersion = _jsonSerializer.Serialize(version);
            Assert.Equal("null", jsonVersion);
        }

        [Fact]
        public void SerializeDeserialize_SystemVersion()
        {
            Version version = new Version(1, 2, 3, 65537);
            Assert.Equal(version, (Version)_jsonSerializer.Deserialize(_jsonSerializer.Serialize(version), typeof(Version)));

            string jsonVersion1 = "{\"Major\":1,\"Minor\":2,\"Build\":3,\"Revision\":65537,\"MajorRevision\":1,\"MinorRevision\":1}";
            string jsonVersion2 = "\"1.2.3.65537\"";
            Assert.Equal(jsonVersion2, _jsonSerializer.Serialize(_jsonSerializer.Deserialize(jsonVersion1, typeof(Version))));
        }
    }
}
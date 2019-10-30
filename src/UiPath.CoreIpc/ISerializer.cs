using System;

namespace UiPath.CoreIpc
{
    public interface ISerializer
    {
        object Deserialize(string json, Type type);
        string Serialize(object obj);
    }
}
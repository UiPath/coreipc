using System;

namespace UiPath.Ipc
{
    public interface ISerializer
    {
        object Deserialize(string json, Type type);
        string Serialize(object obj);
    }
}
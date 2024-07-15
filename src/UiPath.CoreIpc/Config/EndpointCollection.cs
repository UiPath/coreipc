using System.Collections;

namespace UiPath.Ipc;

public class EndpointCollection : IEnumerable, IEnumerable<KeyValuePair<Type, object?>>
{
    internal readonly Dictionary<Type, object?> Endpoints = new();

    public void Add(Type type) => Add(type, instance: null);
    public void Add(Type type, object? instance)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (instance is not null && !instance.GetType().IsAssignableTo(type)) throw new ArgumentOutOfRangeException(nameof(instance));
        Endpoints[type] = instance;
    }

    public IEnumerator<KeyValuePair<Type, object?>> GetEnumerator() => Endpoints.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

using System.Collections;

namespace UiPath.Ipc;

public class EndpointCollection : IEnumerable, IEnumerable<EndpointSettings>
{
    internal readonly Dictionary<Type, EndpointSettings> Endpoints = new();

    public void Add(Type type) => Add(type, instance: null);
    public void Add(Type contractType, object? instance) => Add(new EndpointSettings(contractType, instance));
    public void Add(EndpointSettings endpointSettings)
    {
        if (endpointSettings is null) throw new ArgumentNullException(nameof(endpointSettings));
        endpointSettings.Validate();

        Endpoints[endpointSettings.Service.Type] = endpointSettings;
    }
    public IEnumerator<EndpointSettings> GetEnumerator() => Endpoints.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

using System.Collections;

namespace UiPath.Ipc;

public class ContractCollection : IEnumerable<ContractSettings>
{
    internal readonly Dictionary<Type, ContractSettings> Endpoints = new();

    public void Add(Type contractType) => Add(contractType, instance: null);
    public void Add(Type contractType, object? instance) => Add(new ContractSettings(contractType, instance));
    public void Add(ContractSettings endpointSettings)
    {
        if (endpointSettings is null) throw new ArgumentNullException(nameof(endpointSettings));

        Endpoints[endpointSettings.Service.Type] = endpointSettings;
    }
    public IEnumerator<ContractSettings> GetEnumerator() => Endpoints.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

using System.Collections;

namespace UiPath.Ipc;

internal sealed class HeaderCollection : IDictionary<string, string>, IReadOnlyDictionary<string, string>
{
    public static readonly IReadOnlyDictionary<string, string> Empty = new HeaderCollection(capacity: 0).Freeze();

    private readonly Dictionary<string, string> _innerMap;
    private volatile bool _frozen;

    public HeaderCollection() : this(capacity: 0) { }
    private HeaderCollection(int capacity) => _innerMap = new(capacity);

    public HeaderCollection Freeze()
    {
        _frozen = true;
        return this;
    }

    public string this[string key]
    {
        get => _innerMap[NormalizeKey(key)];
        set
        {
            AssertNotFrozen();
            _innerMap[NormalizeKey(key)] = value;
        }
    }

    public ICollection<string> Keys => _innerMap.Keys;

    public ICollection<string> Values => _innerMap.Values;

    public int Count => _innerMap.Count;

    public bool IsReadOnly => false;

    public void Add(string key, string value)
    {
        AssertNotFrozen();
        _innerMap.Add(NormalizeKey(key), value);
    }

    public void Add(KeyValuePair<string, string> item)
    {
        AssertNotFrozen();
        Add(NormalizeKey(item.Key), item.Value);
    }

    public void Clear()
    {
        AssertNotFrozen();
        _innerMap.Clear();
    }

    public bool Contains(KeyValuePair<string, string> item) => _innerMap.Contains(NormalizeKey(item));

    public bool ContainsKey(string key) => _innerMap.ContainsKey(NormalizeKey(key));

    public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex)
    {
        (_innerMap as IDictionary<string, string>).CopyTo(array, arrayIndex);
    }

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _innerMap.GetEnumerator();

    public bool Remove(string key) {
        AssertNotFrozen();
        return _innerMap.Remove(NormalizeKey(key));
    }

    public bool Remove(KeyValuePair<string, string> item)
    {
        AssertNotFrozen();
        return (_innerMap as IDictionary<string, string>).Remove(NormalizeKey(item));
    }

    public bool TryGetValue(string key, out string value) => _innerMap.TryGetValue(NormalizeKey(key), out value!);

    IEnumerable<string> IReadOnlyDictionary<string, string>.Keys => Keys;

    IEnumerable<string> IReadOnlyDictionary<string, string>.Values => Values;

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private KeyValuePair<string, string> NormalizeKey(KeyValuePair<string, string> item)
        => new(NormalizeKey(item.Key), item.Value);

    private string NormalizeKey(string key) =>
#if NETFRAMEWORK
        key?.All(char.IsLower) ?? throw new ArgumentNullException(nameof(key))
            ? key
            : key.ToLowerInvariant();
#else
        key?.ToLowerInvariant() ?? throw new ArgumentNullException(nameof(key));
#endif

    private void AssertNotFrozen()
    {
        if (_frozen)
        {
            throw new InvalidOperationException("HeaderCollection is frozen and cannot be modified.");
        }
    }
}

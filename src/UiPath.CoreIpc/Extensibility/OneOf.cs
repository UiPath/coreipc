using System.Diagnostics.CodeAnalysis;

namespace UiPath.Ipc.Extensibility;

public readonly struct OneOf<T1, T2> : IEquatable<OneOf<T1, T2>>
{
    public static implicit operator OneOf<T1, T2>(T1 t1) => new(t1);
    public static implicit operator OneOf<T1, T2>(T2 t2) => new(t2);

    public static implicit operator T1(OneOf<T1, T2> oneOf) => oneOf.As1;
    public static implicit operator T2(OneOf<T1, T2> oneOf) => oneOf.As2;

    public static bool operator ==(OneOf<T1, T2> left, OneOf<T1, T2> right) => left.Equals(right);
    public static bool operator !=(OneOf<T1, T2> left, OneOf<T1, T2> right) => !(left == right);

    private readonly T1 _t1;
    private readonly T2 _t2;
    private readonly int _hashCode;

    public bool Is1 { get; }
    public bool Is2 { get; }

    public T1 As1 => Is1 ? _t1 : throw new InvalidCastException();
    public T2 As2 => Is2 ? _t2 : throw new InvalidCastException();

    public Type ValueType => Is1 ? typeof(T1) : typeof(T2);
    public object? Value => Is1 ? _t1 : _t2;

    public OneOf(T1 t1)
    {
        _t1 = t1;
        _t2 = default!;
        Is1 = true;
        Is2 = false;
        _hashCode = (_t1, _t2, Is1, Is2).GetHashCode();
    }
    public OneOf(T2 t2)
    {
        _t1 = default!;
        _t2 = t2;
        Is1 = false;
        Is2 = true;
        _hashCode = (_t1, _t2, Is1, Is2).GetHashCode();
    }

    public bool Equals(OneOf<T1, T2> other) => Is1 == other.Is1 && Is2 == other.Is2 && EqualityComparer<T1>.Default.Equals(_t1, other._t1) && EqualityComparer<T2>.Default.Equals(_t2, other._t2);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is OneOf<T1, T2> other && Equals(other);
    public override int GetHashCode() => _hashCode;
}

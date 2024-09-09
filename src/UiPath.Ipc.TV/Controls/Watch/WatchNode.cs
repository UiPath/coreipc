using System.Collections;
using System.Reflection;
using UiPath.Ipc.TV.DataAccess;

namespace UiPath.Ipc.TV;

using static Telemetry;

internal sealed class WatchNode
{
    private static string ToCSharpString(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        _ => value.ToString()!
    };
    private static string ToCSharpType(Type type) => type switch
    {
        { } when type == typeof(string) => "string",
        { } when type == typeof(object) => "object",
        { } when type == typeof(bool) => "bool",
        { } when type == typeof(int) => "int",
        { } when type == typeof(short) => "short",
        { } when type == typeof(long) => "long",
        { } when type == typeof(nint) => "nint",

        { } when type == typeof(uint) => "uint",
        { } when type == typeof(ushort) => "ushort",
        { } when type == typeof(ulong) => "ulong",
        { } when type == typeof(nuint) => "nuint",

        { } when type == typeof(float) => "float",
        { } when type == typeof(double) => "double",
        { } when type == typeof(decimal) => "decimal",

        { } when type == typeof(byte) => "byte",
        { } when type == typeof(sbyte) => "sbyte",
        { } when type == typeof(char) => "char",
        { } when type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) => $"{ToCSharpType(type.GetGenericArguments()[0])}?",
        { } when type.IsArray => $"{ToCSharpType(type.GetElementType()!)}[]",
        { } when type.IsGenericType => $"{type.FullName!.Split('`')[0]}<{string.Join(", ", type.GetGenericArguments().Select(ToCSharpType))}>",
        _ => type!.FullName!
    };

    private readonly ValueSource _valueSource;
    private readonly Lazy<object?> _objectValue;
    private readonly Lazy<string> _value;
    private readonly Lazy<string> _type;
    private readonly Lazy<IReadOnlyList<WatchNode>> _children;

    public string Name => _valueSource.GetName();
    public string Value => _value.Value;
    public string Type => _type.Value;
    public string ImageKey => _valueSource.GetImageKey();
    public IReadOnlyList<WatchNode> Children => _children.Value;

    public object? ObjectValue => _objectValue.Value;

    public bool IsReference => _valueSource.IsReference(out _);
    public string? GetReference() => _valueSource.IsReference(out var id) ? id : null;

    public WatchNode(ValueSource valueSource)
    {
        _valueSource = valueSource;
        _objectValue = new(() => _valueSource.GetValue());
        _value = new(ComputeValue);
        _type = new(ComputeType);
        _children = new(ComputeChildren);
    }

    public string? GetActionName()
    {
        if (_valueSource.IsReference(out _))
        {
            return "Go To";
        }

        if (ObjectValue is string or ICollection)
        {
            return "View";
        }

        return null;
    }


    private string ComputeValue() => ToCSharpString(ObjectValue);
    private string ComputeType()
    {
        if (_valueSource is ValueSource.VirtualVariable virtualVariable)
        {
            return virtualVariable.CSharpTypeName;
        }

        var value = _valueSource.GetValue();
        var declaredType = _valueSource.GetDeclaredType();
        var propertyType = ToCSharpType(declaredType);

        if (value is null)
        {
            return propertyType;
        }

        var actualType = value.GetType();
        if (actualType == declaredType)
        {
            return propertyType;
        }

        return $"{propertyType} {{{ToCSharpType(actualType)}}}";
    }

    private IReadOnlyList<WatchNode> ComputeChildren()
    {
        var children = Enumerate();
        var augmented = AugmentChildren(children);
        return augmented.ToArray();

        IEnumerable<WatchNode> Enumerate()
        {
            if (ObjectValue is ExceptionInfo exceptionInfo)
            {
                yield return new(new ValueSource.VirtualProperty(nameof(Exception.Message), typeof(string), exceptionInfo.Message));
                yield return new(new ValueSource.VirtualProperty(nameof(Exception.StackTrace), typeof(string), exceptionInfo.StackTrace));
                yield return new(new ValueSource.VirtualProperty(nameof(Exception.InnerException), typeof(Exception), exceptionInfo.InnerException));
                yield break;
            }

            var type = _valueSource.GetDeclaredType();
            if (type == typeof(string)) { yield break; }

            if (ObjectValue is ICollection collection)
            {
                var elementType = collection.GetType().GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>))
                    .Select(i => i.GetGenericArguments()[0])
                    .FirstOrDefault() ?? typeof(object);

                for (int i = 0; i < collection.Count; i++)
                {
                    yield return new(new ValueSource.Index(collection, i, elementType));
                }
                yield break;
            }

            if (ObjectValue is not null)
            {
                var properties = ObjectValue.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                foreach (var property in properties)
                {
                    if (property.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    yield return new(new ValueSource.Property(ObjectValue, property));
                }
            }

            yield break;
        }
    }

    private IEnumerable<WatchNode> AugmentChildren(IEnumerable<WatchNode> children)
    {
        if (ObjectValue is Is<Effect> effect)
        {
            yield return new(new ValueSource.VirtualProperty("Cause", typeof(string), effect.Of?.Value) { Reference = true });
        }
        if (ObjectValue is Is<SubOperation> subOperation)
        {
            yield return new(new ValueSource.VirtualProperty("Parent", typeof(string), subOperation.Of?.Value) { Reference = true });
        }
        if (ObjectValue is Is<Modifier> modifier)
        {
            yield return new(new ValueSource.VirtualProperty("Modified", typeof(string), modifier.Of?.Value) { Reference = true });
        }

        if (ObjectValue is Is<Success> success)
        {
            yield return new(new ValueSource.VirtualProperty("Succeeded", typeof(string), success.Of?.Value) { Reference = true });        
        }

        if (ObjectValue is Is<Failure> failure)
        {
            yield return new(new ValueSource.VirtualProperty("Failed", typeof(string), failure.Of?.Value) { Reference = true });
        }

        if (ObjectValue is RecordEntity entity)
        {
            yield return new(new ValueSource.VirtualProperty("$deserialization", typeof(RecordBase), entity.GetTelemetryRecord()));
        }

        foreach (var child in children)
        {
            yield return child;
        }
    }
}


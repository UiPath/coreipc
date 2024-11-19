namespace UiPath.Ipc.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class OverrideConfigAttribute : Attribute
{
    public Type OverrideConfigType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OverrideConfigAttribute"/> class.
    /// </summary>
    /// <param name="overrideConfigType">A <c>typeof</c> expression that indicates a concrete subclass of <see cref="OverrideConfig"/>, with a public, parameterless constructor.</param>
    public OverrideConfigAttribute(Type overrideConfigType)
    {
        if (overrideConfigType is null)
        {
            throw new ArgumentNullException(nameof(overrideConfigType));
        }
        if (overrideConfigType.IsAbstract)
        {
            throw new ArgumentException($"The type {overrideConfigType} is abstract.", nameof(overrideConfigType));
        }
        if (!typeof(OverrideConfig).IsAssignableFrom(overrideConfigType))
        {
            throw new ArgumentException($"The type {overrideConfigType} does not inherit from {typeof(OverrideConfig)}.", nameof(overrideConfigType));
        }
        if (overrideConfigType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, Type.DefaultBinder, Type.EmptyTypes, modifiers: null) is null)
        {
            throw new ArgumentException($"The type {overrideConfigType} does not have a public, parameterless constructor.", nameof(overrideConfigType));
        }

        OverrideConfigType = overrideConfigType;
    }
}

public abstract class OverrideConfig
{
    public virtual Task<ServerTransport?> Override(Func<Task<ServerTransport>> listener) => listener()!;
    public virtual IpcClient? Override(Func<IpcClient> client) => client();
}
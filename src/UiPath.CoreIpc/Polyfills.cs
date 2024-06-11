#if NETFRAMEWORK

namespace System.Diagnostics.CodeAnalysis;

using static AttributeTargets;

[ExcludeFromCodeCoverage]
[DebuggerNonUserCode]
[AttributeUsage(Parameter | Property | ReturnValue, AllowMultiple = true)]
internal sealed class NotNullIfNotNullAttribute : Attribute
{
    /// <summary>
    ///   Gets the associated parameter name.
    ///   The output will be non-<see langword="null"/> if the argument to the
    ///   parameter specified is non-<see langword="null"/>.
    /// </summary>
    public string ParameterName { get; }

    /// <summary>
    ///   Initializes the attribute with the associated parameter name.
    /// </summary>
    /// <param name="parameterName">
    ///   The associated parameter name.
    ///   The output will be non-<see langword="null"/> if the argument to the
    ///   parameter specified is non-<see langword="null"/>.
    /// </param>
    public NotNullIfNotNullAttribute(string parameterName) =>
        ParameterName = parameterName;
}
#endif
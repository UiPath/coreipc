namespace System.Runtime.CompilerServices;

/// <summary>
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Field | System.AttributeTargets.Property | System.AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class RequiredMemberAttribute : Attribute { }

/// <summary>
/// Indicates that compiler support for a particular feature is required for the location where this attribute is applied.
/// </summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
public // polyfill!
sealed class CompilerFeatureRequiredAttribute : Attribute
{
    /// <summary>
    /// The <see cref="FeatureName"/> used for the ref structs C# feature.
    /// </summary>
    public const string RefStructs = nameof(RefStructs);

    /// <summary>
    /// The <see cref="FeatureName"/> used for the required members C# feature.
    /// </summary>
    public const string RequiredMembers = nameof(RequiredMembers);

    /// <summary>
    /// Initializes a new instance of the <see cref="CompilerFeatureRequiredAttribute"/> class.
    /// </summary>
    /// <param name="featureName">The name of the required compiler feature.</param>
    public CompilerFeatureRequiredAttribute(string featureName)
    {
        FeatureName = featureName;
    }

    /// <summary>
    /// Gets the name of the compiler feature.
    /// </summary>
    public string FeatureName { get; }

    /// <summary>
    /// Gets a value indicating whether the compiler can choose to allow access to the location where this attribute is applied
    /// if it does not understand <see cref="FeatureName"/>.
    /// </summary>
    public bool IsOptional { get; init; }
}

internal static class IsExternalInit
{
}

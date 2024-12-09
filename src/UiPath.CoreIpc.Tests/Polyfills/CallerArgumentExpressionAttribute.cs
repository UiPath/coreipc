#pragma warning disable

#if !NET5_0_OR_GREATER

namespace System.Runtime.CompilerServices;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

//
// Summary:
//     Allows capturing of the expressions passed to a method.
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
internal sealed class CallerArgumentExpressionAttribute : Attribute
{
    //
    // Summary:
    //     Initializes a new instance of the System.Runtime.CompilerServices.CallerArgumentExpressionAttribute
    //     class.
    //
    // Parameters:
    //   parameterName:
    //     The name of the targeted parameter.
    public CallerArgumentExpressionAttribute(string parameterName) => ParameterName = parameterName;

    //
    // Summary:
    //     Gets the target parameter name of the CallerArgumentExpression.
    //
    // Returns:
    //     The name of the targeted parameter of the CallerArgumentExpression.
    public string ParameterName { get; }
}


#endif
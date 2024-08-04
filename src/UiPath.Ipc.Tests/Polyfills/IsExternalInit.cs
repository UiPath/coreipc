﻿#pragma warning disable

#if !NET5_0_OR_GREATER

namespace System.Runtime.CompilerServices;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Reserved to be used by the compiler for tracking metadata. This class should not be used by developers in source code.
/// </summary>
[ExcludeFromCodeCoverage]
[DebuggerNonUserCode]
internal static class IsExternalInit
{
}

#endif
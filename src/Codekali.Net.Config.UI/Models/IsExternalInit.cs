// This polyfill enables C# 9+ `init`-only setters when targeting netstandard2.1.
// The type is built into .NET 5+ runtimes but must be declared manually for
// netstandard2.1 targets. The conditional compilation guard ensures it is
// never emitted when building for net5.0 or later, where it already exists.

#if NETSTANDARD2_1

namespace System.Runtime.CompilerServices;

/// <summary>
/// Reserved for use by the compiler. Enables init-only property setters (C# 9+).
/// </summary>
internal static class IsExternalInit { }

#endif
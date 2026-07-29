#if NETSTANDARD2_1
namespace System.Runtime.CompilerServices;

/// <summary>
/// Compiler marker that netstandard2.1 does not ship, without which `init` accessors do not compile.
/// The type is resolved per-assembly, and the suite is one assembly, so this is the only copy.
/// </summary>
internal static class IsExternalInit
{
}
#endif

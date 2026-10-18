#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    // Records and init accessors on netstandard2.0, which Visual Studio requires for analyzers.
    internal static class IsExternalInit { }
}
#endif

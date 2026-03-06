#if NETFRAMEWORK
namespace System.Diagnostics.CodeAnalysis
{
    //
    // Summary:
    //     Specifies that an output is not null even if the corresponding type allows it.
    //     Specifies that an input argument was not null when the call returns.
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, Inherited = false)]
    public sealed class NotNullAttribute : Attribute
    {
    }
}
#endif

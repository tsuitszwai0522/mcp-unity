using System;

namespace McpUnity.Tools
{
    /// <summary>
    /// Marks an <see cref="McpToolBase"/> implementation as a first-party tool that
    /// ships with hand-written TypeScript wrappers in <c>Server~/src/tools/</c>.
    /// First-party tools are excluded from the dynamic <c>list_tools</c> registration
    /// path used by external plugin tools, preventing double-registration.
    ///
    /// Apply this to every tool inside an <c>McpUnity.{Feature}</c> sub-assembly.
    /// As a fallback, <see cref="McpUnity.Unity.McpUnitySocketHandler.HandleListTools"/>
    /// also excludes any tool whose assembly name begins with "McpUnity." — the attribute
    /// is the preferred, explicit form.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class McpUnityFirstPartyAttribute : Attribute
    {
    }
}

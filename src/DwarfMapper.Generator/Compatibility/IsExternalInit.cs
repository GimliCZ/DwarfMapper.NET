// SPDX-License-Identifier: GPL-2.0-only

using System.ComponentModel;

// a polyfill has to live where the compiler looks for it
// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    /// <summary>
    ///     Polyfill enabling C# <c>init</c>-only setters (and therefore positional
    ///     records) on netstandard2.0, where this type is not provided by the runtime.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}

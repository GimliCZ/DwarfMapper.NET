// SPDX-License-Identifier: GPL-2.0-only

using System.ComponentModel;

namespace System.Runtime.CompilerServices;

/// <summary>
///     Polyfill enabling C# <c>init</c>-only setters (and therefore positional
///     records) on netstandard2.0, where this type is not provided by the runtime.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit
{
}

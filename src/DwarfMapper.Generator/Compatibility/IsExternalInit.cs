// SPDX-License-Identifier: GPL-2.0-only
namespace System.Runtime.CompilerServices;

/// <summary>
/// Polyfill enabling C# <c>init</c>-only setters (and therefore positional
/// records) on netstandard2.0, where this type is not provided by the runtime.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
internal static class IsExternalInit
{
}

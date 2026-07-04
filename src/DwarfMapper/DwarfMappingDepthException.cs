// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Thrown by a recursion-capable auto-synthesized mapper when the mapping depth exceeds
/// <see cref="DwarfRefContext.MaxDepth"/>. This is a loud, catchable replacement for the
/// otherwise uncatchable <see cref="StackOverflowException"/> that would occur on deep or
/// cyclic object graphs under the default <c>ReferenceHandling = None</c> mode.
/// </summary>
/// <remarks>
/// <para>
/// If you encounter this exception, consider one of:
/// <list type="bullet">
///   <item>Increase <c>[DwarfMapper(MaxDepth = N)]</c> (hard cap: 1000).</item>
///   <item>Use <c>ReferenceHandling = Preserve</c> to map graphs without recursion.</item>
///   <item>Truncate the source chain before mapping to stay within the depth limit.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class DwarfMappingDepthException : Exception
{
    /// <summary>The depth limit that was exceeded.</summary>
    public int MaxDepth { get; }

    /// <summary>The actual depth at which the limit was detected.</summary>
    public int ActualDepth { get; }

    /// <summary>
    /// Initializes a new <see cref="DwarfMappingDepthException"/> with depth information.
    /// </summary>
    public DwarfMappingDepthException(int maxDepth, int actualDepth)
        : base(
            $"DwarfMapper mapping depth exceeded the limit of {maxDepth}. " +
            $"Actual depth reached: {actualDepth}. " +
            $"Increase [DwarfMapper(MaxDepth = N)] (max 1000), or enable ReferenceHandling = Preserve " +
            $"to handle cyclic/deep graphs without recursion.")
    {
        MaxDepth = maxDepth;
        ActualDepth = actualDepth;
    }

    /// <summary>Initializes a new instance with no message (for serialization infrastructure).</summary>
    public DwarfMappingDepthException() : base() { }

    /// <summary>Initializes a new instance with a custom message (for serialization).</summary>
    public DwarfMappingDepthException(string message) : base(message) { }

    /// <summary>Initializes a new instance with a custom message and inner exception.</summary>
    public DwarfMappingDepthException(string message, System.Exception innerException)
        : base(message, innerException) { }
}

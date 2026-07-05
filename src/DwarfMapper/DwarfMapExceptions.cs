// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     Thrown by the ambient <see cref="IDwarfMapper" /> / <see cref="DwarfMapperRegistry" /> when no map is
///     registered for the requested pair. Normally prevented ahead of time by the compile-time DWARF061
///     validation at the composition root, or by <c>DwarfMap.Validate()</c> at startup; this is the last-resort
///     loud failure if neither ran.
/// </summary>
public sealed class DwarfMapMissingException : InvalidOperationException
{
    /// <summary>
    ///     Creates the exception for an unregistered <paramref name="sourceType" /> -&gt;
    ///     <paramref name="destinationType" /> pair.
    /// </summary>
    public DwarfMapMissingException(Type sourceType, Type destinationType)
        : base(FormatMessage(sourceType, destinationType))
    {
        SourceType = sourceType;
        DestinationType = destinationType;
    }

    /// <summary>Initializes a new instance with no message (for serialization infrastructure).</summary>
    public DwarfMapMissingException()
    {
    }

    /// <summary>Initializes a new instance with a custom message (for serialization).</summary>
    public DwarfMapMissingException(string message) : base(message)
    {
    }

    /// <summary>Initializes a new instance with a custom message and inner exception.</summary>
    public DwarfMapMissingException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>The (runtime) source type that had no registered map.</summary>
    public Type? SourceType { get; }

    /// <summary>The requested destination type.</summary>
    public Type? DestinationType { get; }

    private static string FormatMessage(Type? sourceType, Type? destinationType)
    {
        return $"No DwarfMapper map is registered for '{sourceType}' -> '{destinationType}'. " +
               $"Declare [GenerateMap<{sourceType?.Name}, {destinationType?.Name}>] in a referenced assembly " +
               "(its module initializer self-registers the map), or inject that assembly's concrete mapper directly.";
    }
}

/// <summary>
///     Thrown by <c>DwarfMap.Validate()</c> when required ambient maps are missing or ambiguous — the
///     fail-fast, one-time startup check that recovers DwarfMapper's loud-failure guarantee for the
///     cross-assembly linkage. The <see cref="Exception.Message" /> lists every offending pair.
/// </summary>
public sealed class DwarfMapValidationException : InvalidOperationException
{
    /// <summary>Initializes a new instance with no message (for serialization infrastructure).</summary>
    public DwarfMapValidationException()
    {
    }

    /// <summary>Creates the validation exception with a message listing the offending pairs.</summary>
    public DwarfMapValidationException(string message) : base(message)
    {
    }

    /// <summary>Initializes a new instance with a custom message and inner exception.</summary>
    public DwarfMapValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

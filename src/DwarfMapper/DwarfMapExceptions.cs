// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper
{
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
            : this(sourceType, destinationType, null)
        {
        }

        /// <summary>
        ///     Creates the exception, optionally naming the interfaces that were ambiguous.
        /// </summary>
        /// <param name="sourceType">The runtime type of the value that had no map.</param>
        /// <param name="destinationType">The requested destination type.</param>
        /// <param name="ambiguousInterfaces">
        ///     Interfaces of <paramref name="sourceType" /> that each had a registration, when there was more than
        ///     one. Reported rather than silently picked, because <see cref="Type.GetInterfaces" /> has no
        ///     guaranteed order — choosing one would mean mapping through a different map on a different run.
        /// </param>
        /// <param name="isUpdate">
        ///     <see langword="true" /> when the missing map is an UPDATE-INTO map. Update-into is keyed separately
        ///     from create-maps, so the message must say which one is absent — otherwise a reader goes looking for
        ///     a registration that already exists.
        /// </param>
        public DwarfMapMissingException(
            Type sourceType,
            Type destinationType,
            IReadOnlyList<Type>? ambiguousInterfaces,
            bool isUpdate = false)
            : base(FormatMessage(sourceType, destinationType, ambiguousInterfaces, isUpdate))
        {
            SourceType = sourceType;
            DestinationType = destinationType;
            AmbiguousInterfaces = ambiguousInterfaces ?? [];
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

        /// <summary>
        ///     When lookup failed because <b>several</b> of the source's interfaces had a registration, the
        ///     interfaces in question. Empty in the ordinary "nothing registered" case.
        /// </summary>
        public IReadOnlyList<Type> AmbiguousInterfaces { get; } = [];

        private static string FormatMessage(
            Type? sourceType,
            Type? destinationType,
            IReadOnlyList<Type>? ambiguousInterfaces,
            bool isUpdate = false)
        {
            // The typed constructors take non-nullable types, but a nullable-oblivious caller can pass null, and an
            // exception constructor must not throw while composing its own message. Each name is read once: the
            // iterator remedy below is reached only when sourceType is non-null, so a `sourceType?.Name` written
            // inside it carried a null arm no input can reach.
            var sourceName = sourceType?.Name;
            var destinationName = destinationType?.Name;

            // Update-into has its own key space, so "no map" here means something different from the create case:
            // a create-map for the same pair may well exist. Saying so stops the reader hunting for a
            // registration that is already there.
            if (isUpdate)
            {
                return $"No DwarfMapper UPDATE-INTO map is registered for '{sourceType}' -> '{destinationType}'. " +
                       "A create-map for the same pair does not satisfy this — they are different operations " +
                       "and are keyed separately. Declare a two-parameter partial method, e.g. " +
                       $"`public partial void Update({sourceName} source, {destinationName} " +
                       "destination);`, on a public mapper with a parameterless constructor.";
            }

            if (ambiguousInterfaces is { Count: > 1 })
            {
                return $"Ambiguous DwarfMapper map for '{sourceType}' -> '{destinationType}': it was not " +
                       "registered directly or on a base type, and more than one of its interfaces has a " +
                       $"registration ({string.Join(", ", ambiguousInterfaces.Select(i => i.Name))}). Interface " +
                       "order is not defined, so no map was chosen. Declare the pair for the concrete source " +
                       $"type: [GenerateMap<{sourceName}, {destinationName}>].";
            }

            // A compiler-generated iterator (Where/Select/SelectMany) can never be named by an attribute, so the
            // generic "declare the pair" advice is unactionable for it. Say the thing that actually works.
            var isIterator = sourceName?.Contains('<', StringComparison.Ordinal) == true || sourceType?.IsNestedPrivate == true;

            var remedy = isIterator
                ? $"'{sourceName}' is a compiler-generated LINQ iterator — no [GenerateMap] attribute can " + "name it. Materialize the sequence before mapping (.ToList()), or declare the pair for the " + "interface it implements, e.g. [GenerateMap<IEnumerable<T>, " + $"{destinationName}>]."
                : $"Declare [GenerateMap<{sourceName}, {destinationName}>] in a referenced assembly " + "(its module initializer self-registers the map), or inject that assembly's concrete mapper " + "directly.";

            return $"No DwarfMapper map is registered for '{sourceType}' -> '{destinationType}'. Lookup tried the " + "runtime type, its base types, and its interfaces. " + remedy;
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
}

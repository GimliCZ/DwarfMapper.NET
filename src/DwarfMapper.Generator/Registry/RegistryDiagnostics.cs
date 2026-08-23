// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Registry
{
    /// <summary>
    ///     Diagnostics for the <c>[MapTo]</c> registry generator. Kept in a SEPARATE class from
    ///     <c>DiagnosticDescriptors</c> because the DWARF0xx self-validation scans key on
    ///     <c>^DWARF\d{3}$</c> and these carry a <c>DWARFR</c> prefix. They are NOT unvalidated, though:
    ///     Scan1f/1g/1h in <c>AssemblyScanTests</c> enforce the same descriptor ↔ AnalyzerReleases sync
    ///     (both directions, plus severity) for this class that Scan1a/1b/1c enforce for that one.
    ///     A later unification could fold them into the DWARF0xx scheme.
    /// </summary>
    internal static class RegistryDiagnostics
    {
        private const string Category = "DwarfMapper.Registry";

        public static readonly DiagnosticDescriptor InvalidTarget = new(
            "DWARFR01",
            "Invalid [MapTo] target",
            "[MapTo] target {0} is not a mappable type (must be a non-abstract class or struct, and not the source type itself)",
            Category,
            DiagnosticSeverity.Error,
            true);

        public static readonly DiagnosticDescriptor UnmappedDestination = new(
            "DWARFR02",
            "Destination member is not mapped",
            "Destination member {0} has no source member (add a source member, a [MapProperty] binding, or remove it)",
            Category,
            DiagnosticSeverity.Error,
            true);

        public static readonly DiagnosticDescriptor ConflictingSource = new(
            "DWARFR03",
            "Conflicting sources for one destination member",
            "Destination member {0} is claimed by more than one source member — give them distinct positional [MapProperty] names",
            Category,
            DiagnosticSeverity.Error,
            true);

        // Message composed at report time (MessageFormat is the pass-through "{0}"), because the two arities this
        // covers need different remedies: a stacked-attribute count mismatch says "match the target count", a
        // class-model two-argument [MapProperty] on a registry member says "drop the first argument" instead.
        public static readonly DiagnosticDescriptor MapPropertyArity = new(
            "DWARFR04",
            "[MapProperty] value count does not match the targets",
            "{0}",
            Category,
            DiagnosticSeverity.Error,
            true);

        public static readonly DiagnosticDescriptor NoConversion = new(
            "DWARFR05",
            "No conversion between mapped members",
            "No built-in conversion for {0}; the source and destination member types are incompatible (use the [DwarfMapper] class model for a custom converter)",
            Category,
            DiagnosticSeverity.Error,
            true);

        // Two [MapTo] targets whose SIMPLE names collide (Foo.Order and Bar.Order) both generate `ToOrder(this Src)`
        // in one static class → CS0111 out of generated code. The MapTo<TTarget>() dispatcher is fine (it keys on
        // typeof), so only the per-target convenience methods clash.
        public static readonly DiagnosticDescriptor DuplicateTargetMethodName = new(
            "DWARFR08",
            "Two [MapTo] targets generate the same method name",
            "{0}",
            Category,
            DiagnosticSeverity.Error,
            true);

        // The registry constructs every type it builds with `new T { … }`, which requires an accessible
        // parameterless ctor. A ctor-only type also has no writable members the completeness gate can miss, so the
        // failure surfaced only as CS1729 out of generated code.
        //
        // TWO call sites, because the registry constructs exactly two kinds of type (B30, round 22 W3): the
        // [MapTo] target itself, and every nested object SynthNested builds — which is also the path a
        // collection's ELEMENT type reaches (TryCollection → Resolve → SynthNested; the collection itself is
        // always a List<T> or an array, neither of which can lack a parameterless constructor). Guarding only the
        // target left the identical CS1729 one level down with no diagnostic at all, which is precisely what
        // DWARFR11 had to be given both sites to avoid. ONE id rather than a second: a separate id for the second
        // site would restate the same refusal in different words and re-open the asymmetry this fixes — so the
        // TITLE names the registry rather than the target, and the message is composed at the call site ("{0}")
        // so each can say which type is at fault and how it was reached.
        public static readonly DiagnosticDescriptor NoParameterlessConstructor = new(
            "DWARFR09",
            "A type the [MapTo] registry constructs has no accessible parameterless constructor",
            "{0}",
            Category,
            DiagnosticSeverity.Error,
            true);

        // Info, mirroring DWARF038 in the class model: the conversion is legal and implicit in C#, so making it an
        // error would reject code that compiles fine today — but it loses precision above the mantissa, and the
        // whole point is that the registry must not be SILENT where the class model warns.
        public static readonly DiagnosticDescriptor LossyImplicitConversion = new(
            "DWARFR07",
            "Lossy implicit numeric conversion",
            "Implicit conversion {0} crosses numeric categories and loses precision for large magnitudes (the [DwarfMapper] class model reports this as DWARF038)",
            Category,
            DiagnosticSeverity.Info,
            true);

        // The trust boundary, mirroring DWARF072 in the class model — and a separate id rather than a reuse of it,
        // because DWARF072's message names [DwarfMapper(AutoMatchMembers = false)] and there is no mapper class at
        // this front door: the instruction arrives as [assembly: DwarfMapperDefaults(AutoMatchMembers = false)] and
        // the fixes are written on the SOURCE member, not on a mapping method.
        public static readonly DiagnosticDescriptor AutoMatchDisabled = new(
            "DWARFR10",
            "Member has a source match but auto-matching is disabled",
            "Destination member {0} would be wired from a same-named source member, but this assembly is " +
            "explicit-only ([assembly: DwarfMapperDefaults(AutoMatchMembers = false)]) so nothing is auto-wired " +
            "across the trust boundary. Name the destination deliberately with [MapProperty] on the source " +
            "member, or exclude the source member with [MapIgnore]. This is what stops an untrusted same-named " +
            "member (e.g. IsAdmin) from silently over-posting onto a protected one.",
            Category,
            DiagnosticSeverity.Error,
            true);

        // [DwarfMapperConstructor] names the constructor DwarfMapper must build a target with. This front door
        // reads no such thing: EVERY type it constructs — the [MapTo] target itself and every nested object it
        // synthesizes a helper for — is built with `new T { … }`, and no constructor is selected at all (there is
        // no ConstructorSelector call anywhere under Registry/). Warning, not Error: the object-initializer
        // mapping the caller gets is correct and complete, so the mapper still ships; what is wrong is that the
        // caller's stated intent is silently not the one the emitted code follows, and the SAME annotation on the
        // SAME type is honoured through a [DwarfMapper] class model map over that pair.
        public static readonly DiagnosticDescriptor ConstructorDirectiveNotRead = new(
            "DWARFR11",
            "[DwarfMapperConstructor] is not read by the [MapTo] registry",
            "[DwarfMapperConstructor] on {0} is not read here: the [MapTo] registry front door constructs every " +
            "target with an object initializer (new {0} {{ ... }}) and selects no constructor, so the annotated " +
            "one is never called and the members are assigned after construction instead. Map the pair with the " +
            "[DwarfMapper] class model — a 'partial {0} Map(...)' on a [DwarfMapper] class — which selects it. " +
            "Remove the attribute if the object-initializer mapping is what you want.",
            Category,
            DiagnosticSeverity.Warning,
            true);

        // The [MapIgnore] mirror of DWARFR04's member-form [MapProperty] check, and the registry half of the
        // B15 decision (the co-located host refuses the identical text as DWARF089): the registry's member form
        // ignores the ANNOTATED source member itself, so an argument written on it is discarded — a silent
        // discard of exactly the shape this family exists to end. A Warning rather than an Error, for DWARF089's
        // own reason: the directive's payload (ignore this member) is usable and honoured, so the mapping ships;
        // what is wrong is that the argument the caller wrote names nothing here. Message composed at report
        // time ("{0}") so it can quote the argument as written.
        public static readonly DiagnosticDescriptor MapIgnoreArgumentNotRead = new(
            "DWARFR12",
            "[MapIgnore] argument is not read by the [MapTo] registry",
            "{0}",
            Category,
            DiagnosticSeverity.Warning,
            true);

        public static readonly DiagnosticDescriptor RecursiveNesting = new(
            "DWARFR06",
            "Recursive nested mapping is not supported by the registry",
            "Nested mapping for {0} is recursive; the registry front door does not thread a reference context — use the [DwarfMapper] class model (ReferenceHandling/OnCycle) for cyclic graphs",
            Category,
            DiagnosticSeverity.Error,
            true);
    }
}

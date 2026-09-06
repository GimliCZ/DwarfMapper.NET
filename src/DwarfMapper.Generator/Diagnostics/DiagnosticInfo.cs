// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Diagnostics
{
    /// <summary>Value-equatable diagnostic carrier; converted to a real <see cref="Diagnostic" /> at output time.</summary>
    public sealed record DiagnosticInfo(
        // IMPORTANT — descriptor identity equality:
        // C# record equality uses REFERENCE identity for DiagnosticDescriptor (it is a class with no custom Equals).
        // This is correct ONLY because callers MUST pass the static readonly singletons from DiagnosticDescriptors.
        // Constructing a DiagnosticDescriptor inline at call sites (e.g. new DiagnosticDescriptor("DWARF005", ...))
        // would create a distinct instance and silently break Roslyn's incremental-cache equality checks,
        // causing the generator to re-run unnecessarily or produce stale output.
        DiagnosticDescriptor Descriptor,
        LocationInfo? Location,
        string MessageArg,
        // Optional per-instance severity override. Used by DWARF038 (implicit-conversion suggestion):
        // Info by default, escalated to Error when [DwarfMapper(ImplicitConversions = false)] is set.
        // Null → use the descriptor's DefaultSeverity. Part of value equality (so the incremental cache
        // distinguishes a suggestion from an error for the same descriptor).
        DiagnosticSeverity? SeverityOverride = null,
        // The destination member this diagnostic is about, surfaced to code fixes as the "Member" property.
        // Code fixes used to recover it by PARSING the message between the first pair of quotes, which couples a
        // fix to the exact wording of a human-readable string: rewording a message (or localising it) silently
        // breaks the fix with no compile error and no failing test. A plain string keeps the record
        // value-equatable, which the incremental cache depends on — hence a single field rather than a dictionary.
        string? MemberName = null,
        // The pair a code fix should copy configuration FROM, as "SourceDisplayName|TargetDisplayName". Only
        // DWARF085 sets it. Handed over rather than re-derived: the generator has already decided which pair is
        // the base (nearest declared pair up the class chain, ties refused), and a CodeFixProvider working that
        // out again from syntax would be a second implementation of the same rule — free to disagree with the
        // first, silently, which is a defect shape this project has already been bitten by twice.
        string? SourcePair = null,
        // True when this error is confined to ONE mapping method and must NOT suppress the whole mapper class.
        // Only DWARF028 (ProjectionNotTranslatable) sets it, and only after MapperExtractor has dropped the
        // projection method it belongs to: the member cannot be translated, so that ONE method cannot be
        // generated, but the .Map methods sitting beside it on the same class are unaffected and used to be
        // taken down with it (TASKS.md I14). Read by MapperClassModel.HasBlockingError, which decides emission;
        // the diagnostic is still REPORTED either way. Part of value equality, like SeverityOverride, so the
        // incremental cache tells a scoped refusal from a class-killing one.
        bool ScopedToMethod = false,
        // A second message argument, for the rare descriptor whose message must name TWO things: DWARF064 names
        // the target the [MapValue] is for AND the real spelling of the source member it shadows, because that
        // spelling is what the remedy it prints has to carry. Every other pipeline descriptor either has one
        // placeholder or has its caller build the whole string (DWARF067–069). Null means the message has one
        // placeholder. A plain string, like MemberName, so the record stays value-equatable for the cache.
        string? MessageArg2 = null,
        // The type a code fix should REWRITE, as its DocumentationCommentId ("T:Demo.OrderDto"). Only DWARF103
        // sets it. A doc-comment id rather than a display string because it round-trips:
        // DocumentationCommentId.GetFirstSymbolForDeclarationId hands the fix back the very symbol the
        // classifier judged, so the rewrite cannot land on a different type of the same name.
        string? TransferModelId = null,
        // The transfer models that type INLINES, transitively, pipe-separated as DocumentationCommentIds; null
        // when it inlines none. These are not a convenience: the classifier costs a nested shaped model at its
        // own struct size rather than at pointer width, so the size DWARF103 PRINTS is only true if these are
        // converted alongside the root. A fix that rewrote the root alone would retroactively falsify a number
        // already in front of the consumer, which is why they travel together and are applied as one change.
        // A plain string, like MemberName and SourcePair, so the record stays value-equatable for the cache.
        string? NestedTransferModelIds = null)
    {
        /// <summary>Property bag key under which <see cref="MemberName" /> reaches a CodeFixProvider.</summary>
        public const string MemberPropertyKey = "Member";

        /// <summary>Property bag key under which <see cref="SourcePair" /> reaches a CodeFixProvider.</summary>
        public const string SourcePairPropertyKey = "SourcePair";

        /// <summary>Property bag key under which <see cref="TransferModelId" /> reaches a CodeFixProvider.</summary>
        public const string TransferModelIdPropertyKey = "TransferModelId";

        /// <summary>
        ///     Property bag key under which <see cref="NestedTransferModelIds" /> reaches a CodeFixProvider.
        ///     Absent, rather than present and empty, when the type inlines no transfer model — so "no nested
        ///     models" and "an older generator that did not carry them" stay the same reading, which is the
        ///     conservative one: the fix converts the root only.
        /// </summary>
        public const string NestedTransferModelIdsPropertyKey = "NestedTransferModelIds";

        public bool IsError => (SeverityOverride ?? Descriptor.DefaultSeverity) == DiagnosticSeverity.Error;

        public Diagnostic ToDiagnostic()
        {
            var location = Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None;
            var properties = ImmutableDictionary<string, string?>.Empty;
            if (MemberName is not null)
            {
                properties = properties.Add(MemberPropertyKey, MemberName);
            }

            if (SourcePair is not null)
            {
                properties = properties.Add(SourcePairPropertyKey, SourcePair);
            }

            if (TransferModelId is not null)
            {
                properties = properties.Add(TransferModelIdPropertyKey, TransferModelId);
            }

            if (NestedTransferModelIds is not null)
            {
                properties = properties.Add(NestedTransferModelIdsPropertyKey, NestedTransferModelIds);
            }

            if (properties.Count == 0)
            {
                properties = null!;
            }

            var messageArgs = MessageArg2 is null
                ? new object[] { MessageArg }
                : new object[] { MessageArg, MessageArg2 };
            return SeverityOverride is { } sev
                ? Diagnostic.Create(Descriptor, location, sev, null, properties, messageArgs)
                : Diagnostic.Create(Descriptor, location, properties, messageArgs);
        }
    }
}

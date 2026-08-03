// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>
///     Endpoint divergences that are known, recorded, and not yet fixed.
///     <para>
///         A ratchet, not an allowance. Any silent cell NOT named here fails the build, and any entry here
///         that stops being silent must be removed — so the list can only shrink, and it cannot quietly
///         re-permit a divergence that comes back.
///     </para>
///     <para>
///         Kept in one place because two things consume it: the parity property, which would otherwise fail
///         the build, and the generated support matrix, which renders these cells as <c>SILENT</c> so a reader
///         sees the gap rather than a blank. Two copies would drift, and a stale copy is how a fixed gap
///         silently keeps its exemption.
///     </para>
/// </summary>
public static class OptionGaps
{
    /// <summary>
    ///     Currently EMPTY. Every divergence found in this codebase has been closed:
    ///     SkipNullSourceMembers, NullSubstitute, When=, AllowNonPublic and the nullable-to-non-nullable
    ///     decision at projection; AutoMatchMembers, AutoNest and IgnoreObsoleteMembers at projection;
    ///     AutoMatchMembers at the span and async-stream endpoints (DWARF077); and RequiredMapping's
    ///     source-coverage gate at every endpoint that lacked it.
    ///     <para>
    ///         Kept rather than deleted because the ratchet reads from it: an empty dictionary means every
    ///         silent cell fails the build immediately, with nowhere to put a new one without writing down
    ///         why. Deleting the type would just make the next gap easier to leave unrecorded.
    ///     </para>
    /// </summary>
    public static readonly Dictionary<string, string> KnownSilent = new(StringComparer.Ordinal)
    {
        ["MaxDepth"] =
            "honoured at CreateMap and UpdateInto, silent at the span and async-stream endpoints. Found only "
            + "once a RECURSIVE fixture existed — a fixed-depth chain never exercises a depth BUDGET, so the "
            + "row read 'not probed' and claimed nothing. The element pair's depth guard comes from the "
            + "auto-synthesized mapper rather than the method model, so adding MaxDepth to the span/async "
            + "models (done, for consistency with their siblings) changes no output. Lower severity than it "
            + "sounds: the default bound of 64 still applies, so this is a tighter bound being ignored, not "
            + "unguarded recursion",

        ["NullCollections"] =
            "honoured everywhere except Projection, which reads the option nowhere and always emits "
            + "`src.Items == null ? null : ...` — i.e. AsNull. Under the DEFAULT (AsEmpty) the runtime "
            + "produces an EMPTY collection, so .Map and .Project answer the same input differently and a "
            + "caller doing dto.Items.Length gets an NRE on the projection path only.\n\n"
            + "NOT fixed, and deliberately so — this one is a DESIGN DECISION, not a threading oversight "
            + "like the other ten. A refusal was implemented and reverted: it broke seven existing tests, "
            + "including ProjectionDeepTests.Projection_nullable_collection_member_gets_source_null_guard, "
            + "which asserts that ternary ON PURPOSE because Enumerable.Select(null!, ...) throws at query "
            + "evaluation time, and the ProjectionMatrixSafeTests capability tests. Refusing would mean "
            + "'you cannot project a nullable collection under default options', a capability regression "
            + "far larger than the divergence it closes.\n\n"
            + "Three candidate resolutions, for a maintainer to choose:\n"
            + "  (a) honour AsEmpty by emitting `== null ? new List<T>() : ...` — fixes it properly, but "
            + "needs someone to confirm the provider translates a constructed empty collection inside an "
            + "expression tree; failing inside a query at runtime is worse than the current divergence;\n"
            + "  (b) refuse with DWARF028 unless NullCollections = AsNull — loud and correct, but breaks the "
            + "default path for every nullable source collection;\n"
            + "  (c) declare projection's collection null-semantics to be AsNull by nature and document it, "
            + "leaving the code alone.\n\n"
            + "Pinned by the generated matrix rendering this cell SILENT, so it cannot be forgotten"
    };

    /// <summary>
    ///     Cells where the option CANNOT apply because the endpoint has no such surface — a different claim
    ///     from <see cref="KnownSilent" />, which is "it should apply and does not".
    ///     <para>
    ///         Shared with the generated matrix so it renders these as structural rather than as a
    ///         divergence. Keeping them apart matters: "there is nothing here to configure" and "your
    ///         configuration was discarded" look identical in the output and mean opposite things.
    ///     </para>
    /// </summary>
    public static readonly Dictionary<(string Option, Endpoint Endpoint), string> StructurallyInapplicable =
        new()
        {
            // The convenience extension is the create-shaped `source.ToTarget()`. Only a create map produces
            // one, so on every other endpoint there is nothing for GenerateExtensions to suppress.
            [("GenerateExtensions", Endpoint.UpdateInto)] =
                "an update has no source.ToTarget() form to suppress",
            [("GenerateExtensions", Endpoint.Projection)] =
                "projection emits no convenience extension",
            [("GenerateExtensions", Endpoint.SpanMap)] =
                "the extension is generated per mapper, not per span overload",
            [("GenerateExtensions", Endpoint.AsyncStream)] =
                "the extension is generated per mapper, not per stream overload"
        };
}

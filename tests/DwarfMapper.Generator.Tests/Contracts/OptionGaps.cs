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
            "honoured everywhere except Projection, where a null source collection is mapped with no regard "
            + "for the configured policy and no diagnostic. Found only after the fixture used DIFFERENT "
            + "collection types (List<int> -> int[]); with the same type on both sides it is a reference copy "
            + "and the null policy never comes up. ResolveProjectionMembers is not passed nullCollections at "
            + "all — the same not-threaded shape as the seven already fixed, and the next one to close"
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

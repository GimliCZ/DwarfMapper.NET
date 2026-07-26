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
    public static readonly Dictionary<string, string> KnownSilent = new(StringComparer.Ordinal);
}

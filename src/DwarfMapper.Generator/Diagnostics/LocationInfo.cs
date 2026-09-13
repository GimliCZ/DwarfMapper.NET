// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DwarfMapper.Generator.Diagnostics
{
    /// <summary>Value-equatable replacement for <see cref="Location" /> in pipeline models.</summary>
    public sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
    {
        public Location ToLocation()
        {
            return Location.Create(FilePath, TextSpan, LineSpan);
        }

        /// <summary>
        ///     <paramref name="info" />'s <see cref="Location" />, or <see cref="Location.None" /> for a diagnostic that has
        ///     no position — the degradation every report site relies on when <see cref="From" /> could not keep one.
        /// </summary>
        /// <remarks>
        ///     One statement for the report sites that spelled it inline. Those sites only ever receive a located
        ///     diagnostic through a compilation, so the no-position answer is reached here, where the unit test asks it.
        /// </remarks>
        public static Location ToLocationOrNone(LocationInfo? info)
        {
            return info is null ? Location.None : info.ToLocation();
        }

        /// <summary>
        ///     <see cref="From" /> for a symbol's FIRST declaration location, or <see cref="Location.None" /> when it has
        ///     none. Every symbol the generator anchors a diagnostic at is declared in source, so the empty case is not
        ///     reached through a compilation — it is one helper, tested directly, instead of six inline fallbacks.
        /// </summary>
        public static LocationInfo? FromFirst(ImmutableArray<Location> locations)
        {
            return From(locations.FirstOrDefault() ?? Location.None);
        }

        /// <summary>
        ///     <see cref="From" /> for the syntax a reference points at — typically an attribute's
        ///     <see cref="AttributeData.ApplicationSyntaxReference" /> — or <see langword="null" /> when there is none. An
        ///     attribute read from a referenced assembly's metadata has no application syntax; one helper answers that
        ///     for every directive reader instead of each repeating the fallback inline.
        /// </summary>
        public static LocationInfo? FromReference(SyntaxReference? reference)
        {
            return reference is null ? null : From(reference.GetSyntax().GetLocation());
        }

        /// <summary>
        ///     <see cref="FromReference(SyntaxReference?)" />, or <paramref name="fallback" /> when that has no location —
        ///     for a reader that anchors a directive at its enclosing declaration when the directive itself has no
        ///     position.
        /// </summary>
        public static LocationInfo? FromReference(SyntaxReference? reference, LocationInfo? fallback)
        {
            return FromReference(reference) ?? fallback;
        }

        public static LocationInfo? From(Location location)
        {
            if (location is null || location.SourceTree is null)
            {
                return null;
            }

            // A Location is a span INTO a particular SourceText. Inside an IDE the generator is handed
            // symbols from a compilation snapshot that the user is still editing, and ISymbol.Locations
            // can name a span in ANOTHER file that has since shrunk - at which point GetLineSpan() throws
            // ArgumentOutOfRangeException('character') deep inside Roslyn's TextLineCollection.
            //
            // That must never escape. A source generator which throws "will not contribute to the output":
            // one stale span in one symbol takes down EVERY map in the compilation, and the consumer sees
            // their whole mapping layer vanish with an exception instead of a diagnostic. The position is
            // worth strictly less than the generated code, so degrade to a position-less diagnostic -
            // every consumer already spells this `Location?.ToLocation() ?? Location.None`.
            //
            // The bounds test is the whole guard, and it is sufficient rather than merely likely: a TextSpan
            // cannot have Start < 0 or End < Start, GetLineSpan() maps exactly Start and End through the line
            // table of the SAME tree's text, and that table only throws for a position outside
            // [0, Length]. With End <= Length every position it is asked for is inside. No catch backs this
            // up on purpose: one that cannot fire is a claim no test can check.
            if (location.SourceSpan.End > location.SourceTree.Length)
            {
                return null;
            }

            return new LocationInfo(
                location.SourceTree.FilePath,
                location.SourceSpan,
                location.GetLineSpan().Span);
        }
    }
}

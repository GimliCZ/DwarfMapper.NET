// SPDX-License-Identifier: GPL-2.0-only

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

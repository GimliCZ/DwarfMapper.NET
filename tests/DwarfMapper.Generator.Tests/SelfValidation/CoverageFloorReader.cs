// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text.RegularExpressions;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Reads the per-assembly coverage floors out of <c>scripts/housekeeping.ps1</c>.
    ///     <para>
    ///         This is what remains of <c>QualityBadgeRenderer</c>, which rendered the README's quality badges
    ///         from the gates' own files until those badges became LIVE — the Stryker dashboard's aggregate and
    ///         Codecov's figure, both fetched when the page is viewed. Rendering committed numbers and
    ///         byte-comparing them is no longer how the README states quality, so the rendering, the ledger-row
    ///         reader, the <c>break</c> reader and the colour derivation went with it: dead code, deleted rather
    ///         than kept "in case".
    ///     </para>
    ///     <para>
    ///         The floor parser did not go with them, because it was never only a badge reader.
    ///         <c>RatchetInvariantScanTests</c> uses it to assert the floors still exist at all: Codecov's
    ///         <c>auto</c> targets forbid only getting WORSE, so without a fixed line, coverage could ratchet
    ///         down one non-regressing commit at a time and nothing would measure it. The floors table is that
    ///         absolute line, and this is the only thing that reads it.
    ///     </para>
    /// </summary>
    internal static partial class CoverageFloorReader
    {
        private const string HousekeepingPath = "scripts/housekeeping.ps1";

        /// <summary>How many assemblies <c>$coverageFloors</c> must declare. A vacuity floor, not a list.</summary>
        private const int ExpectedCoverageFloors = 5;

        /// <summary>Split from the file read so the reader can be driven against fixture text in its own tests.</summary>
        internal static IReadOnlyList<CoverageFloor> ParseCoverageFloors(string housekeeping)
        {
            const string opener = "$coverageFloors = [ordered]@{";
            var open = housekeeping.IndexOf(opener, StringComparison.Ordinal);
            if (open < 0)
            {
                throw new InvalidOperationException(
                    $"{HousekeepingPath}: no '{opener}' block. It is the absolute half of the coverage gate \u2014 " + "Codecov's `auto` targets only forbid a DROP against the previous commit. If the floors moved " + "somewhere else, point this reader at the new home rather than letting the gate read nothing.");
            }

            var close = housekeeping.IndexOf('}', open + opener.Length);
            if (close < 0)
            {
                throw new InvalidOperationException($"{HousekeepingPath}: '{opener}' is never closed with '}}'.");
            }

            var body = housekeeping[(open + opener.Length)..close];
            var floors = new List<CoverageFloor>();
            foreach (var line in body.Split('\n'))
            {
                var match = FloorLine().Match(line);
                if (!match.Success)
                {
                    continue;
                }

                floors.Add(new CoverageFloor(match.Groups["name"].Value,
                    double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture)));
            }

            if (floors.Count != ExpectedCoverageFloors)
            {
                throw new InvalidOperationException(
                    $"{HousekeepingPath}: parsed {floors.Count} coverage floor(s), expected {ExpectedCoverageFloors}. " +
                    "Either an assembly joined or left the gate \u2014 in which case update ExpectedCoverageFloors in " +
                    "the same commit \u2014 or the block's line shape changed and this reader is now silently seeing " +
                    "fewer floors than the gate enforces.");
            }

            foreach (var floor in floors)
                if (floor.Floor is <= 0 or > 100)
                {
                    throw new InvalidOperationException(
                        $"{HousekeepingPath}: coverage floor for {floor.Assembly} parsed as {floor.Floor}, which is " + "not a percentage. The parse is wrong, not the gate.");
                }

            return floors;
        }

        [GeneratedRegex(@"^\s*'(?<name>[^']+)'\s*=\s*(?<value>\d+(?:\.\d+)?)\s*$")]
        private static partial Regex FloorLine();

        /// <summary>A coverage floor as <c>scripts/housekeeping.ps1</c> declares it.</summary>
        internal readonly record struct CoverageFloor(string Assembly, double Floor);
    }
}

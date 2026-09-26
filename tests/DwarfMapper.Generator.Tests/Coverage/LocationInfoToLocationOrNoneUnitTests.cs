// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

// LocationInfo.ToLocationOrNone replaces the inline `info?.ToLocation() ?? Location.None` at DwarfGenerator's report
// sites (per-branch rule: a branch no compilation reaches is extracted and tested directly). Those sites only receive
// located diagnostics through a compilation, so the no-position answer is pinned here.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class LocationInfoToLocationOrNoneUnitTests
    {
        [Fact]
        public void No_location_info_is_Location_None()
        {
            Assert.Equal(Location.None, LocationInfo.ToLocationOrNone(null));
        }

        [Fact]
        public void Location_info_becomes_its_file_location()
        {
            var info = new LocationInfo("Mapper.cs", new TextSpan(6, 4), new LinePositionSpan(new LinePosition(0, 6), new LinePosition(0, 10)));

            var location = LocationInfo.ToLocationOrNone(info);

            Assert.Equal(new TextSpan(6, 4), location.SourceSpan);
            Assert.Equal("Mapper.cs", location.GetMappedLineSpan().Path);
        }
    }
}

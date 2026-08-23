// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    public class CiSrc
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    public class CiDst
    {
        // Conventional camelCase constructor parameters binding PascalCase source members.
        public CiDst(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public int Id { get; }

        public string Name { get; }
    }

// No [DwarfMapper(CaseInsensitive=true)] — constructor parameters must match case-insensitively by default.
    [DwarfMapper]
    [GenerateMap<CiSrc, CiDst>]
    public partial class CtorCaseMapper
    {
    }

    public class CtorCaseInsensitiveRuntimeTests
    {
        [Fact]
        public void Ctor_params_match_source_members_case_insensitively_by_default()
        {
            var d = new CtorCaseMapper().Map(new CiSrc
            {
                Id = 5,
                Name = "x"
            });
            Assert.Equal(5, d.Id);
            Assert.Equal("x", d.Name);
        }
    }
}

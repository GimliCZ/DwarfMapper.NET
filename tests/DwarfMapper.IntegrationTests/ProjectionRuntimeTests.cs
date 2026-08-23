// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    public class PPerson
    {
        public int Age { get; set; }

        public string Name { get; set; } = "";
    }

    public class PPersonDto
    {
        public int Age { get; set; }

        public string Name { get; set; } = "";
    }

    [DwarfMapper]
    public partial class ProjMapper
    {
        public partial IQueryable<PPersonDto> Project(IQueryable<PPerson> src);
    }

// ── Constructor targets (R18-32) ─────────────────────────────────────────────────────────────────────────
// A projection whose target is built by CONSTRUCTOR rather than member-init, with members outside the
// parameter list assigned by an initializer on the call: `new PSlotDto(x) { Note = y }`. Every projection
// test before this one built its target by member-init, so the expression tree shape below — a MemberInit
// over a New WITH arguments, binding an INIT-ONLY property — had never actually been executed. It compiles;
// this asserts it also runs and carries the data.

    public class PWindow
    {
        public int Start { get; set; }

        public int End { get; set; }
    }

    public class PSlot
    {
        public PWindow Window { get; set; } = new();

        public string Note { get; set; } = "";
    }

    public sealed record PSlotDto(int Start)
    {
        public string Note { get; init; } = "";
    }

    [DwarfMapper]
    public partial class ProjCtorMapper
    {
        [MapProperty("Window.Start", "Start")]
        public partial IQueryable<PSlotDto> Project(IQueryable<PSlot> src);
    }

    public class ProjectionRuntimeTests
    {
        [Fact]
        public void Projects_a_constructor_target_fed_by_a_dotted_explicit_map()
        {
            var source = new[]
            {
                new PSlot
                {
                    Window = new PWindow
                    {
                        Start = 9,
                        End = 10
                    },
                    Note = "Hall A"
                },
                new PSlot
                {
                    Window = new PWindow
                    {
                        Start = 11,
                        End = 12
                    },
                    Note = "Hall B"
                }
            }.AsQueryable();

            var dtos = new ProjCtorMapper().Project(source).ToList();

            Assert.Equal(2, dtos.Count);
            Assert.Equal(9, dtos[0].Start);
            Assert.Equal(11, dtos[1].Start);
            // The member the constructor did NOT take — dropped in silence before R18-32, and the reason the
            // whole family was fixed rather than filed.
            Assert.Equal("Hall A", dtos[0].Note);
            Assert.Equal("Hall B", dtos[1].Note);
        }

        [Fact]
        public void Projects_over_queryable()
        {
            var source = new[]
            {
                new PPerson
                {
                    Age = 30,
                    Name = "Thorin"
                },
                new PPerson
                {
                    Age = 40,
                    Name = "Dwalin"
                }
            }.AsQueryable();

            var dtos = new ProjMapper().Project(source).ToList();

            Assert.Equal(2, dtos.Count);
            Assert.Equal(30, dtos[0].Age);
            Assert.Equal("Thorin", dtos[0].Name);
            Assert.Equal("Dwalin", dtos[1].Name);
        }
    }
}

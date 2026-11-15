// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    public class DepItem
    {
        public int V { get; set; }
    }

    public class DepItemDto
    {
        public int V { get; set; }
    }

    public class DepHolder
    {
        public DepItem One { get; set; } = new();

        public List<DepItem> All { get; set; } = new();

        public DepItem[] Arr { get; set; } = Array.Empty<DepItem>();

        public Dictionary<string, DepItem> ByName { get; set; } = new();
    }

    public class DepHolderDto
    {
        public DepItemDto One { get; set; } = new();

        public List<DepItemDto> All { get; set; } = new();

        public DepItemDto[] Arr { get; set; } = Array.Empty<DepItemDto>();

        public Dictionary<string, DepItemDto> ByName { get; set; } = new();
    }

    // AutoNest = false: nothing is synthesized for Item -> ItemDto, so every nested use is the declared method.
    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve, AutoNest = false)]
    public partial class DepDeclaredOnlyMapper
    {
        public partial DepItemDto MapItem(DepItem i);

        public partial DepHolderDto Map(DepHolder h);
    }

    public abstract class DepShape
    {
        public string Name { get; set; } = "";
    }

    public class DepCircle : DepShape
    {
    }

    public class DepShapeDto
    {
        public string Name { get; set; } = "";
    }

    public class DepDrawing
    {
        public DepShape? Main { get; set; }

        public List<DepShape> All { get; set; } = new();

        public Dictionary<string, DepShape> ByName { get; set; } = new();
    }

    public class DepDrawingDto
    {
        public DepShapeDto? Main { get; set; }

        public List<DepShapeDto> All { get; set; } = new();

        public Dictionary<string, DepShapeDto> ByName { get; set; } = new();
    }

    // An abstract source is never auto-nested, so here too every nested use is the declared method - a plain map of
    // the base type's members, no dispatch.
    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class DepAbstractSourceMapper
    {
        public partial DepShapeDto MapShape(DepShape s);

        public partial DepDrawingDto Map(DepDrawing d);
    }

    public enum DepStatus
    {
        Open,
        Closed
    }

    public enum DepStatusDto
    {
        Open,
        Closed
    }

    public class DepBoard
    {
        public List<DepStatus> All { get; set; } = new();

        public Dictionary<DepStatus, int> Count { get; set; } = new();

        public Dictionary<string, DepStatus> ByName { get; set; } = new();
    }

    public class DepBoardDto
    {
        public List<DepStatusDto> All { get; set; } = new();

        public Dictionary<DepStatusDto, int> Count { get; set; } = new();

        public Dictionary<string, DepStatusDto> ByName { get; set; } = new();
    }

    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class DepEnumMapper
    {
        public partial DepStatusDto MapStatus(DepStatus s);

        public partial DepBoardDto Map(DepBoard b);
    }

    /// <summary>
    ///     <b>Under Preserve, a declared mapping method reached as an element keeps one target per source</b> - not
    ///     only a <c>[MapDerivedType]</c> dispatch (round 35, T12). The collection and dictionary helpers called the
    ///     public method, each call on a context of its own, so two references to one element became two objects.
    ///     A value-type element (an enum here) has no identity to keep and is mapped as it always was.
    /// </summary>
    public class DeclaredElementUnderPreserveRuntimeTests
    {
        [Fact]
        public void With_AutoNest_off_one_item_is_one_target_on_every_route()
        {
            var item = new DepItem { V = 7 };
            var holder = new DepHolder { One = item, All = { item, item }, Arr = new[] { item }, ByName = { ["x"] = item } };

            var dto = new DepDeclaredOnlyMapper().Map(holder);

            Assert.Equal(7, dto.One.V);
            Assert.Same(dto.All[0], dto.All[1]);
            Assert.Same(dto.One, dto.All[0]);
            Assert.Same(dto.One, dto.Arr[0]);
            Assert.Same(dto.One, dto.ByName["x"]);
        }

        [Fact]
        public void With_an_abstract_source_one_shape_is_one_target_on_every_route()
        {
            var circle = new DepCircle { Name = "c" };
            var drawing = new DepDrawing { Main = circle, All = { circle, circle }, ByName = { ["c"] = circle } };

            var dto = new DepAbstractSourceMapper().Map(drawing);

            Assert.Equal("c", dto.Main!.Name);
            Assert.Same(dto.All[0], dto.All[1]);
            Assert.Same(dto.Main, dto.All[0]);
            Assert.Same(dto.Main, dto.ByName["c"]);
        }

        [Fact]
        public void A_declared_enum_map_is_applied_to_elements_keys_and_values()
        {
            var board = new DepBoard
            {
                All = { DepStatus.Closed, DepStatus.Open },
                Count = { [DepStatus.Closed] = 2 },
                ByName = { ["a"] = DepStatus.Closed }
            };

            var dto = new DepEnumMapper().Map(board);

            Assert.Equal([DepStatusDto.Closed, DepStatusDto.Open], dto.All);
            Assert.Equal(2, dto.Count[DepStatusDto.Closed]);
            Assert.Equal(DepStatusDto.Closed, dto.ByName["a"]);
        }
    }
}

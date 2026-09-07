// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // Zero-copy views: [GenerateView<S,T>] emits a nested `readonly ref struct` on the mapper whose properties
// evaluate the create map's member resolution LAZILY, against the source instance. Nothing is allocated and
// nothing is copied, and the `ref struct` is what makes that safe: the compiler will not let the view be
// stored in a field, boxed, captured by a lambda or held across an `await`, so it cannot outlive the source
// it borrows. Consume it at once; use Map where the result is kept.

    public enum VwKind
    {
        Draft,
        Published
    }

    public sealed class VwAuthor
    {
        public string Name { get; set; } = "";
    }

    public sealed class VwAuthorDto
    {
        public string Name { get; set; } = "";
    }

    public sealed class VwPost
    {
        public int Id { get; set; }

        public string Title { get; set; } = "";

        public VwKind Kind { get; set; }

        public VwAuthor? Author { get; set; }

        public List<string> Tags { get; set; } = [];
    }

    public sealed class VwPostDto
    {
        public int Id { get; set; }

        public string Title { get; set; } = "";

        public string Kind { get; set; } = "";

        public VwAuthorDto? Author { get; set; }

        public IReadOnlyList<string> Tags { get; set; } = new List<string>();
    }

    [DwarfMapper]
    [GenerateMap<VwPost, VwPostDto>]
    [GenerateView<VwPost, VwPostDto>]
    public partial class ViewMapper
    {
    }

    public class ViewRuntimeTests
    {
        private static VwPost Post()
        {
            return new VwPost
            {
                Id = 7,
                Title = "Digging",
                Kind = VwKind.Published,
                Author = new VwAuthor
                {
                    Name = "Gimli"
                },
                Tags =
                [
                    "deep", "bold"
                ]
            };
        }

        [Fact]
        public void Every_member_reads_through_to_the_source()
        {
            var mapper = new ViewMapper();
            var post = Post();

            var view = mapper.View(post);

            Assert.True(view.HasValue);
            Assert.Equal(7, view.Id);
            Assert.Equal("Digging", view.Title);
            Assert.Equal("Published", view.Kind);
            Assert.Equal("Gimli", view.Author.Name);
        }

        /// <summary>
        ///     A view and the map it mirrors agree member for member. This is the whole point of resolving a view
        ///     through the SAME member resolution: a view that quietly disagreed with <c>Map</c> would be the
        ///     endpoint-divergence defect this project's surface matrix exists to prevent, in a new place.
        /// </summary>
        [Fact]
        public void A_view_and_the_map_agree_member_for_member()
        {
            var mapper = new ViewMapper();
            var post = Post();

            var dto = mapper.Map(post);
            var view = mapper.View(post);

            Assert.Equal(dto.Id, view.Id);
            Assert.Equal(dto.Title, view.Title);
            Assert.Equal(dto.Kind, view.Kind);
            Assert.Equal(dto.Author!.Name, view.Author.Name);
            Assert.Equal(dto.Tags, view.Tags);
        }

        /// <summary>
        ///     A view is a WINDOW, not a snapshot: a source mutated after the view was created is visible through
        ///     it. That is the contract, not an accident of the implementation, so it is pinned — and it is
        ///     exactly why the type is a <c>ref struct</c> that cannot be stored.
        /// </summary>
        [Fact]
        public void A_source_mutation_after_the_view_was_created_is_visible_through_it()
        {
            var mapper = new ViewMapper();
            var post = Post();

            var view = mapper.View(post);
            Assert.Equal("Digging", view.Title);

            post.Title = "Digging deeper";
            Assert.Equal("Digging deeper", view.Title);
        }

        /// <summary>
        ///     A collection whose elements need no conversion is handed back AS the source collection — no copy.
        ///     That is the zero-copy claim at its most literal, and it is a real difference from <c>Map</c>,
        ///     which builds a new list.
        /// </summary>
        [Fact]
        public void A_collection_member_is_the_source_collection_itself_not_a_copy()
        {
            var mapper = new ViewMapper();
            var post = Post();

            var view = mapper.View(post);

            Assert.Same(post.Tags, view.Tags);
            Assert.NotSame(post.Tags, mapper.Map(post).Tags);
        }

        [Fact]
        public void A_null_nested_member_yields_a_view_whose_HasValue_is_false()
        {
            var mapper = new ViewMapper();
            var post = Post();
            post.Author = null;

            var view = mapper.View(post);

            Assert.False(view.Author.HasValue);
        }

        [Fact]
        public void A_null_source_is_refused_loudly()
        {
            var mapper = new ViewMapper();

            Assert.Throws<ArgumentNullException>(() => mapper.View(null!));
        }

        /// <summary>
        ///     The view itself allocates nothing. Measured on the SECOND read of the same members, after a warm
        ///     read has settled the enum-to-string converter's interned literals: what is being asserted is that
        ///     creating and reading a view costs no heap, not that every converter a view may call is free — one
        ///     that builds a string allocates on each access, and the docs say so.
        /// </summary>
        [Fact]
        public void Creating_and_reading_a_view_allocates_nothing()
        {
            var mapper = new ViewMapper();
            var post = Post();

            // Warm-up: JIT the property bodies and settle anything cached behind them.
            var warm = mapper.View(post);
            var sink = warm.Id + warm.Title.Length + warm.Kind.Length + warm.Tags.Count + warm.Author.Name.Length;
            Assert.True(sink > 0);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
            {
                var view = mapper.View(post);
                sink += view.Id + view.Title.Length + view.Kind.Length + view.Tags.Count + view.Author.Name.Length;
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(allocated == 0,
                $"100 view creations plus five member reads each allocated {allocated} bytes; a view must allocate nothing.");
            Assert.True(sink > 0);
        }
    }
}

// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;

namespace DwarfMapper.IntegrationTests;

// Proof that the constructs a real-world AutoMapper codebase leans on — which look like "AutoMapper-only"
// features — are all compile-time solvable with DwarfMapper. One region per contested construct. If this
// file compiles, the generator accepted every pattern; the asserts prove the runtime behaviour matches
// AutoMapper's.

// ───────────────────────── Pattern 1: Map(src, dst, typeof(A), typeof(B)) == update-into ─────────────────
// AutoMapper: _mapper.Map(item, existingItem, typeof(StoreItem), typeof(StoreItem)) — the types are literal,
// so it is just an update-into onto an existing instance (here same-type A->A, the StoreManager case).
public class FcStoreItem
{
    public string Name { get; set; } = "";
    public int Price { get; set; }
}

[DwarfMapper]
public partial class FcUpdateMapper
{
    public partial void Update(FcStoreItem src, FcStoreItem dest); // void update-into, same type
    public partial FcStoreItem UpdateFluent(FcStoreItem src, FcStoreItem dest); // fluent form
}

// ───────────────────────── Pattern 2: custom IEnumerable source (open-generic ConcurrentList<>→List<>) ───
// AutoMapper needed CreateMap(typeof(ConcurrentList<>), typeof(List<>)) + ITypeConverter because it did not
// recognise the custom type. DwarfMapper accepts the BCL interface IEnumerable<T> as the source, and a
// custom ConcurrentList<T> : IEnumerable<T> is assignable to it — no converter, no open generics.
public sealed class FcConcurrentList<T> : IEnumerable<T>
{
    private readonly List<T> _items = new();

    public IEnumerator<T> GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Add(T item)
    {
        _items.Add(item);
    }
}

public class FcMsgSrc
{
    public string Text { get; set; } = "";
}

public class FcMsgDst
{
    public string Text { get; set; } = "";
}

[DwarfMapper]
public partial class FcEnumerableMapper
{
    public partial List<int> MapInts(IEnumerable<int> src); // identity element
    public partial ICollection<FcMsgDst> MapMsgs(IEnumerable<FcMsgSrc> src); // element auto-nest
}

// ───────────────────────── Pattern 3: enum-keyed dictionary <-> flat per-platform document ──────────────
// AutoMapper built a Dictionary<BotPlatform, ClientAlertSettings> in an AfterMap/MapFrom that looped over
// the enum and called IRuntimeMapper. DwarfMapper: per-platform concrete maps + an [AfterMap] instance hook
// that calls them. All compile-time, AOT-safe.
public enum FcPlatform
{
    YouTube,
    Twitch
}

public abstract class FcClient
{
    public string Color { get; set; } = "";
}

public sealed class FcYt : FcClient
{
}

public sealed class FcTw : FcClient
{
}

public class FcYtDoc
{
    public string Color { get; set; } = "";
}

public class FcTwDoc
{
    public string Color { get; set; } = "";
}

public class FcAlertsDoc
{
    public FcYtDoc? YouTube { get; set; }
    public FcTwDoc? Twitch { get; set; }
}

public class FcAlerts
{
    public Dictionary<FcPlatform, FcClient> ClientAlerts { get; set; } = new();
}

[DwarfMapper]
public partial class FcAlertsMapper
{
    // Per-platform concrete maps (compile-time generated).
    public partial FcYtDoc MapYt(FcYt s);
    public partial FcTwDoc MapTw(FcTw s);
    public partial FcYt MapYt(FcYtDoc s);
    public partial FcTw MapTw(FcTwDoc s);

    // model -> doc: ignore the flat props in the auto-map; an AfterMap hook fills them from the dictionary.
    [MapIgnore(nameof(FcAlertsDoc.YouTube))]
    [MapIgnore(nameof(FcAlertsDoc.Twitch))]
    public partial FcAlertsDoc ToDoc(FcAlerts s);

    // doc -> model: ignore the dictionary; an AfterMap hook fills it from the flat props.
    [MapIgnore(nameof(FcAlerts.ClientAlerts))]
    public partial FcAlerts ToModel(FcAlertsDoc s);

    [AfterMap]
    private void FillDoc(FcAlerts s, FcAlertsDoc d)
    {
        if (s.ClientAlerts.TryGetValue(FcPlatform.YouTube, out var yt) && yt is FcYt y) d.YouTube = MapYt(y);
        if (s.ClientAlerts.TryGetValue(FcPlatform.Twitch, out var tw) && tw is FcTw t) d.Twitch = MapTw(t);
    }

    [AfterMap]
    private void FillModel(FcAlertsDoc s, FcAlerts d)
    {
        if (s.YouTube is not null) d.ClientAlerts[FcPlatform.YouTube] = MapYt(s.YouTube);
        if (s.Twitch is not null) d.ClientAlerts[FcPlatform.Twitch] = MapTw(s.Twitch);
    }
}

// ───────────────────────── Pattern 4: polymorphic Data (src.Data as DiscordBasicNotification) ───────────
public abstract class FcNote
{
}

public sealed class FcBasic : FcNote
{
    public string Msg { get; set; } = "";
}

public sealed class FcEmbed : FcNote
{
    public string Title { get; set; } = "";
}

public class FcNoteDto
{
}

public sealed class FcBasicDto : FcNoteDto
{
    public string Msg { get; set; } = "";
}

public sealed class FcEmbedDto : FcNoteDto
{
    public string Title { get; set; } = "";
}

[DwarfMapper]
public partial class FcNoteMapper
{
    [MapDerivedType<FcBasic, FcBasicDto>]
    [MapDerivedType<FcEmbed, FcEmbedDto>]
    public partial FcNoteDto Map(FcNote n);

    public partial FcBasicDto Map(FcBasic b);
    public partial FcEmbedDto Map(FcEmbed e);
}

public sealed class AutoMapperPatternsRuntimeTests
{
    [Fact]
    public void Pattern1_UpdateInto_SameType_mutates_existing_instance()
    {
        var mapper = new FcUpdateMapper();
        var existing = new FcStoreItem { Name = "old", Price = 1 };
        mapper.Update(new FcStoreItem { Name = "new", Price = 42 }, existing);
        Assert.Equal("new", existing.Name);
        Assert.Equal(42, existing.Price);

        var target = new FcStoreItem { Name = "x", Price = 0 };
        var returned = mapper.UpdateFluent(new FcStoreItem { Name = "y", Price = 7 }, target);
        Assert.Same(target, returned);
        Assert.Equal("y", target.Name);
    }

    [Fact]
    public void Pattern2_CustomEnumerable_source_maps_via_IEnumerable_param()
    {
        var mapper = new FcEnumerableMapper();

        var ints = new FcConcurrentList<int> { 1, 2, 3 }; // custom IEnumerable<int>
        var mappedInts = mapper.MapInts(ints);
        Assert.Equal(new[] { 1, 2, 3 }, mappedInts);

        var msgs = new FcConcurrentList<FcMsgSrc>();
        msgs.Add(new FcMsgSrc { Text = "hi" });
        var mappedMsgs = mapper.MapMsgs(msgs);
        Assert.Single(mappedMsgs);
        Assert.Equal("hi", mappedMsgs.First().Text);
    }

    [Fact]
    public void Pattern3_EnumKeyedDictionary_roundtrips_through_flat_document()
    {
        var mapper = new FcAlertsMapper();

        var model = new FcAlerts
        {
            ClientAlerts =
            {
                [FcPlatform.YouTube] = new FcYt { Color = "red" },
                [FcPlatform.Twitch] = new FcTw { Color = "purple" }
            }
        };

        var doc = mapper.ToDoc(model);
        Assert.Equal("red", doc.YouTube!.Color);
        Assert.Equal("purple", doc.Twitch!.Color);

        var back = mapper.ToModel(doc);
        Assert.Equal("red", ((FcYt)back.ClientAlerts[FcPlatform.YouTube]).Color);
        Assert.Equal("purple", ((FcTw)back.ClientAlerts[FcPlatform.Twitch]).Color);
    }

    [Fact]
    public void Pattern4_Polymorphic_base_source_dispatches_to_derived_map()
    {
        var mapper = new FcNoteMapper();

        FcNote basic = new FcBasic { Msg = "hello" };
        FcNote embed = new FcEmbed { Title = "title" };

        Assert.IsType<FcBasicDto>(mapper.Map(basic));
        Assert.IsType<FcEmbedDto>(mapper.Map(embed));
        Assert.Equal("hello", ((FcBasicDto)mapper.Map(basic)).Msg);
        Assert.Equal("title", ((FcEmbedDto)mapper.Map(embed)).Title);
    }
}

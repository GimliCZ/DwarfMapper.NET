// Research batch 3 (safe APIs only):
//  A. DTO VIEWS — a readonly ref struct over the source, consumed directly, vs map-to-class/struct then consume
//  B. ARENA consumption — build AND read every line: classes vs per-order arrays vs one arena + ranges
//  C. a FusedChat-shaped message (string-heavy: 5 strings, DateTime, bool, enum) — class vs struct gather vs view
//  D. FusedChat ButtonSlots: `ProfileButtonData?[8]` per profile vs an inline 8-slot struct with a validity mask
//  E. FusedChat `Dictionary<BotPlatform,int>` per element vs an enum-indexed inline array
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

// C: message shapes
public sealed class MsgDoc { public int OwnerId; public int Origin; public string Author = ""; public string AuthorId = ""; public string? AuthorImage; public string Message = ""; public string? MessageWithEmotes; public bool IsCommand; public DateTime PostedAt; }
public sealed class MsgC { public string Author = ""; public string AuthorId = ""; public string? AuthorImage; public string Message = ""; public string? MessageWithEmotes; public bool IsCommand; public int Origin; public DateTime PostedAt; }
public struct MsgS { public string Author; public string AuthorId; public string? AuthorImage; public string Message; public string? MessageWithEmotes; public bool IsCommand; public int Origin; public DateTime PostedAt; }
public readonly ref struct MsgView
{
    private readonly MsgDoc _s;
    public MsgView(MsgDoc s) => _s = s;
    public string Author => _s.Author; public string AuthorId => _s.AuthorId; public string? AuthorImage => _s.AuthorImage;
    public string Message => _s.Message; public string? MessageWithEmotes => _s.MessageWithEmotes;
    public bool IsCommand => _s.IsCommand; public int Origin => _s.Origin; public DateTime PostedAt => _s.PostedAt;
}
// A: view over the OrderC tree from PlanProbe
public readonly ref struct OrderView
{
    private readonly OrderC _s;
    public OrderView(OrderC s) => _s = s;
    public long Id => _s.Header.Id; public int Kind => _s.Header.Kind;
    public int ShipZip => _s.Ship.Zip; public int BillZip => _s.Bill.Zip;
    public long Amount => _s.Total.Amount; public int Currency => _s.Total.Currency;
}
// D: button slots
public sealed class ButtonC { public string Text = ""; public string Url = ""; public string Color = ""; public string Icon = ""; public bool IsFullWidth; }
public sealed class ProfileC { public int OwnerId; public string Name = ""; public ButtonC?[] ButtonSlots = new ButtonC?[8]; }
public sealed class ProfileDtoC { public int OwnerId; public string Name = ""; public ButtonC?[] ButtonSlots = new ButtonC?[8]; }
public struct ButtonS { public string Text, Url, Color, Icon; public bool IsFullWidth; }
[InlineArray(8)] public struct Slots8 { private ButtonS _e0; }
public struct ProfileS { public int OwnerId; public string Name; public byte SlotMask; public Slots8 Slots; }   // one element, no per-profile allocation
// E: per-platform counters
public sealed class StatsC { public int UserId; public Dictionary<int, int> MessageCounts = new(); }
public sealed class StatsDtoC { public int UserId; public Dictionary<int, int> MessageCounts = new(); }
[InlineArray(6)] public struct Counts6 { private int _e0; }
public struct StatsS { public int UserId; public Counts6 MessageCounts; }   // indexed by BotPlatform value (1..5)

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class PlanProbe3
{
    [Params(1_000, 100_000)]
    public int N;

    private OrderC[] _orders = null!; private OrderLinesC[] _ordC = null!; private MsgDoc[] _msgs = null!;
    private ProfileC[] _profiles = null!; private StatsC[] _stats = null!;
    private long[] _sink = null!;
    private static readonly string[] Words = Enumerable.Range(0, 64).Select(i => "w" + i).ToArray();
    private const int LinesPerOrder = 5;

    [GlobalSetup]
    public void Setup()
    {
        var rnd = new Random(11);
        _sink = new long[Math.Max(8, N * 8)];
        _orders = new OrderC[N]; _ordC = new OrderLinesC[N]; _msgs = new MsgDoc[N];
        _profiles = new ProfileC[Math.Max(1, N / 10)]; _stats = new StatsC[N];
        for (var i = 0; i < N; i++)
        {
            _orders[i] = new OrderC { Header = new HeaderC { Id = i, Kind = i & 7, Flags = i & 3 }, Ship = new AddressC { Street = i, City = 1, Zip = i + 2, Country = 3 }, Bill = new AddressC { Street = -i, City = 1, Zip = -i - 2, Country = 3 }, Total = new MoneyC { Amount = i * 100L, Currency = i & 3 } };
            var lines = new List<LineC>(LinesPerOrder);
            for (var l = 0; l < LinesPerOrder; l++) lines.Add(new LineC { Sku = i * 10 + l, Qty = l + 1, Price = 100L * l });
            _ordC[i] = new OrderLinesC { Id = i, Lines = lines };
            _msgs[i] = new MsgDoc { OwnerId = i, Origin = 1 + (i % 5), Author = Words[i & 63], AuthorId = Words[(i + 1) & 63], AuthorImage = (i & 3) == 0 ? null : Words[(i + 2) & 63], Message = Words[(i + 3) & 63], MessageWithEmotes = (i & 1) == 0 ? null : Words[(i + 4) & 63], IsCommand = (i & 7) == 0, PostedAt = new DateTime(2026, 1, 1).AddSeconds(i) };
            _stats[i] = new StatsC { UserId = i, MessageCounts = new Dictionary<int, int> { [1] = i, [2] = i + 1, [4] = i + 2 } };
        }
        for (var p = 0; p < _profiles.Length; p++)
        {
            var pr = new ProfileC { OwnerId = p, Name = Words[p & 63] };
            for (var s = 0; s < 8; s++) if (((p + s) & 3) != 0) pr.ButtonSlots[s] = new ButtonC { Text = Words[s], Url = Words[s + 8], Color = Words[s + 16], Icon = Words[s + 24], IsFullWidth = (s & 1) == 0 };
            _profiles[p] = pr;
        }
        // self-checks: every consumer produces the same sink
        var a = A_MapToClasses_ThenConsume(); var b = A_View_Consume(); var c = A_MapToStructs_ThenConsume();
        if (a != b || a != c) throw new InvalidOperationException("A sinks differ");
        var d = B_Classes_BuildAndRead(); var e = B_Arena_BuildAndRead(); var f = B_PerOrderArrays_BuildAndRead();
        if (d != e || d != f) throw new InvalidOperationException("B sinks differ");
        var g = C_Msg_MapToClass_ThenConsume(); var h = C_Msg_View_Consume(); var k = C_Msg_MapToStruct_ThenConsume();
        if (g != h || g != k) throw new InvalidOperationException("C sinks differ");
        var m = D_Profiles_ClassSlots(); var n = D_Profiles_InlineSlots();
        for (var p = 0; p < m.Length; p++) for (var s = 0; s < 8; s++) { var has = m[p].ButtonSlots[s] is not null; if (has != (((n[p].SlotMask >> s) & 1) == 1)) throw new InvalidOperationException("D slot mask wrong"); if (has && m[p].ButtonSlots[s]!.Url != n[p].Slots[s].Url) throw new InvalidOperationException("D slot wrong"); }
        var o = E_Stats_DictionaryCopy(); var q = E_Stats_InlineCounts();
        for (var i = 0; i < o.Length; i++) if (o[i].MessageCounts[2] != q[i].MessageCounts[2]) throw new InvalidOperationException("E wrong");
    }

    // ── A. views vs map-then-consume (the consumer reads 6 fields per order into a sink) ───────────────
    [Benchmark(Baseline = true), BenchmarkCategory("A_view")]
    public long A_MapToClasses_ThenConsume()
    {
        var r = new OrderDtoC[_orders.Length];
        for (var i = 0; i < r.Length; i++) { var s = _orders[i]; r[i] = new OrderDtoC { Header = new HeaderC { Id = s.Header.Id, Kind = s.Header.Kind, Flags = s.Header.Flags }, Ship = new AddressC { Street = s.Ship.Street, City = s.Ship.City, Zip = s.Ship.Zip, Country = s.Ship.Country }, Bill = new AddressC { Street = s.Bill.Street, City = s.Bill.City, Zip = s.Bill.Zip, Country = s.Bill.Country }, Total = new MoneyC { Amount = s.Total.Amount, Currency = s.Total.Currency } }; }
        long acc = 0; var sink = _sink;
        for (var i = 0; i < r.Length; i++) { var d = r[i]; var o = i * 6; sink[o] = d.Header.Id; sink[o + 1] = d.Header.Kind; sink[o + 2] = d.Ship.Zip; sink[o + 3] = d.Bill.Zip; sink[o + 4] = d.Total.Amount; sink[o + 5] = d.Total.Currency; acc += d.Total.Amount + d.Ship.Zip; }
        return acc;
    }

    [Benchmark, BenchmarkCategory("A_view")]
    public long A_MapToStructs_ThenConsume()
    {
        var r = new OrderS[_orders.Length];
        for (var i = 0; i < r.Length; i++) { var s = _orders[i]; r[i] = new OrderS { Header = new HeaderS { Id = s.Header.Id, Kind = s.Header.Kind, Flags = s.Header.Flags }, Ship = new AddressS { Street = s.Ship.Street, City = s.Ship.City, Zip = s.Ship.Zip, Country = s.Ship.Country }, Bill = new AddressS { Street = s.Bill.Street, City = s.Bill.City, Zip = s.Bill.Zip, Country = s.Bill.Country }, Total = new MoneyS { Amount = s.Total.Amount, Currency = s.Total.Currency } }; }
        long acc = 0; var sink = _sink;
        for (var i = 0; i < r.Length; i++) { ref readonly var d = ref r[i]; var o = i * 6; sink[o] = d.Header.Id; sink[o + 1] = d.Header.Kind; sink[o + 2] = d.Ship.Zip; sink[o + 3] = d.Bill.Zip; sink[o + 4] = d.Total.Amount; sink[o + 5] = d.Total.Currency; acc += d.Total.Amount + d.Ship.Zip; }
        return acc;
    }

    [Benchmark, BenchmarkCategory("A_view")]
    public long A_View_Consume()
    {
        long acc = 0; var sink = _sink;
        for (var i = 0; i < _orders.Length; i++) { var d = new OrderView(_orders[i]); var o = i * 6; sink[o] = d.Id; sink[o + 1] = d.Kind; sink[o + 2] = d.ShipZip; sink[o + 3] = d.BillZip; sink[o + 4] = d.Amount; sink[o + 5] = d.Currency; acc += d.Amount + d.ShipZip; }
        return acc;
    }

    // ── B. arena: build the result AND read every line ─────────────────────────────────────────────────
    [Benchmark(Baseline = true), BenchmarkCategory("B_arena")]
    public long B_Classes_BuildAndRead()
    {
        var r = new OrderLinesDtoC[_ordC.Length];
        for (var i = 0; i < r.Length; i++) { var s = _ordC[i]; var lines = new List<LineC>(s.Lines.Count); foreach (var l in s.Lines) lines.Add(new LineC { Sku = l.Sku, Qty = l.Qty, Price = l.Price }); r[i] = new OrderLinesDtoC { Id = s.Id, Lines = lines }; }
        long acc = 0;
        for (var i = 0; i < r.Length; i++) foreach (var l in r[i].Lines) acc += l.Price * l.Qty + l.Sku;
        return acc;
    }

    [Benchmark, BenchmarkCategory("B_arena")]
    public long B_PerOrderArrays_BuildAndRead()
    {
        var r = new OrderLinesS[_ordC.Length];
        for (var i = 0; i < r.Length; i++) { var s = _ordC[i]; var lines = new LineS[s.Lines.Count]; for (var l = 0; l < lines.Length; l++) { var x = s.Lines[l]; lines[l] = new LineS { Sku = x.Sku, Qty = x.Qty, Price = x.Price }; } r[i] = new OrderLinesS { Id = s.Id, Lines = lines }; }
        long acc = 0;
        for (var i = 0; i < r.Length; i++) { var ls = r[i].Lines; for (var l = 0; l < ls.Length; l++) { ref readonly var x = ref ls[l]; acc += x.Price * x.Qty + x.Sku; } }
        return acc;
    }

    [Benchmark, BenchmarkCategory("B_arena")]
    public long B_Arena_BuildAndRead()
    {
        var total = 0; for (var i = 0; i < _ordC.Length; i++) total += _ordC[i].Lines.Count;
        var lines = new LineS[total]; var orders = new OrderRangeS[_ordC.Length]; var pos = 0;
        for (var i = 0; i < orders.Length; i++) { var s = _ordC[i]; orders[i] = new OrderRangeS { Id = s.Id, LineOffset = pos, LineCount = s.Lines.Count }; foreach (var x in s.Lines) lines[pos++] = new LineS { Sku = x.Sku, Qty = x.Qty, Price = x.Price }; }
        long acc = 0;
        for (var i = 0; i < orders.Length; i++) { var span = lines.AsSpan(orders[i].LineOffset, orders[i].LineCount); for (var l = 0; l < span.Length; l++) { ref readonly var x = ref span[l]; acc += x.Price * x.Qty + x.Sku; } }
        return acc;
    }

    // ── C. FusedChat-shaped message (string-heavy) ─────────────────────────────────────────────────────
    [Benchmark(Baseline = true), BenchmarkCategory("C_msg")]
    public long C_Msg_MapToClass_ThenConsume()
    {
        var r = new MsgC[_msgs.Length];
        for (var i = 0; i < r.Length; i++) { var s = _msgs[i]; r[i] = new MsgC { Author = s.Author, AuthorId = s.AuthorId, AuthorImage = s.AuthorImage, Message = s.Message, MessageWithEmotes = s.MessageWithEmotes, IsCommand = s.IsCommand, Origin = s.Origin, PostedAt = s.PostedAt }; }
        long acc = 0;
        for (var i = 0; i < r.Length; i++) { var d = r[i]; acc += d.Message.Length + d.Author.Length + d.Origin + (d.IsCommand ? 1 : 0) + d.PostedAt.Second; }
        return acc;
    }

    [Benchmark, BenchmarkCategory("C_msg")]
    public long C_Msg_MapToStruct_ThenConsume()
    {
        var r = new MsgS[_msgs.Length];
        for (var i = 0; i < r.Length; i++) { var s = _msgs[i]; r[i] = new MsgS { Author = s.Author, AuthorId = s.AuthorId, AuthorImage = s.AuthorImage, Message = s.Message, MessageWithEmotes = s.MessageWithEmotes, IsCommand = s.IsCommand, Origin = s.Origin, PostedAt = s.PostedAt }; }
        long acc = 0;
        for (var i = 0; i < r.Length; i++) { ref readonly var d = ref r[i]; acc += d.Message.Length + d.Author.Length + d.Origin + (d.IsCommand ? 1 : 0) + d.PostedAt.Second; }
        return acc;
    }

    [Benchmark, BenchmarkCategory("C_msg")]
    public long C_Msg_View_Consume()
    {
        long acc = 0;
        for (var i = 0; i < _msgs.Length; i++) { var d = new MsgView(_msgs[i]); acc += d.Message.Length + d.Author.Length + d.Origin + (d.IsCommand ? 1 : 0) + d.PostedAt.Second; }
        return acc;
    }

    // ── D. ButtonSlots: 8 nullable class slots per profile vs inline slots + mask ───────────────────────
    [Benchmark(Baseline = true), BenchmarkCategory("D_slots")]
    public ProfileDtoC[] D_Profiles_ClassSlots()
    {
        var r = new ProfileDtoC[_profiles.Length];
        for (var p = 0; p < r.Length; p++)
        {
            var s = _profiles[p]; var d = new ProfileDtoC { OwnerId = s.OwnerId, Name = s.Name };
            for (var i = 0; i < 8; i++) { var b = s.ButtonSlots[i]; d.ButtonSlots[i] = b is null ? null : new ButtonC { Text = b.Text, Url = b.Url, Color = b.Color, Icon = b.Icon, IsFullWidth = b.IsFullWidth }; }
            r[p] = d;
        }
        return r;
    }

    [Benchmark, BenchmarkCategory("D_slots")]
    public ProfileS[] D_Profiles_InlineSlots()
    {
        var r = new ProfileS[_profiles.Length];
        for (var p = 0; p < r.Length; p++)
        {
            var s = _profiles[p]; var d = new ProfileS { OwnerId = s.OwnerId, Name = s.Name };
            for (var i = 0; i < 8; i++) { var b = s.ButtonSlots[i]; if (b is null) continue; d.SlotMask |= (byte)(1 << i); d.Slots[i] = new ButtonS { Text = b.Text, Url = b.Url, Color = b.Color, Icon = b.Icon, IsFullWidth = b.IsFullWidth }; }
            r[p] = d;
        }
        return r;
    }

    // ── E. per-platform counters: Dictionary<enum,int> per element vs enum-indexed inline array ────────
    [Benchmark(Baseline = true), BenchmarkCategory("E_counts")]
    public StatsDtoC[] E_Stats_DictionaryCopy()
    {
        var r = new StatsDtoC[_stats.Length];
        for (var i = 0; i < r.Length; i++) { var s = _stats[i]; var d = new StatsDtoC { UserId = s.UserId, MessageCounts = new Dictionary<int, int>(s.MessageCounts.Count) }; foreach (var kv in s.MessageCounts) d.MessageCounts[kv.Key] = kv.Value; r[i] = d; }
        return r;
    }

    [Benchmark, BenchmarkCategory("E_counts")]
    public StatsS[] E_Stats_InlineCounts()
    {
        var r = new StatsS[_stats.Length];
        for (var i = 0; i < r.Length; i++) { var s = _stats[i]; var d = new StatsS { UserId = s.UserId }; foreach (var kv in s.MessageCounts) d.MessageCounts[kv.Key] = kv.Value; r[i] = d; }
        return r;
    }
}

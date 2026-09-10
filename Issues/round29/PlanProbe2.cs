// Research batch 2 (safe APIs only): the open pathways after the decomposition verdict.
//  A. optional nested member (Nullable<T>) — does the root still blit, and what does it cost?
//  B. 1:N members (List<Line> per order) — per-order arrays vs one flattened arena with (offset,count) ranges
//  C. reference leaves in struct arrays — the GC cost of scanning a large struct[] that holds strings
//  D. layout hygiene — the same fields in a padding-heavy order vs a packed order
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

// A
public struct OptS { public long Id; public AddressS? Ship; public MoneyS Total; }        // Nullable<AddressS>: 1 B flag + 16 B, padded
public struct OptSDto { public long Id; public AddressS? Ship; public MoneyS Total; }
public sealed class OptC { public long Id; public AddressC? Ship; public MoneyC Total = null!; }
// B
public sealed class LineC { public int Sku; public int Qty; public long Price; }
public sealed class OrderLinesC { public long Id; public List<LineC> Lines = null!; }
public sealed class OrderLinesDtoC { public long Id; public List<LineC> Lines = null!; }
public struct LineS { public int Sku; public int Qty; public long Price; }
public struct OrderLinesS { public long Id; public LineS[] Lines; }                        // per-order array (a reference field)
public struct OrderRangeS { public long Id; public int LineOffset; public int LineCount; } // arena: ranges into one LineS[]
public struct ArenaResult { public OrderRangeS[] Orders; public LineS[] Lines; }
// C
public struct WithRefS { public int A; public string Name; public long B; }
public struct NoRefS { public int A; public int NameId; public long B; }
// D
public struct PaddedS { public bool Flag; public long Id; public byte Kind; public double Value; public short Code; }   // 40 B with padding
public struct PackedS { public long Id; public double Value; public short Code; public byte Kind; public bool Flag; }   // 24 B
public struct PaddedSDto { public bool Flag; public long Id; public byte Kind; public double Value; public short Code; }
public struct PackedSDto { public long Id; public double Value; public short Code; public byte Kind; public bool Flag; }

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class PlanProbe2
{
    [Params(1_000, 100_000)]
    public int N;

    private OptC[] _optC = null!; private OptS[] _optS = null!;
    private OrderLinesC[] _ordC = null!;
    private WithRefS[] _withRef = null!; private NoRefS[] _noRef = null!; private string[] _names = null!;
    private PaddedS[] _padded = null!; private PackedS[] _packed = null!;
    private const int LinesPerOrder = 5;

    [GlobalSetup]
    public void Setup()
    {
        _optC = new OptC[N]; _optS = new OptS[N]; _ordC = new OrderLinesC[N];
        _withRef = new WithRefS[N]; _noRef = new NoRefS[N]; _padded = new PaddedS[N]; _packed = new PackedS[N];
        _names = new string[64];
        for (var i = 0; i < 64; i++) _names[i] = "name-" + i;
        for (var i = 0; i < N; i++)
        {
            var hasShip = (i & 3) != 0;
            _optC[i] = new OptC { Id = i, Ship = hasShip ? new AddressC { Street = i, City = 1, Zip = 2, Country = 3 } : null, Total = new MoneyC { Amount = i, Currency = 1 } };
            _optS[i] = new OptS { Id = i, Ship = hasShip ? new AddressS { Street = i, City = 1, Zip = 2, Country = 3 } : null, Total = new MoneyS { Amount = i, Currency = 1 } };
            var lines = new List<LineC>(LinesPerOrder);
            for (var l = 0; l < LinesPerOrder; l++) lines.Add(new LineC { Sku = i * 10 + l, Qty = l + 1, Price = 100L * l });
            _ordC[i] = new OrderLinesC { Id = i, Lines = lines };
            _withRef[i] = new WithRefS { A = i, Name = _names[i & 63], B = i * 2L };
            _noRef[i] = new NoRefS { A = i, NameId = i & 63, B = i * 2L };
            _padded[i] = new PaddedS { Flag = (i & 1) == 0, Id = i, Kind = (byte)(i & 7), Value = i * 0.5, Code = (short)(i & 127) };
            _packed[i] = new PackedS { Flag = (i & 1) == 0, Id = i, Kind = (byte)(i & 7), Value = i * 0.5, Code = (short)(i & 127) };
        }
        // self-checks
        var a = Opt_Structs_Blit(); var b = Opt_Structs_ScalarCopy();
        for (var i = 0; i < N; i++) if (a[i].Ship.HasValue != b[i].Ship.HasValue || (a[i].Ship.HasValue && a[i].Ship!.Value.Street != b[i].Ship!.Value.Street) || a[i].Total.Amount != b[i].Total.Amount) throw new InvalidOperationException("Nullable blit wrong at " + i);
        var c = Lines_Arena(); var d = Lines_PerOrderArray();
        for (var i = 0; i < N; i++) { if (c.Orders[i].LineCount != d[i].Lines.Length) throw new InvalidOperationException("arena count wrong"); for (var l = 0; l < LinesPerOrder; l++) if (c.Lines[c.Orders[i].LineOffset + l].Sku != d[i].Lines[l].Sku) throw new InvalidOperationException("arena wrong at " + i); }
        var e = Padded_Blit(); var f = Packed_Blit();
        for (var i = 0; i < N; i++) if (e[i].Id != f[i].Id || e[i].Value != f[i].Value || e[i].Code != f[i].Code) throw new InvalidOperationException("layout wrong at " + i);
        if (Marshal.SizeOf<PaddedS>() == Marshal.SizeOf<PackedS>()) throw new InvalidOperationException("padding fixture is not padded");
    }

    // ── A. optional nested member ─────────────────────────────────────────────────────────────────────
    [Benchmark(Baseline = true), BenchmarkCategory("A_optional")]
    public OptC[] Opt_Classes_FieldCopy()
    {
        var r = new OptC[_optC.Length];
        for (var i = 0; i < r.Length; i++)
        {
            var s = _optC[i];
            r[i] = new OptC { Id = s.Id, Ship = s.Ship is null ? null : new AddressC { Street = s.Ship.Street, City = s.Ship.City, Zip = s.Ship.Zip, Country = s.Ship.Country }, Total = new MoneyC { Amount = s.Total.Amount, Currency = s.Total.Currency } };
        }
        return r;
    }

    [Benchmark, BenchmarkCategory("A_optional")]
    public OptS[] Opt_Classes_To_Structs_Gather()
    {
        var r = new OptS[_optC.Length];
        for (var i = 0; i < r.Length; i++)
        {
            var s = _optC[i];
            r[i] = new OptS { Id = s.Id, Ship = s.Ship is null ? null : new AddressS { Street = s.Ship.Street, City = s.Ship.City, Zip = s.Ship.Zip, Country = s.Ship.Country }, Total = new MoneyS { Amount = s.Total.Amount, Currency = s.Total.Currency } };
        }
        return r;
    }

    [Benchmark, BenchmarkCategory("A_optional")]
    public OptSDto[] Opt_Structs_ScalarCopy()
    {
        var r = new OptSDto[_optS.Length];
        for (var i = 0; i < r.Length; i++) { ref readonly var s = ref _optS[i]; r[i] = new OptSDto { Id = s.Id, Ship = s.Ship, Total = s.Total }; }
        return r;
    }

    [Benchmark, BenchmarkCategory("A_optional")]
    public OptSDto[] Opt_Structs_Blit()
    {
        // Nullable<AddressS> vs Nullable<AddressS>: same T, so the same layout — the proof extension's premise.
        var r = new OptSDto[_optS.Length];
        MemoryMarshal.Cast<OptS, OptSDto>(new ReadOnlySpan<OptS>(_optS)).CopyTo(r);
        return r;
    }

    // ── B. 1:N members ────────────────────────────────────────────────────────────────────────────────
    [Benchmark(Baseline = true), BenchmarkCategory("B_lines")]
    public OrderLinesDtoC[] Lines_Classes()
    {
        var r = new OrderLinesDtoC[_ordC.Length];
        for (var i = 0; i < r.Length; i++)
        {
            var s = _ordC[i];
            var lines = new List<LineC>(s.Lines.Count);
            foreach (var l in s.Lines) lines.Add(new LineC { Sku = l.Sku, Qty = l.Qty, Price = l.Price });
            r[i] = new OrderLinesDtoC { Id = s.Id, Lines = lines };
        }
        return r;
    }

    [Benchmark, BenchmarkCategory("B_lines")]
    public OrderLinesS[] Lines_PerOrderArray()
    {
        var r = new OrderLinesS[_ordC.Length];
        for (var i = 0; i < r.Length; i++)
        {
            var s = _ordC[i];
            var lines = new LineS[s.Lines.Count];
            for (var l = 0; l < lines.Length; l++) { var x = s.Lines[l]; lines[l] = new LineS { Sku = x.Sku, Qty = x.Qty, Price = x.Price }; }
            r[i] = new OrderLinesS { Id = s.Id, Lines = lines };
        }
        return r;
    }

    [Benchmark, BenchmarkCategory("B_lines")]
    public ArenaResult Lines_Arena()
    {
        // One allocation for every order's lines; each order carries (offset, count). Two allocations total.
        var total = 0;
        for (var i = 0; i < _ordC.Length; i++) total += _ordC[i].Lines.Count;
        var lines = new LineS[total];
        var orders = new OrderRangeS[_ordC.Length];
        var pos = 0;
        for (var i = 0; i < orders.Length; i++)
        {
            var s = _ordC[i];
            orders[i] = new OrderRangeS { Id = s.Id, LineOffset = pos, LineCount = s.Lines.Count };
            foreach (var x in s.Lines) lines[pos++] = new LineS { Sku = x.Sku, Qty = x.Qty, Price = x.Price };
        }
        return new ArenaResult { Orders = orders, Lines = lines };
    }

    // ── C. reference leaves in a struct array: what a full GC costs with the array alive ─────────────
    [Benchmark(Baseline = true), BenchmarkCategory("C_gcscan")]
    public int Gc_Collect_WithRefArrayAlive()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        return _withRef.Length + _names.Length;
    }

    [Benchmark, BenchmarkCategory("C_gcscan")]
    public int Gc_Collect_NoRefArrayAlive()
    {
        // Note: BOTH arrays are alive in both benchmarks (fields); the difference is which one the JIT keeps
        // "used". This measures a full GC with the whole fixture alive — read the C section's note in the doc.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        return _noRef.Length;
    }

    [Benchmark, BenchmarkCategory("C_gather")]
    public WithRefS[] Gather_WithStringLeaf()
    {
        var r = new WithRefS[_withRef.Length];
        for (var i = 0; i < r.Length; i++) { ref readonly var s = ref _withRef[i]; r[i] = new WithRefS { A = s.A, Name = s.Name, B = s.B }; }
        return r;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("C_gather")]
    public NoRefS[] Gather_NoRef()
    {
        var r = new NoRefS[_noRef.Length];
        for (var i = 0; i < r.Length; i++) { ref readonly var s = ref _noRef[i]; r[i] = new NoRefS { A = s.A, NameId = s.NameId, B = s.B }; }
        return r;
    }

    // ── D. layout hygiene ────────────────────────────────────────────────────────────────────────────
    [Benchmark(Baseline = true), BenchmarkCategory("D_layout")]
    public PaddedSDto[] Padded_Blit()
    {
        var r = new PaddedSDto[_padded.Length];
        MemoryMarshal.Cast<PaddedS, PaddedSDto>(new ReadOnlySpan<PaddedS>(_padded)).CopyTo(r);
        return r;
    }

    [Benchmark, BenchmarkCategory("D_layout")]
    public PackedSDto[] Packed_Blit()
    {
        var r = new PackedSDto[_packed.Length];
        MemoryMarshal.Cast<PackedS, PackedSDto>(new ReadOnlySpan<PackedS>(_packed)).CopyTo(r);
        return r;
    }

    [Benchmark, BenchmarkCategory("D_layout")]
    public PackedSDto[] Padded_To_Packed_Scalar()
    {
        // What a layout-hygiene reorder costs when only ONE side is packed: the scalar loop instead of the blit.
        var r = new PackedSDto[_padded.Length];
        for (var i = 0; i < r.Length; i++) { ref readonly var s = ref _padded[i]; r[i] = new PackedSDto { Id = s.Id, Value = s.Value, Code = s.Code, Kind = s.Kind, Flag = s.Flag }; }
        return r;
    }
}

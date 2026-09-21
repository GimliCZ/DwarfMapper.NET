// Plan simulation: each strategy from Issues/round29/RESEARCH-hardware-mode.md, written with the SAFE APIs it would
// ship with (no unsafe, no uninitialized memory), measured against the scalar loop it would replace.
// Every fast variant is checked against the scalar result in GlobalSetup, so a wrong shuffle fails loudly.
using System.Buffers;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

// ── shapes ───────────────────────────────────────────────────────────────────────────────────────────
// (P) permutation: same bytes, reversed field order
public struct Q16S { public float X, Y, Z, W; }
public struct Q16D { public float W, Z, Y, X; }
public struct V3S { public float X, Y, Z; }
public struct V3R { public float Z, Y, X; }
// (W) width change inside a homogeneous struct
public struct V3Dbl { public double X, Y, Z; }
// (M) mixed types, all widths double
public struct MixS { public int A; public float B; public int C; public float D; }
public struct MixD { public long A; public double B; public long C; public double D; }
// (D) a DTO tree: root + 3 nested transfer models, 4 allocations per element as classes
public sealed class MoneyC { public long Amount; public int Currency; }
public sealed class AddressC { public int Street; public int City; public int Zip; public int Country; }
public sealed class HeaderC { public long Id; public int Kind; public int Flags; }
public sealed class OrderC { public HeaderC Header = null!; public AddressC Ship = null!; public AddressC Bill = null!; public MoneyC Total = null!; }
public sealed class OrderDtoC { public HeaderC Header = null!; public AddressC Ship = null!; public AddressC Bill = null!; public MoneyC Total = null!; }
public struct MoneyS { public long Amount; public int Currency; }
public struct AddressS { public int Street, City, Zip, Country; }
public struct HeaderS { public long Id; public int Kind, Flags; }
public struct OrderS { public HeaderS Header; public AddressS Ship; public AddressS Bill; public MoneyS Total; }        // 48 B, unmanaged
public struct OrderSDto { public HeaderS Header; public AddressS Ship; public AddressS Bill; public MoneyS Total; }    // layout-identical twin
public struct OrderOptS { public HeaderS Header; public AddressS? Ship; public AddressS Bill; public MoneyS Total; }   // optional nested (Nullable<T>)
public struct OrderOptSDto { public HeaderS Header; public AddressS? Ship; public AddressS Bill; public MoneyS Total; }
public struct OrderStrS { public HeaderS Header; public AddressS Ship; public string Note; public MoneyS Total; }      // a reference leaf: gather only

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class PlanProbe
{
    [Params(1_000, 100_000)]
    public int N;

    private Q16S[] _q = null!; private Q16D[] _qOut = null!;
    private V3S[] _v = null!; private V3R[] _vOut = null!; private V3Dbl[] _vDbl = null!; private V3S[] _vSpanOut = null!;
    private MixS[] _m = null!; private MixD[] _mOut = null!;
    private OrderC[] _orders = null!; private OrderS[] _orderStructs = null!;
    private Vector128<byte> _ctrl16;
    private (Vector128<byte> P, Vector128<byte> A, Vector128<byte> B)[] _ctrl12 = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rnd = new Random(7);
        _q = new Q16S[N]; _qOut = new Q16D[N];
        _v = new V3S[N]; _vOut = new V3R[N]; _vDbl = new V3Dbl[N]; _vSpanOut = new V3S[N];
        _m = new MixS[N]; _mOut = new MixD[N];
        for (var i = 0; i < N; i++)
        {
            _q[i] = new Q16S { X = i, Y = i + 0.25f, Z = i + 0.5f, W = rnd.NextSingle() };
            _v[i] = new V3S { X = i, Y = i + 0.5f, Z = rnd.NextSingle() };
            _m[i] = new MixS { A = i, B = i * 0.5f, C = -i, D = rnd.NextSingle() };
        }
        _orders = new OrderC[N]; _orderStructs = new OrderS[N];
        for (var i = 0; i < N; i++)
        {
            _orders[i] = new OrderC
            {
                Header = new HeaderC { Id = i, Kind = i & 7, Flags = i & 3 },
                Ship = new AddressC { Street = i, City = i + 1, Zip = i + 2, Country = i & 15 },
                Bill = new AddressC { Street = -i, City = -i - 1, Zip = -i - 2, Country = i & 15 },
                Total = new MoneyC { Amount = i * 100L, Currency = i & 3 },
            };
            _orderStructs[i] = new OrderS
            {
                Header = new HeaderS { Id = i, Kind = i & 7, Flags = i & 3 },
                Ship = new AddressS { Street = i, City = i + 1, Zip = i + 2, Country = i & 15 },
                Bill = new AddressS { Street = -i, City = -i - 1, Zip = -i - 2, Country = i & 15 },
                Total = new MoneyS { Amount = i * 100L, Currency = i & 3 },
            };
        }

        // control vectors from the two layouts: dest byte -> source byte (per struct), padding = none here
        _ctrl16 = BuildControl16(size: 16, fieldMap: new[] { (src: 0, dst: 12, len: 4), (src: 4, dst: 8, len: 4), (src: 8, dst: 4, len: 4), (src: 12, dst: 0, len: 4) });
        _ctrl12 = BuildControlPeriod(size: 12, fieldMap: new[] { (src: 0, dst: 8, len: 4), (src: 4, dst: 4, len: 4), (src: 8, dst: 0, len: 4) });

        // self-checks
        var a = Perm16_Scalar(); var b = Perm16_ByteShuffle();
        for (var i = 0; i < N; i++) if (a[i].X != b[i].X || a[i].Y != b[i].Y || a[i].Z != b[i].Z || a[i].W != b[i].W) throw new InvalidOperationException("Perm16 shuffle wrong at " + i);
        var c = Perm12_Scalar(); var d = Perm12_ByteShuffle();
        for (var i = 0; i < N; i++) if (c[i].X != d[i].X || c[i].Y != d[i].Y || c[i].Z != d[i].Z) throw new InvalidOperationException("Perm12 shuffle wrong at " + i);
        var e = Widen_Scalar(); var f = Widen_TensorPrimitives();
        for (var i = 0; i < N; i++) if (e[i].X != f[i].X || e[i].Y != f[i].Y || e[i].Z != f[i].Z) throw new InvalidOperationException("Widen wrong at " + i);
        var g = Mixed_Scalar(); var h = Mixed_ColumnTranspose();
        for (var i = 0; i < N; i++) if (g[i].A != h[i].A || g[i].B != h[i].B || g[i].C != h[i].C || g[i].D != h[i].D) throw new InvalidOperationException("Mixed wrong at " + i);
        var s1 = Tree_Classes_FieldCopy(); var s2 = Tree_Classes_To_NestedStructs_Gather();
        for (var i = 0; i < N; i++) if (s1[i].Ship.Zip != s2[i].Ship.Zip || s1[i].Total.Amount != s2[i].Total.Amount) throw new InvalidOperationException("Tree wrong at " + i);
    }

    // ── (D) DTO tree decomposition ─────────────────────────────────────────────────────────────────────
    [Benchmark(Baseline = true), BenchmarkCategory("D_tree")]
    public OrderDtoC[] Tree_Classes_FieldCopy()
    {
        var r = new OrderDtoC[_orders.Length];
        for (var i = 0; i < r.Length; i++)
        {
            var s = _orders[i];
            r[i] = new OrderDtoC
            {
                Header = new HeaderC { Id = s.Header.Id, Kind = s.Header.Kind, Flags = s.Header.Flags },
                Ship = new AddressC { Street = s.Ship.Street, City = s.Ship.City, Zip = s.Ship.Zip, Country = s.Ship.Country },
                Bill = new AddressC { Street = s.Bill.Street, City = s.Bill.City, Zip = s.Bill.Zip, Country = s.Bill.Country },
                Total = new MoneyC { Amount = s.Total.Amount, Currency = s.Total.Currency },
            };
        }
        return r;
    }

    [Benchmark, BenchmarkCategory("D_tree")]
    public OrderS[] Tree_Classes_To_NestedStructs_Gather()
    {
        var r = new OrderS[_orders.Length];
        for (var i = 0; i < r.Length; i++)
        {
            var s = _orders[i];
            r[i] = new OrderS
            {
                Header = new HeaderS { Id = s.Header.Id, Kind = s.Header.Kind, Flags = s.Header.Flags },
                Ship = new AddressS { Street = s.Ship.Street, City = s.Ship.City, Zip = s.Ship.Zip, Country = s.Ship.Country },
                Bill = new AddressS { Street = s.Bill.Street, City = s.Bill.City, Zip = s.Bill.Zip, Country = s.Bill.Country },
                Total = new MoneyS { Amount = s.Total.Amount, Currency = s.Total.Currency },
            };
        }
        return r;
    }

    [Benchmark, BenchmarkCategory("D_tree")]
    public OrderStrS[] Tree_Classes_To_NestedStructs_WithStringLeaf_Gather()
    {
        var r = new OrderStrS[_orders.Length];
        for (var i = 0; i < r.Length; i++)
        {
            var s = _orders[i];
            r[i] = new OrderStrS
            {
                Header = new HeaderS { Id = s.Header.Id, Kind = s.Header.Kind, Flags = s.Header.Flags },
                Ship = new AddressS { Street = s.Ship.Street, City = s.Ship.City, Zip = s.Ship.Zip, Country = s.Ship.Country },
                Note = "n",
                Total = new MoneyS { Amount = s.Total.Amount, Currency = s.Total.Currency },
            };
        }
        return r;
    }

    [Benchmark, BenchmarkCategory("D_tree")]
    public OrderSDto[] Tree_NestedStructs_Blit()
    {
        var r = new OrderSDto[_orderStructs.Length];
        MemoryMarshal.Cast<OrderS, OrderSDto>(new ReadOnlySpan<OrderS>(_orderStructs)).CopyTo(r);
        return r;
    }

    [Benchmark, BenchmarkCategory("D_tree")]
    public OrderSDto[] Tree_NestedStructs_ScalarCopy()
    {
        var r = new OrderSDto[_orderStructs.Length];
        for (var i = 0; i < r.Length; i++)
        {
            ref readonly var s = ref _orderStructs[i];
            r[i] = new OrderSDto { Header = s.Header, Ship = s.Ship, Bill = s.Bill, Total = s.Total };
        }
        return r;
    }

    // ── (S2) span map: element loop vs blit into a caller-owned buffer ─────────────────────────────────
    [Benchmark(Baseline = true), BenchmarkCategory("S2_spanmap")]
    public int SpanMap_ElementLoop()
    {
        ReadOnlySpan<V3S> src = _v; Span<V3S> dst = _vSpanOut;
        for (var i = 0; i < src.Length; i++) dst[i] = new V3S { X = src[i].X, Y = src[i].Y, Z = src[i].Z };
        return dst.Length;
    }

    [Benchmark, BenchmarkCategory("S2_spanmap")]
    public int SpanMap_Blit()
    {
        ReadOnlySpan<V3S> src = _v; Span<V3S> dst = _vSpanOut;
        MemoryMarshal.Cast<V3S, V3S>(src).CopyTo(dst);
        return dst.Length;
    }

    // ── (P) permuted blit: byte-granular Vector128.Shuffle, safe span APIs only ────────────────────────
    [Benchmark(Baseline = true), BenchmarkCategory("P16_perm")]
    public Q16D[] Perm16_Scalar()
    {
        var r = new Q16D[_q.Length];
        for (var i = 0; i < r.Length; i++) { ref readonly var s = ref _q[i]; r[i] = new Q16D { W = s.W, Z = s.Z, Y = s.Y, X = s.X }; }
        return r;
    }

    [Benchmark, BenchmarkCategory("P16_perm")]
    public Q16D[] Perm16_ByteShuffle()
    {
        var r = new Q16D[_q.Length];
        var src = MemoryMarshal.AsBytes(new ReadOnlySpan<Q16S>(_q));
        var dst = MemoryMarshal.AsBytes(new Span<Q16D>(r));
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
            for (; i + 16 <= src.Length; i += 16)
                Vector128.Shuffle(Vector128.Create(src.Slice(i, 16)), _ctrl16).CopyTo(dst.Slice(i, 16));
        for (var e = i / 16; e < r.Length; e++) { ref readonly var s = ref _q[e]; r[e] = new Q16D { W = s.W, Z = s.Z, Y = s.Y, X = s.X }; }
        return r;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("P12_perm")]
    public V3R[] Perm12_Scalar()
    {
        var r = new V3R[_v.Length];
        for (var i = 0; i < r.Length; i++) { ref readonly var s = ref _v[i]; r[i] = new V3R { Z = s.Z, Y = s.Y, X = s.X }; }
        return r;
    }

    [Benchmark, BenchmarkCategory("P12_perm")]
    public V3R[] Perm12_ByteShuffle()
    {
        // 12-byte structs: the byte pattern repeats every lcm(12,16) = 48 bytes = 3 output windows. An output
        // window draws from up to THREE consecutive 16-byte input windows (previous, current, next) — a reversed
        // field order pulls a struct's last destination bytes from source bytes that precede the window — so each
        // window is the OR of three shuffles (out-of-range index -> 0). Scalar head (elements 0-1) and tail.
        var r = new V3R[_v.Length];
        var src = MemoryMarshal.AsBytes(new ReadOnlySpan<V3S>(_v));
        var dst = MemoryMarshal.AsBytes(new Span<V3R>(r));
        var i = 0;
        if (Vector128.IsHardwareAccelerated && src.Length >= 48)
        {
            for (var e = 0; e < 2; e++) { ref readonly var s = ref _v[e]; r[e] = new V3R { Z = s.Z, Y = s.Y, X = s.X }; }
            for (i = 16; i + 32 <= src.Length; i += 16)
            {
                var pv = Vector128.Create(src.Slice(i - 16, 16));
                var a = Vector128.Create(src.Slice(i, 16));
                var b = Vector128.Create(src.Slice(i + 16, 16));
                var (cp, ca, cb) = _ctrl12[(i / 16) % 3];
                (Vector128.Shuffle(pv, cp) | Vector128.Shuffle(a, ca) | Vector128.Shuffle(b, cb)).CopyTo(dst.Slice(i, 16));
            }
        }
        // scalar tail from the first element the vector loop did not fully write
        for (var e = i / 12; e < r.Length; e++) { ref readonly var s = ref _v[e]; r[e] = new V3R { Z = s.Z, Y = s.Y, X = s.X }; }
        return r;
    }

    // ── (W) width change inside a homogeneous struct via TensorPrimitives ─────────────────────────────
    [Benchmark(Baseline = true), BenchmarkCategory("W_widen")]
    public V3Dbl[] Widen_Scalar()
    {
        var r = new V3Dbl[_v.Length];
        for (var i = 0; i < r.Length; i++) { ref readonly var s = ref _v[i]; r[i] = new V3Dbl { X = s.X, Y = s.Y, Z = s.Z }; }
        return r;
    }

    [Benchmark, BenchmarkCategory("W_widen")]
    public V3Dbl[] Widen_TensorPrimitives()
    {
        var r = new V3Dbl[_v.Length];
        TensorPrimitives.ConvertChecked<float, double>(MemoryMarshal.Cast<V3S, float>(_v), MemoryMarshal.Cast<V3Dbl, double>(r.AsSpan()));
        return r;
    }

    // ── (M) mixed widths: AoS -> SoA columns -> TensorPrimitives -> AoS ───────────────────────────────
    [Benchmark(Baseline = true), BenchmarkCategory("M_mixed")]
    public MixD[] Mixed_Scalar()
    {
        var r = new MixD[_m.Length];
        for (var i = 0; i < r.Length; i++) { ref readonly var s = ref _m[i]; r[i] = new MixD { A = s.A, B = s.B, C = s.C, D = s.D }; }
        return r;
    }

    [Benchmark, BenchmarkCategory("M_mixed")]
    public MixD[] Mixed_ColumnTranspose()
    {
        var n = _m.Length;
        var r = new MixD[n];
        var ints = ArrayPool<int>.Shared.Rent(n); var longs = ArrayPool<long>.Shared.Rent(n);
        var floats = ArrayPool<float>.Shared.Rent(n); var doubles = ArrayPool<double>.Shared.Rent(n);
        try
        {
            // column A (int -> long)
            for (var i = 0; i < n; i++) ints[i] = _m[i].A;
            TensorPrimitives.ConvertChecked<int, long>(ints.AsSpan(0, n), longs.AsSpan(0, n));
            for (var i = 0; i < n; i++) r[i].A = longs[i];
            // column C
            for (var i = 0; i < n; i++) ints[i] = _m[i].C;
            TensorPrimitives.ConvertChecked<int, long>(ints.AsSpan(0, n), longs.AsSpan(0, n));
            for (var i = 0; i < n; i++) r[i].C = longs[i];
            // column B (float -> double)
            for (var i = 0; i < n; i++) floats[i] = _m[i].B;
            TensorPrimitives.ConvertChecked<float, double>(floats.AsSpan(0, n), doubles.AsSpan(0, n));
            for (var i = 0; i < n; i++) r[i].B = doubles[i];
            // column D
            for (var i = 0; i < n; i++) floats[i] = _m[i].D;
            TensorPrimitives.ConvertChecked<float, double>(floats.AsSpan(0, n), doubles.AsSpan(0, n));
            for (var i = 0; i < n; i++) r[i].D = doubles[i];
        }
        finally
        {
            ArrayPool<int>.Shared.Return(ints); ArrayPool<long>.Shared.Return(longs);
            ArrayPool<float>.Shared.Return(floats); ArrayPool<double>.Shared.Return(doubles);
        }
        return r;
    }

    // ── control-vector builders (what the generator would compute at generation time) ──────────────────
    private static Vector128<byte> BuildControl16(int size, (int src, int dst, int len)[] fieldMap)
    {
        Span<byte> ctrl = stackalloc byte[16];
        ctrl.Fill(0x80); // out-of-range index -> 0 (padding)
        foreach (var (src, dst, len) in fieldMap)
            for (var b = 0; b < len; b++) ctrl[dst + b] = (byte)(src + b);
        return Vector128.Create((ReadOnlySpan<byte>)ctrl);
    }

    private static (Vector128<byte> P, Vector128<byte> A, Vector128<byte> B)[] BuildControlPeriod(int size, (int src, int dst, int len)[] fieldMap)
    {
        // period = lcm(size, 16) bytes = period/16 output windows; dest byte p (absolute within period) belongs to
        // struct p/size at offset p%size, whose source byte is at struct*size + srcOffset(offset).
        var period = size * 16 / Gcd(size, 16);
        var windows = period / 16;
        var srcOf = new int[size];
        foreach (var (src, dst, len) in fieldMap) for (var b = 0; b < len; b++) srcOf[dst + b] = src + b;
        var result = new (Vector128<byte>, Vector128<byte>, Vector128<byte>)[windows];
        Span<byte> cp = stackalloc byte[16]; Span<byte> ca = stackalloc byte[16]; Span<byte> cb = stackalloc byte[16];
        for (var w = 0; w < windows; w++)
        {
            cp.Fill(0x80); ca.Fill(0x80); cb.Fill(0x80);
            for (var j = 0; j < 16; j++)
            {
                var p = w * 16 + j;                 // absolute dest byte within the period
                var s = (p / size) * size + srcOf[p % size]; // absolute source byte within the period
                var rel = s - w * 16;               // relative to this window's CURRENT input vector
                if (rel >= -16 && rel < 0) cp[j] = (byte)(rel + 16);
                else if (rel >= 0 && rel < 16) ca[j] = (byte)rel;
                else if (rel >= 16 && rel < 32) cb[j] = (byte)(rel - 16);
                else throw new InvalidOperationException("source byte outside the three-vector window");
            }
            result[w] = (Vector128.Create((ReadOnlySpan<byte>)cp), Vector128.Create((ReadOnlySpan<byte>)ca), Vector128.Create((ReadOnlySpan<byte>)cb));
        }
        return result;
    }

    private static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }
}

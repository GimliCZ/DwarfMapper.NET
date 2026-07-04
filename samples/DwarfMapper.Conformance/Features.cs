// SPDX-License-Identifier: GPL-2.0-only
using System.Globalization;
using DwarfMapper;

namespace DwarfMapper.Conformance;

public static class R
{
    public static int Pass, Fail;
    public static void Check(string feat, bool ok, string detail = "")
    {
        if (ok) { Pass++; Console.WriteLine($"  ✓ {feat,-26} {detail}"); }
        else { Fail++; Console.WriteLine($"  ✗ FAIL {feat,-21} {detail}"); }
    }
    public static bool Throws<TEx>(Action a) where TEx : Exception
    { try { a(); return false; } catch (TEx) { return true; } catch { return false; } }
    public static bool NoThrow(Action a) { try { a(); return true; } catch { return false; } }
}

// ── F01 flat by-name ────────────────────────────────────────────────────────
public class F01S { public int Id { get; set; } public string Name { get; set; } = ""; }
public class F01D { public int Id { get; set; } public string Name { get; set; } = ""; }
[DwarfMapper] public partial class F01M { public partial F01D Map(F01S s); }

// ── F02 rename ──────────────────────────────────────────────────────────────
public class F02S { public string FullName { get; set; } = ""; }
public class F02D { public string Name { get; set; } = ""; }
[DwarfMapper] public partial class F02M
{
    [MapProperty(nameof(F02S.FullName), nameof(F02D.Name))]
    public partial F02D Map(F02S s);
}

// ── F03 built-in conversions (widen / enum->string / parse / DateTime / lift) ─
public enum Col { Red, Green }
public class F03S { public int Small { get; set; } public Col Colour { get; set; } public string Num { get; set; } = ""; public DateTime When { get; set; } public int Lift { get; set; } }
public class F03D { public long Small { get; set; } public string Colour { get; set; } = ""; public int Num { get; set; } public string When { get; set; } = ""; public int? Lift { get; set; } }
[DwarfMapper] public partial class F03M { public partial F03D Map(F03S s); }

// ── F04 enum by VALUE ───────────────────────────────────────────────────────
public enum Src4 { A = 5, B = 9 }
public enum Dst4 { X = 5, Y = 9 }
public class F04S { public Src4 E { get; set; } }
public class F04D { public Dst4 E { get; set; } }
[DwarfMapper(EnumStrategy = EnumStrategy.ByValue)] public partial class F04M { public partial F04D Map(F04S s); }

// ── F05 nested object (auto-synth) ──────────────────────────────────────────
public class Inner5 { public int V { get; set; } }
public class Inner5D { public int V { get; set; } }
public class F05S { public Inner5 Inner { get; set; } = new(); }
public class F05D { public Inner5D Inner { get; set; } = new(); }
[DwarfMapper] public partial class F05M { public partial F05D Map(F05S s); }

// ── F06 collections (List + array) ──────────────────────────────────────────
public class F06S { public List<int> Nums { get; set; } = new(); public string[] Tags { get; set; } = Array.Empty<string>(); }
public class F06D { public List<int> Nums { get; set; } = new(); public string[] Tags { get; set; } = Array.Empty<string>(); }
[DwarfMapper] public partial class F06M { public partial F06D Map(F06S s); }

// ── F07 null-collection strategy = AsNull ───────────────────────────────────
public class F07S { public List<int>? Nums { get; set; } }
public class F07D { public List<int>? Nums { get; set; } }
[DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)] public partial class F07M { public partial F07D Map(F07S s); }

// ── F08 deep / dotted source path ───────────────────────────────────────────
public class Addr8 { public string City { get; set; } = ""; }
public class F08S { public Addr8 Address { get; set; } = new(); }
public class F08D { public string City { get; set; } = ""; }
[DwarfMapper] public partial class F08M
{
    [MapProperty("Address.City", nameof(F08D.City))]
    public partial F08D Map(F08S s);
}

// ── F09 flatten ─────────────────────────────────────────────────────────────
public class Addr9 { public string City { get; set; } = ""; public string Zip { get; set; } = ""; }
public class F09S { public Addr9 Address { get; set; } = new(); }
public class F09D { public string City { get; set; } = ""; public string Zip { get; set; } = ""; }
[DwarfMapper] public partial class F09M
{
    [Flatten(nameof(F09S.Address))]
    public partial F09D Map(F09S s);
}

// ── F10 custom converter (Use=) ─────────────────────────────────────────────
public class F10S { public decimal Total { get; set; } }
public class F10D { public string Total { get; set; } = ""; }
[DwarfMapper] public partial class F10M
{
    [MapProperty(nameof(F10S.Total), nameof(F10D.Total), Use = nameof(Money))]
    public partial F10D Map(F10S s);
    private static string Money(decimal d) => d.ToString("C", CultureInfo.GetCultureInfo("en-US"));
}

// ── F11 null substitute ─────────────────────────────────────────────────────
public class F11S { public string? Nickname { get; set; } }
public class F11D { public string Nickname { get; set; } = ""; }
[DwarfMapper] public partial class F11M
{
    [MapProperty(nameof(F11S.Nickname), nameof(F11D.Nickname), NullSubstitute = "(none)")]
    public partial F11D Map(F11S s);
}

// ── F12 conditional (When=) ─────────────────────────────────────────────────
public class F12S { public int Score { get; set; } public bool Active { get; set; } }
public class F12D { public int Score { get; set; } }
[DwarfMapper] public partial class F12M
{
    [MapProperty(nameof(F12S.Score), nameof(F12D.Score), When = nameof(IsActive))]
    public partial F12D Map(F12S s);
    private static bool IsActive(F12S s) => s.Active;
}

// ── F13 MapValue (constant + computed) ──────────────────────────────────────
public class F13S { public int Id { get; set; } }
public class F13D { public int Id { get; set; } public string Tier { get; set; } = ""; public string Stamp { get; set; } = ""; }
[DwarfMapper] public partial class F13M
{
    [MapValue(nameof(F13D.Tier), "guild")]
    [MapValue(nameof(F13D.Stamp), Use = nameof(Stamp))]
    public partial F13D Map(F13S s);
    private static string Stamp() => "v1";
}

// ── F14 record target (ctor mapping) ────────────────────────────────────────
public class F14S { public int Id { get; set; } public string Name { get; set; } = ""; }
public record F14D(int Id, string Name);
[DwarfMapper] public partial class F14M { public partial F14D Map(F14S s); }

// ── F15 before/after hooks ──────────────────────────────────────────────────
public class F15S { public int Id { get; set; } }
public class F15D { public int Id { get; set; } public int Touched { get; set; } }
[DwarfMapper] public partial class F15M
{
    [MapIgnore(nameof(F15D.Touched))]
    public partial F15D Map(F15S s);
    [BeforeMap] private static void Before(F15S s) { s.Id += 0; }
    [AfterMap] private static void After(F15S s, F15D d) => d.Touched = 99;
}

// ── F16 reverse map ─────────────────────────────────────────────────────────
public class F16A { public string FullName { get; set; } = ""; }
public class F16B { public string Name { get; set; } = ""; }
[DwarfMapper] public partial class F16M
{
    [MapProperty(nameof(F16A.FullName), nameof(F16B.Name))]
    [ReverseMap]
    public partial F16B Forward(F16A a);
    public partial F16A Backward(F16B b);
}

// ── F17 ignore destination ──────────────────────────────────────────────────
public class F17S { public int Id { get; set; } }
public class F17D { public int Id { get; set; } public string? Secret { get; set; } }
[DwarfMapper] public partial class F17M
{
    [MapIgnore(nameof(F17D.Secret))]
    public partial F17D Map(F17S s);
}

// ── F18 [MapTo] registry front door (generates extension method) ────────────
[MapTo(typeof(F18D))]
public class F18S { public int Id { get; set; } public string Name { get; set; } = ""; }
public class F18D { public int Id { get; set; } public string Name { get; set; } = ""; }

// ── F19 polymorphism ([MapDerivedType]) ─────────────────────────────────────
public abstract class Animal19 { public int Id { get; set; } }
public class Dog19 : Animal19 { public string Breed { get; set; } = ""; }
public abstract class Animal19D { public int Id { get; set; } }
public class Dog19D : Animal19D { public string Breed { get; set; } = ""; }
[DwarfMapper] public partial class F19M
{
    [MapDerivedType<Dog19, Dog19D>]
    public partial Animal19D Map(Animal19 a);
    public partial Dog19D Map(Dog19 d);
}

// ── F20 IQueryable projection ───────────────────────────────────────────────
public class F20S { public int Id { get; set; } public string Name { get; set; } = ""; }
public class F20D { public int Id { get; set; } public string Name { get; set; } = ""; }
[DwarfMapper] public partial class F20M
{
    public partial System.Linq.IQueryable<F20D> Project(System.Linq.IQueryable<F20S> q);
}

// ── F21 reference handling / cycles (Preserve) ──────────────────────────────
public class Node21 { public int Id { get; set; } public Node21? Next { get; set; } }
public class Node21D { public int Id { get; set; } public Node21D? Next { get; set; } }
[DwarfMapper(AutoNest = true, ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class F21M { public partial Node21D Map(Node21 n); }

// ── F22 NameConvention.Flexible (snake_case <-> PascalCase) ──────────────────
public class F22S { public string user_name { get; set; } = ""; }
public class F22D { public string UserName { get; set; } = ""; }
[DwarfMapper(NameConvention = NameConvention.Flexible)] public partial class F22M { public partial F22D Map(F22S s); }

// ── F23 [DwarfMapperConstructor] (explicit ctor selection on target) ─────────
public class F23S { public int Id { get; set; } public string Name { get; set; } = ""; }
public class F23D
{
    public int Id { get; } public string Name { get; }
    public F23D() { Id = -1; Name = "?"; }
    [DwarfMapperConstructor] public F23D(int id, string name) { Id = id; Name = name; }
}
[DwarfMapper(CaseInsensitive = true)] public partial class F23M { public partial F23D Map(F23S s); }  // id<->Id

// ── F24 [GenerateMap<S,D>] (no partial method written) ──────────────────────
public class F24S { public int Id { get; set; } }
public class F24D { public int Id { get; set; } }
[DwarfMapper]
[GenerateMap<F24S, F24D>]
public partial class F24M { }

// ── F25 RequiredMapping=Both + [MapIgnoreSource] (source coverage) ───────────
public class F25S { public int Id { get; set; } public string Internal { get; set; } = ""; }
public class F25D { public int Id { get; set; } }
[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)] public partial class F25M
{
    [MapIgnoreSource(nameof(F25S.Internal))]
    public partial F25D Map(F25S s);
}

// ═══════════════════ BATCH 4: remaining features ════════════════════════════

// ── F26 [FlattenGraph] + polymorphism (tree graph -> flat list) ─────────────
public abstract class FsNode { public string Name { get; set; } = ""; }
public class Folder : FsNode { public List<FsNode> Children { get; set; } = new(); }
public class FileN : FsNode { public long Size { get; set; } }
public abstract class FsNodeDto { public string Name { get; set; } = ""; }
public class FolderDto : FsNodeDto { public List<FsNodeDto>? Children { get; set; } }
public class FileNDto : FsNodeDto { public long Size { get; set; } }
public class Tree { public FsNode? Root { get; set; } public string Label { get; set; } = ""; }
public class TreeDto { public List<FsNodeDto> Nodes { get; set; } = new(); public string Label { get; set; } = ""; }
[DwarfMapper] public partial class F26M
{
    [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
    [MapDerivedType<Folder, FolderDto>]
    [MapDerivedType<FileN, FileNDto>]
    public partial TreeDto Map(Tree t);
}

// ── F27 [Reinterpret] (struct blit across differently-named fields) ──────────
public struct Px { public int A; public int B; }
public struct Qx { public int X; public int Y; }
public class ReSrc { public Px[] V { get; set; } = Array.Empty<Px>(); }
public class ReDst { public Qx[] V { get; set; } = Array.Empty<Qx>(); }
[DwarfMapper] public partial class F27M
{
    [Reinterpret(nameof(ReDst.V))]   // blit Px[] -> Qx[] (identical layout, differently-named fields)
    public partial ReDst Map(ReSrc s);
}

// ── F28 OnCycle = SetNull (cycle broken by nulling the back-ref) ─────────────
public class CnA { public int Id { get; set; } public CnA? Next { get; set; } }
public class CnB { public int Id { get; set; } public CnB? Next { get; set; } }
[DwarfMapper(AutoNest = true, OnCycle = OnCycleStrategy.SetNull)]
public partial class F28M { public partial CnB Map(CnA a); }

// ── F29 ambient registry (public [GenerateMap] self-registers on load) ──────
public sealed class AmbSrc { public int V { get; set; } }
public sealed class AmbDst { public int V { get; set; } }
[DwarfMapper]
[GenerateMap<AmbSrc, AmbDst>]
public partial class F29M { }

// ── F30 [RoundTrip] (fuzz-verified backward(forward(x)) == x) ────────────────
public class RtA { public int Id { get; set; } public string Name { get; set; } = ""; }
public class RtB { public int Id { get; set; } public string Name { get; set; } = ""; }
[DwarfMapper] public partial class F30M
{
    [RoundTrip] public partial RtB ToB(RtA a);
    public partial RtA ToA(RtB b);
}

// ═══════════════════ BATCH 5: dirty / unexpected paths ═══════════════════════

// D2 narrowing overflow (long -> int, CreateChecked throws OverflowException)
public class OvS { public long V { get; set; } }
public class OvD { public int V { get; set; } }
[DwarfMapper] public partial class DovM { public partial OvD Map(OvS s); }

// D3 unparseable string -> int (IParsable.Parse throws FormatException)
public class PsS { public string V { get; set; } = ""; }
public class PsD { public int V { get; set; } }
[DwarfMapper] public partial class DpsM { public partial PsD Map(PsS s); }

// D4 cycle with OnCycle=Throw (default) -> throws at runtime
public class CtA { public int Id { get; set; } public CtA? Next { get; set; } }
public class CtB { public int Id { get; set; } public CtB? Next { get; set; } }
[DwarfMapper(AutoNest = true, OnCycle = OnCycleStrategy.Throw)]
public partial class DctM { public partial CtB Map(CtA a); }

// D5 nested source null -> nested dest null (defensive)
public class NnInner { public int V { get; set; } }
public class NnInnerD { public int V { get; set; } }
public class NnS { public NnInner? Inner { get; set; } }
public class NnD { public NnInnerD? Inner { get; set; } }
[DwarfMapper] public partial class DnnM { public partial NnD Map(NnS s); }

// D6 default null-collection strategy (AsEmpty): null source list -> empty list
public class EcS { public List<int>? Xs { get; set; } }
public class EcD { public List<int> Xs { get; set; } = new(); }
[DwarfMapper] public partial class DecM { public partial EcD Map(EcS s); }

// D7 enum by-value with an UNDEFINED numeric value (raw value passes through)
public enum RawE { A = 1 }
public class ReS { public int Code { get; set; } }
public class ReD { public RawE Code { get; set; } }
[DwarfMapper] public partial class DreM { public partial ReD Map(ReS s); }

// D8 unicode + empty-string passthrough
public class UniS { public string Text { get; set; } = ""; }
public class UniD { public string Text { get; set; } = ""; }
[DwarfMapper] public partial class DuniM { public partial UniD Map(UniS s); }

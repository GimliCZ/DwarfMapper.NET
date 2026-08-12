// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Conformance;

public static class R
{
    public static int Pass, Fail;

    public static void Check(string feat, bool ok, string detail = "")
    {
        if (ok)
        {
            Pass++;
            Console.WriteLine($"  ✓ {feat,-26} {detail}");
        }
        else
        {
            Fail++;
            Console.WriteLine($"  ✗ FAIL {feat,-21} {detail}");
        }
    }

    public static bool Throws<TEx>(Action a) where TEx : Exception
    {
        try
        {
            a();
            return false;
        }
        catch (TEx)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool NoThrow(Action a)
    {
        try
        {
            a();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

// ── F01 flat by-name ────────────────────────────────────────────────────────
public class F01S
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class F01D
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[DwarfMapper]
public partial class F01M
{
    public partial F01D Map(F01S s);
}

// ── F02 rename ──────────────────────────────────────────────────────────────
public class F02S
{
    public string FullName { get; set; } = "";
}

public class F02D
{
    public string Name { get; set; } = "";
}

[DwarfMapper]
public partial class F02M
{
    [MapProperty(nameof(F02S.FullName), nameof(F02D.Name))]
    public partial F02D Map(F02S s);
}

// ── F03 built-in conversions (widen / enum->string / parse / DateTime / lift) ─
public enum Col
{
    Red,
    Green
}

public class F03S
{
    public int Small { get; set; }
    public Col Colour { get; set; }
    public string Num { get; set; } = "";
    public DateTime When { get; set; }
    public int Lift { get; set; }
}

public class F03D
{
    public long Small { get; set; }
    public string Colour { get; set; } = "";
    public int Num { get; set; }
    public string When { get; set; } = "";
    public int? Lift { get; set; }
}

[DwarfMapper]
public partial class F03M
{
    public partial F03D Map(F03S s);
}

// ── F04 enum by VALUE ───────────────────────────────────────────────────────
public enum Src4
{
    A = 5,
    B = 9
}

public enum Dst4
{
    X = 5,
    Y = 9
}

public class F04S
{
    public Src4 E { get; set; }
}

public class F04D
{
    public Dst4 E { get; set; }
}

[DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]
public partial class F04M
{
    public partial F04D Map(F04S s);
}

// ── F05 nested object (auto-synth) ──────────────────────────────────────────
public class Inner5
{
    public int V { get; set; }
}

public class Inner5D
{
    public int V { get; set; }
}

public class F05S
{
    public Inner5 Inner { get; set; } = new();
}

public class F05D
{
    public Inner5D Inner { get; set; } = new();
}

[DwarfMapper]
public partial class F05M
{
    public partial F05D Map(F05S s);
}

// ── F06 collections (List + array) ──────────────────────────────────────────
public class F06S
{
    public List<int> Nums { get; set; } = new();
    public string[] Tags { get; set; } = Array.Empty<string>();
}

public class F06D
{
    public List<int> Nums { get; set; } = new();
    public string[] Tags { get; set; } = Array.Empty<string>();
}

[DwarfMapper]
public partial class F06M
{
    public partial F06D Map(F06S s);
}

// ── F07 null-collection strategy = AsNull ───────────────────────────────────
public class F07S
{
    public List<int>? Nums { get; set; }
}

public class F07D
{
    public List<int>? Nums { get; set; }
}

[DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
public partial class F07M
{
    public partial F07D Map(F07S s);
}

// ── F08 deep / dotted source path ───────────────────────────────────────────
public class Addr8
{
    public string City { get; set; } = "";
}

public class F08S
{
    public Addr8 Address { get; set; } = new();
}

public class F08D
{
    public string City { get; set; } = "";
}

[DwarfMapper]
public partial class F08M
{
    [MapProperty("Address.City", nameof(F08D.City))]
    public partial F08D Map(F08S s);
}

// ── F09 flatten ─────────────────────────────────────────────────────────────
public class Addr9
{
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}

public class F09S
{
    public Addr9 Address { get; set; } = new();
}

public class F09D
{
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}

[DwarfMapper]
public partial class F09M
{
    [Flatten(nameof(F09S.Address))]
    public partial F09D Map(F09S s);
}

// ── F10 custom converter (Use=) ─────────────────────────────────────────────
public class F10S
{
    public decimal Total { get; set; }
}

public class F10D
{
    public string Total { get; set; } = "";
}

[DwarfMapper]
public partial class F10M
{
    [MapProperty(nameof(F10S.Total), nameof(F10D.Total), Use = nameof(Money))]
    public partial F10D Map(F10S s);

    private static string Money(decimal d)
    {
        return d.ToString("C", CultureInfo.GetCultureInfo("en-US"));
    }
}

// ── F11 null substitute ─────────────────────────────────────────────────────
public class F11S
{
    public string? Nickname { get; set; }
}

public class F11D
{
    public string Nickname { get; set; } = "";
}

[DwarfMapper]
public partial class F11M
{
    [MapProperty(nameof(F11S.Nickname), nameof(F11D.Nickname), NullSubstitute = "(none)")]
    public partial F11D Map(F11S s);
}

// ── F12 conditional (When=) ─────────────────────────────────────────────────
public class F12S
{
    public int Score { get; set; }
    public bool Active { get; set; }
}

public class F12D
{
    public int Score { get; set; }
}

[DwarfMapper]
public partial class F12M
{
    [MapProperty(nameof(F12S.Score), nameof(F12D.Score), When = nameof(IsActive))]
    public partial F12D Map(F12S s);

    private static bool IsActive(F12S s)
    {
        return s.Active;
    }
}

// ── F13 MapValue (constant + computed) ──────────────────────────────────────
public class F13S
{
    public int Id { get; set; }
}

public class F13D
{
    public int Id { get; set; }
    public string Tier { get; set; } = "";
    public string Stamp { get; set; } = "";
}

[DwarfMapper]
public partial class F13M
{
    [MapValue(nameof(F13D.Tier), "guild")]
    [MapValue(nameof(F13D.Stamp), Use = nameof(Stamp))]
    public partial F13D Map(F13S s);

    private static string Stamp()
    {
        return "v1";
    }
}

// ── F14 record target (ctor mapping) ────────────────────────────────────────
public class F14S
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public record F14D(int Id, string Name);

[DwarfMapper]
public partial class F14M
{
    public partial F14D Map(F14S s);
}

// ── F15 before/after hooks ──────────────────────────────────────────────────
public class F15S
{
    public int Id { get; set; }
}

public class F15D
{
    public int Id { get; set; }
    public int Touched { get; set; }
}

[DwarfMapper]
public partial class F15M
{
    [MapIgnore(nameof(F15D.Touched))]
    public partial F15D Map(F15S s);

    [BeforeMap]
    private static void Before(F15S s)
    {
        s.Id += 0;
    }

    [AfterMap]
    private static void After(F15S s, F15D d)
    {
        d.Touched = 99;
    }
}

// ── F16 reverse map ─────────────────────────────────────────────────────────
public class F16A
{
    public string FullName { get; set; } = "";
}

public class F16B
{
    public string Name { get; set; } = "";
}

[DwarfMapper]
public partial class F16M
{
    [MapProperty(nameof(F16A.FullName), nameof(F16B.Name))]
    [ReverseMap]
    public partial F16B Forward(F16A a);

    public partial F16A Backward(F16B b);
}

// ── F17 ignore destination ──────────────────────────────────────────────────
public class F17S
{
    public int Id { get; set; }
}

public class F17D
{
    public int Id { get; set; }
    public string? Secret { get; set; }
}

[DwarfMapper]
public partial class F17M
{
    [MapIgnore(nameof(F17D.Secret))]
    public partial F17D Map(F17S s);
}

// ── F18 [MapTo] registry front door (generates extension method) ────────────
[MapTo(typeof(F18D))]
public class F18S
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class F18D
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

// ── F19 polymorphism ([MapDerivedType]) ─────────────────────────────────────
public abstract class Animal19
{
    public int Id { get; set; }
}

public class Dog19 : Animal19
{
    public string Breed { get; set; } = "";
}

public abstract class Animal19D
{
    public int Id { get; set; }
}

public class Dog19D : Animal19D
{
    public string Breed { get; set; } = "";
}

[DwarfMapper]
public partial class F19M
{
    [MapDerivedType<Dog19, Dog19D>]
    public partial Animal19D Map(Animal19 a);

    public partial Dog19D Map(Dog19 d);
}

// ── F20 IQueryable projection ───────────────────────────────────────────────
public class F20S
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class F20D
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[DwarfMapper]
public partial class F20M
{
    public partial IQueryable<F20D> Project(IQueryable<F20S> q);
}

// ── F21 reference handling / cycles (Preserve) ──────────────────────────────
public class Node21
{
    public int Id { get; set; }
    public Node21? Next { get; set; }
}

public class Node21D
{
    public int Id { get; set; }
    public Node21D? Next { get; set; }
}

[DwarfMapper(AutoNest = true, ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class F21M
{
    public partial Node21D Map(Node21 n);
}

// ── F22 NameConvention.Flexible (snake_case <-> PascalCase) ──────────────────
public class F22S
{
    public string user_name { get; set; } = "";
}

public class F22D
{
    public string UserName { get; set; } = "";
}

[DwarfMapper(NameConvention = NameConvention.Flexible)]
public partial class F22M
{
    public partial F22D Map(F22S s);
}

// ── F23 [DwarfMapperConstructor] (explicit ctor selection on target) ─────────
public class F23S
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class F23D
{
    public F23D()
    {
        Id = -1;
        Name = "?";
    }

    [DwarfMapperConstructor]
    public F23D(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; }
    public string Name { get; }
}

[DwarfMapper(CaseInsensitive = true)]
public partial class F23M
{
    public partial F23D Map(F23S s);
} // id<->Id

// ── F24 [GenerateMap<S,D>] (no partial method written) ──────────────────────
public class F24S
{
    public int Id { get; set; }
}

public class F24D
{
    public int Id { get; set; }
}

[DwarfMapper]
[GenerateMap<F24S, F24D>]
public partial class F24M
{
}

// ── F25 RequiredMapping=Both + [MapIgnoreSource] (source coverage) ───────────
public class F25S
{
    public int Id { get; set; }
    public string Internal { get; set; } = "";
}

public class F25D
{
    public int Id { get; set; }
}

[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
public partial class F25M
{
    [MapIgnoreSource(nameof(F25S.Internal))]
    public partial F25D Map(F25S s);
}

// ═══════════════════ BATCH 4: remaining features ════════════════════════════

// ── F26 [FlattenGraph] + polymorphism (tree graph -> flat list) ─────────────
public abstract class FsNode
{
    public string Name { get; set; } = "";
}

public class Folder : FsNode
{
    public List<FsNode> Children { get; set; } = new();
}

public class FileN : FsNode
{
    public long Size { get; set; }
}

public abstract class FsNodeDto
{
    public string Name { get; set; } = "";
}

public class FolderDto : FsNodeDto
{
    public List<FsNodeDto>? Children { get; set; }
}

public class FileNDto : FsNodeDto
{
    public long Size { get; set; }
}

public class Tree
{
    public FsNode? Root { get; set; }
    public string Label { get; set; } = "";
}

public class TreeDto
{
    public List<FsNodeDto> Nodes { get; set; } = new();
    public string Label { get; set; } = "";
}

[DwarfMapper]
public partial class F26M
{
    [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]
    [MapDerivedType<Folder, FolderDto>]
    [MapDerivedType<FileN, FileNDto>]
    public partial TreeDto Map(Tree t);
}

// ── F27 [Reinterpret] (struct blit across differently-named fields) ──────────
public struct Px
{
    public int A;
    public int B;
}

public struct Qx
{
    public int X;
    public int Y;
}

public class ReSrc
{
    public Px[] V { get; set; } = Array.Empty<Px>();
}

public class ReDst
{
    public Qx[] V { get; set; } = Array.Empty<Qx>();
}

[DwarfMapper]
public partial class F27M
{
    [Reinterpret(nameof(ReDst.V))] // blit Px[] -> Qx[] (identical layout, differently-named fields)
    public partial ReDst Map(ReSrc s);
}

// ── F28 OnCycle = SetNull (cycle broken by nulling the back-ref) ─────────────
public class CnA
{
    public int Id { get; set; }
    public CnA? Next { get; set; }
}

public class CnB
{
    public int Id { get; set; }
    public CnB? Next { get; set; }
}

[DwarfMapper(AutoNest = true, OnCycle = OnCycleStrategy.SetNull)]
public partial class F28M
{
    public partial CnB Map(CnA a);
}

// ── F29 ambient registry (public [GenerateMap] self-registers on load) ──────
public sealed class AmbSrc
{
    public int V { get; set; }
}

public sealed class AmbDst
{
    public int V { get; set; }
}

[DwarfMapper]
[GenerateMap<AmbSrc, AmbDst>]
public partial class F29M
{
}

// ── F30 [RoundTrip] (fuzz-verified backward(forward(x)) == x) ────────────────
public class RtA
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class RtB
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[DwarfMapper]
public partial class F30M
{
    [RoundTrip]
    public partial RtB ToB(RtA a);

    public partial RtA ToA(RtB b);
}

// ═══════════════════ BATCH 5: dirty / unexpected paths ═══════════════════════

// D2 narrowing overflow (long -> int, CreateChecked throws OverflowException)
public class OvS
{
    public long V { get; set; }
}

public class OvD
{
    public int V { get; set; }
}

[DwarfMapper]
public partial class DovM
{
    public partial OvD Map(OvS s);
}

// D3 unparseable string -> int (IParsable.Parse throws FormatException)
public class PsS
{
    public string V { get; set; } = "";
}

public class PsD
{
    public int V { get; set; }
}

[DwarfMapper]
public partial class DpsM
{
    public partial PsD Map(PsS s);
}

// D4 cycle with OnCycle=Throw (default) -> throws at runtime
public class CtA
{
    public int Id { get; set; }
    public CtA? Next { get; set; }
}

public class CtB
{
    public int Id { get; set; }
    public CtB? Next { get; set; }
}

[DwarfMapper(AutoNest = true, OnCycle = OnCycleStrategy.Throw)]
public partial class DctM
{
    public partial CtB Map(CtA a);
}

// D5 nested source null -> nested dest null (defensive)
public class NnInner
{
    public int V { get; set; }
}

public class NnInnerD
{
    public int V { get; set; }
}

public class NnS
{
    public NnInner? Inner { get; set; }
}

public class NnD
{
    public NnInnerD? Inner { get; set; }
}

[DwarfMapper]
public partial class DnnM
{
    public partial NnD Map(NnS s);
}

// D6 default null-collection strategy (AsEmpty): null source list -> empty list
public class EcS
{
    public List<int>? Xs { get; set; }
}

public class EcD
{
    public List<int> Xs { get; set; } = new();
}

[DwarfMapper]
public partial class DecM
{
    public partial EcD Map(EcS s);
}

// D7 enum by-value with an UNDEFINED numeric value (raw value passes through)
public enum RawE
{
    A = 1
}

public class ReS
{
    public int Code { get; set; }
}

public class ReD
{
    public RawE Code { get; set; }
}

[DwarfMapper]
public partial class DreM
{
    public partial ReD Map(ReS s);
}

// D8 unicode + empty-string passthrough
public class UniS
{
    public string Text { get; set; } = "";
}

public class UniD
{
    public string Text { get; set; } = "";
}

[DwarfMapper]
public partial class DuniM
{
    public partial UniD Map(UniS s);
}

// ═══════════════════ BATCH 5: options with no prior coverage ════════════════
//
// Six of fifteen policy options, plus the pair-scoped factory attribute, were exercised NOWHERE in this
// project or in the Gallery before Round 18 — and four of them are what a real migration reaches for first.
// Each case below asserts an OBSERVABLE RUNTIME DIFFERENCE, not merely that the option compiles.
//
// Note this closes the SURFACE gap only. The shape gap — multi-assembly, DI, ambient registry — is what
// actually let Round 18's defects through, and needs a consumer-shaped project instead. See Issues/Rount18/.

// ── F31 SkipNullSourceMembers (patch-merge) + [MapNullSkip] pair/method scope ─
public class F31S
{
    public string? Name { get; set; }
    public string? Note { get; set; }
}

public class F31D
{
    public string? Name { get; set; }
    public string? Note { get; set; }
}

[DwarfMapper]
public partial class F31M
{
    /// <summary>A null CLEARS the destination member.</summary>
    public partial void Replace(F31S s, F31D d);

    /// <summary>A null LEAVES the destination member alone.</summary>
    [MapNullSkip]
    public partial void Patch(F31S s, F31D d);
}

// ── F32 AllowNonPublic (internal constructor, deliberately granted) ──────────
public class F32S
{
    public int Id { get; set; }
}

public class F32D
{
    // internal, not private: a deliberate, compiler-checked grant. `private` is never usable, by design —
    // the generated code could not call it. This is the supported replacement for a reflective mapper's
    // "empty ctor for the mapper" trick.
    internal F32D()
    {
    }

    public int Id { get; set; }
}

[DwarfMapper(AllowNonPublic = true)]
public partial class F32M
{
    public partial F32D Map(F32S s);
}

// ── F33 AutoMatchMembers = false (explicit-only trust boundary) ──────────────
public class F33S
{
    public string Name { get; set; } = "";
    public bool IsAdmin { get; set; }   // the over-posting hazard: same name on both sides
}

public class F33D
{
    public string Name { get; set; } = "";
    public bool IsAdmin { get; set; }
}

[DwarfMapper(AutoMatchMembers = false)]
public partial class F33M
{
    // Nothing is wired by name. Name is mapped because it is NAMED; IsAdmin is refused because it is not —
    // which is the whole point at a trust boundary.
    [MapProperty(nameof(F33S.Name), nameof(F33D.Name))]
    [MapIgnore(nameof(F33D.IsAdmin))]
    public partial F33D Map(F33S s);
}

// ── F34 IgnoreObsoleteMembers ────────────────────────────────────────────────
public class F34S
{
    public int Id { get; set; }
}

public class F34D
{
    public int Id { get; set; }

    [Obsolete("replaced by Id")]
    public int LegacyId { get; set; }
}

[DwarfMapper(IgnoreObsoleteMembers = true)]
public partial class F34M
{
    // Without the option this is DWARF001: LegacyId has no source. With it, the obsolete member is neither
    // required nor populated — and needs no [MapIgnore] to say so.
    public partial F34D Map(F34S s);
}

// ── F35 [MapConstructor<S,T>] pair-scoped factory ────────────────────────────
public class F35S
{
    public string Text { get; set; } = "";
    public int Number { get; set; }
}

public class F35D
{
    public F35D(string text, int number)
    {
        Text = text;
        Number = number;
        Origin = "factory";
    }

    public string Text { get; }
    public int Number { get; }
    public string Origin { get; }
}

[DwarfMapper]
[GenerateMap<F35S, F35D>]
[MapConstructor<F35S, F35D>(nameof(Create))]
public partial class F35M
{
    private static F35D Create(F35S s) => new(s.Text, s.Number);
}

// ── F36 [assembly: DwarfMapperDefaults] — the assembly policy layer ──────────
//
// The assembly attribute lives in Program.cs and sets CaseInsensitive = true. This mapper sets NO options of
// its own, so the ONLY thing that can make a case-mismatched member map is the assembly default being read —
// which is what makes the case non-vacuous.
//
// CaseInsensitive is chosen deliberately: every other feature in this file matches names exactly, so an
// assembly-wide relaxation changes nothing for them. An option like EnumStrategy would have silently
// rewritten F03/F04's expectations instead of testing this one.
public class F36S
{
    public int itemcount { get; set; }
}

public class F36D
{
    public int ItemCount { get; set; }
}

[DwarfMapper]
public partial class F36M
{
    public partial F36D Map(F36S s);
}

// ── F37 RegisterCollectionShapes (ambient collection maps) ───────────────────
// The Round-18 blocking defect, demonstrated: an element map declared once also answers a facade call for a
// COLLECTION of it. Before this, Map<ICollection<D>>(listOfS) threw at first use, invisibly to every
// compile-time check.
public class F37S
{
    public int Id { get; set; }
}

public class F37D
{
    public int Id { get; set; }
}

[DwarfMapper]
[GenerateMap<F37S, F37D>]
public partial class F37M
{
}

// ── F38 [MapCollectionKey] (update-into merge by key) ────────────────────────
// Update-into normally REPLACES a list member. Keyed merge updates matched elements in place, adds new keys,
// and keeps existing elements the source did not mention.
public class F38Line
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
}

public class F38Order
{
    public List<F38Line> Lines { get; set; } = [];
}

[DwarfMapper]
public partial class F38M
{
    [MapCollectionKey(nameof(F38Order.Lines), nameof(F38Line.Id))]
    public partial void Merge(F38Order src, F38Order dest);
}

// ── F39 [AutoNest(false)] (explicit-nesting mode for one method) ─────────────
// With auto-nesting off, the nested pair must be declared by hand — which is the point: no mapper appears
// that the author did not write.
public class F39Inner
{
    public int V { get; set; }
}

public class F39InnerD
{
    public int V { get; set; }
}

public class F39S
{
    public F39Inner Inner { get; set; } = new();
}

public class F39D
{
    public F39InnerD Inner { get; set; } = new();
}

[DwarfMapper]
public partial class F39M
{
    [AutoNest(false)]
    public partial F39D Map(F39S s);

    // Declared explicitly, because [AutoNest(false)] refuses to invent it.
    public partial F39InnerD MapInner(F39Inner s);
}

// ── F40 [GenerateWrapperMap] (single-payload generic wrapper) ────────────────
public class F40Payload
{
    public int Id { get; set; }
}

public class F40PayloadD
{
    public int Id { get; set; }
}

public class F40Envelope<T>
{
    public T Value { get; set; } = default!;
    public string Trace { get; set; } = "";
}

[DwarfMapper]
[GenerateMap<F40Payload, F40PayloadD>]
[GenerateWrapperMap(typeof(F40Envelope<>))]
public partial class F40M
{
}

// ── F41 [ProvidesMap] (hand-written method, ambient-registered) ──────────────
// Some conversions are not mapping SHAPES. An object that HOLDS a collection, mapped to the collection
// itself, is the common one: one side is a container, the other an element sequence. It must be written by
// hand — and once written it is an ordinary method that nothing registers, so every facade call site for it
// throws and a parity harness reports the pair as "not registered" while the code is perfectly correct.
public class F41Item
{
    public int V { get; set; }
}

public class F41ItemDto
{
    public int V { get; set; }
}

public class F41Doc
{
    public List<F41Item> Items { get; set; } = [];
}

[DwarfMapper]
[GenerateMap<F41Item, F41ItemDto>]
public partial class F41M
{
    /// <summary>Hand-written, and registered by declaration rather than by reflection.</summary>
    [ProvidesMap]
    public ICollection<F41ItemDto> ToItems(F41Doc doc)
    {
        // The registry calls this method; it does not wrap it. A hand-written map carries its own guards.
        ArgumentNullException.ThrowIfNull(doc);

        return doc.Items.Select(Map).ToList();
    }
}

// ── F42 A declared pair is THE mapping for its types ─────────────────────────
// A [GenerateMap<S,T>] pair configured with a [MapConstructor] factory, reached three ways: directly, as a
// collection element, and as a nested member. All three must produce the same object, because they are the
// same pair. They did not: element and member resolution searched declared partial METHODS only, so a class
// -level pair was invisible to them and a fresh element mapper got synthesized past it — one that constructs
// the target directly and never calls the factory. Green build, and `Map(item)` and `Map(list)[0]` disagreed.
public class F42Item
{
    public int V { get; set; }
}

public class F42ItemDto
{
    public F42ItemDto(int v) => V = v;

    public int V { get; }
}

public class F42Box
{
    public F42Item Only { get; set; } = new();

    public List<F42Item> Many { get; set; } = [];
}

public class F42BoxDto
{
    public F42ItemDto Only { get; set; } = new(0);

    public List<F42ItemDto> Many { get; set; } = [];
}

[DwarfMapper]
[GenerateMap<F42Item, F42ItemDto>]
[MapConstructor<F42Item, F42ItemDto>(nameof(Create))]
[GenerateMap<F42Box, F42BoxDto>]
[GenerateMap<List<F42Item>, List<F42ItemDto>>]
public partial class F42M
{
    /// <summary>The +100 is the tell: any route that skips the factory returns the raw value.</summary>
    private static F42ItemDto Create(F42Item i) => new(i.V + 100);
}

// ── F43 [MapDerivedType] through a collection ────────────────────────────────
// The symptom that motivated this: a derived element inside a base-typed collection silently lost the member
// only the derived type has. A collection loop binds at COMPILE time, so `List<F43Command>` mapped every item
// as a plain F43Command; the mapper it replaced dispatched on the RUNTIME type. [MapDerivedType] restores
// that — and the arm must not resolve back to the method it dispatches from, which is a switch arm calling
// its own switch: it compiles, reports nothing, and overflows the stack on the first derived element.
public class F43Command
{
    public string Name { get; set; } = "";
}

public class F43AliasCommand : F43Command
{
    public string Alias { get; set; } = "";
}

public class F43CommandDto
{
    public string Name { get; set; } = "";
}

public class F43AliasCommandDto : F43CommandDto
{
    public string Alias { get; set; } = "";
}

public class F43Page
{
    public List<F43Command> Items { get; set; } = [];
}

public class F43PageDto
{
    public List<F43CommandDto> Items { get; set; } = [];
}

[DwarfMapper]
[GenerateMap<F43Page, F43PageDto>]
public partial class F43M
{
    // A dispatching method maps NOTHING itself — every runtime type either matches an arm or throws — so the
    // base type needs an arm of its own. Ordering is by specificity, not by declaration, so the derived arm
    // still wins for an F43AliasCommand.
    [MapDerivedType<F43AliasCommand, F43AliasCommandDto>]
    [MapDerivedType<F43Command, F43CommandDto>]
    public partial F43CommandDto ToDto(F43Command c);

    /// <summary>Deliberately NOT an overload of ToDto — an arm may resolve to any declared method.</summary>
    public partial F43AliasCommandDto ToAliasDto(F43AliasCommand c);
}

// ── F44 [MapDerivedType] where two sources share one target ──────────────────
// The arm's target IS the dispatching method's return type. This is the shape that recursed, and it is a
// perfectly reasonable thing to write: one flat DTO, filled differently depending on what arrived.
public class F44Command
{
    public string Name { get; set; } = "";
}

public class F44AliasCommand : F44Command
{
    public string Alias { get; set; } = "";
}

public class F44OverviewDto
{
    public string Name { get; set; } = "";

    public string Alias { get; set; } = "";
}

[DwarfMapper]
public partial class F44M
{
    [MapDerivedType<F44AliasCommand, F44OverviewDto>]
    public partial F44OverviewDto ToDto(F44Command c);
}

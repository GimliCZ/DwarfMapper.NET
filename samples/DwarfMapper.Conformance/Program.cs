// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Runtime.CompilerServices;
using DwarfMapper;
using DwarfMapper.Conformance;

// F36: the assembly policy layer. Set here rather than on a mapper so F36M can prove the value is READ —
// it declares no options of its own, and a case-mismatched member maps only because of this line.
// CaseInsensitive is deliberate: every other feature matches names exactly, so relaxing it assembly-wide
// changes nothing for them.
[assembly: DwarfMapperDefaults(CaseInsensitive = true)]

Console.WriteLine("DwarfMapper — god project (every feature, one run)\n");

// F01 flat
R.Check("F01 flat by-name", new F01M().Map(new F01S { Id = 1, Name = "a" }) is { Id: 1, Name: "a" });

// F02 rename
R.Check("F02 rename", new F02M().Map(new F02S { FullName = "Ada" }).Name == "Ada");

// F03 built-in conversions
var when = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
var d3 = new F03M().Map(new F03S { Small = 7, Colour = Col.Green, Num = "42", When = when, Lift = 5 });
R.Check("F03 int->long", d3.Small == 7L);
R.Check("F03 enum->string", d3.Colour == "Green");
R.Check("F03 string->int", d3.Num == 42);
R.Check("F03 DateTime->string", d3.When == when.ToString("o", CultureInfo.InvariantCulture));
R.Check("F03 lift T->T?", d3.Lift == 5);

// F04 enum by value
R.Check("F04 enum by-value", new F04M().Map(new F04S { E = Src4.B }).E == Dst4.Y);

// F05 nested
R.Check("F05 nested object", new F05M().Map(new F05S { Inner = new Inner5 { V = 8 } }).Inner.V == 8);

// F06 collections
var d6 = new F06M().Map(new F06S { Nums = { 1, 2 }, Tags = new[] { "x", "y" } });
R.Check("F06 collections", d6.Nums.Count == 2 && d6.Tags.Length == 2 && d6.Tags[1] == "y");

// F07 null-collection AsNull
R.Check("F07 null-collection AsNull", new F07M().Map(new F07S { Nums = null }).Nums is null);

// F08 dotted path
R.Check("F08 dotted source path",
    new F08M().Map(new F08S { Address = new Addr8 { City = "Prague" } }).City == "Prague");

// F09 flatten
var d9 = new F09M().Map(new F09S { Address = new Addr9 { City = "Brno", Zip = "60200" } });
R.Check("F09 flatten", d9.City == "Brno" && d9.Zip == "60200");

// F10 custom converter
R.Check("F10 Use= converter",
    new F10M().Map(new F10S { Total = 9.5m }).Total == 9.5m.ToString("C", CultureInfo.GetCultureInfo("en-US")));

// F11 null substitute
R.Check("F11 NullSubstitute", new F11M().Map(new F11S { Nickname = null }).Nickname == "(none)");

// F12 conditional (When=false -> keeps default)
R.Check("F12 When= (false)", new F12M().Map(new F12S { Score = 50, Active = false }).Score == 0);
R.Check("F12 When= (true)", new F12M().Map(new F12S { Score = 50, Active = true }).Score == 50);

// F13 MapValue const + computed
var d13 = new F13M().Map(new F13S { Id = 1 });
R.Check("F13 MapValue const", d13.Tier == "guild");
R.Check("F13 MapValue computed", d13.Stamp == "v1");

// F14 record ctor
R.Check("F14 record target", new F14M().Map(new F14S { Id = 3, Name = "r" }) == new F14D(3, "r"));

// F15 before/after hooks
R.Check("F15 AfterMap hook", new F15M().Map(new F15S { Id = 1 }).Touched == 99);

// F16 reverse map
R.Check("F16 ReverseMap fwd", new F16M().Forward(new F16A { FullName = "Ada" }).Name == "Ada");
R.Check("F16 ReverseMap back", new F16M().Backward(new F16B { Name = "Bob" }).FullName == "Bob");

// F17 ignore
R.Check("F17 MapIgnore", new F17M().Map(new F17S { Id = 1 }).Secret is null);

// F18 [MapTo] registry extension
R.Check("F18 [MapTo] extension", new F18S { Id = 9, Name = "z" }.MapTo<F18D>() is { Id: 9, Name: "z" });

// F19 polymorphism ([MapDerivedType])
Animal19 a19 = new Dog19 { Id = 1, Breed = "Lab" };
R.Check("F19 polymorphism", new F19M().Map(a19) is Dog19D { Breed: "Lab" });

// F20 IQueryable projection
var d20 = new F20M().Project(new[] { new F20S { Id = 7, Name = "p" } }.AsQueryable()).Single();
R.Check("F20 IQueryable projection", d20 is { Id: 7, Name: "p" });

// F21 reference handling / cycles (Preserve)
var n1 = new Node21 { Id = 1 };
var n2 = new Node21 { Id = 2 };
n1.Next = n2;
n2.Next = n1; // 2-node cycle
var dtoN = new F21M().Map(n1);
R.Check("F21 cycle identity (Preserve)", ReferenceEquals(dtoN.Next!.Next, dtoN));

// F22 NameConvention.Flexible
R.Check("F22 NameConvention flexible", new F22M().Map(new F22S { user_name = "ada" }).UserName == "ada");

// F23 explicit ctor selection
R.Check("F23 [DwarfMapperConstructor]", new F23M().Map(new F23S { Id = 5, Name = "x" }) is { Id: 5, Name: "x" });

// F24 GenerateMap (no partial method)
R.Check("F24 [GenerateMap<S,D>]", new F24M().Map(new F24S { Id = 7 }).Id == 7);

// F25 RequiredMapping=Both + MapIgnoreSource
R.Check("F25 source-required+ignore", new F25M().Map(new F25S { Id = 3, Internal = "x" }).Id == 3);

// F26 FlattenGraph + polymorphism (tree -> flat list of polymorphic dtos)
var tree = new Tree
{
    Label = "T",
    Root = new Folder
    {
        Name = "root",
        Children =
        {
            new FileN { Name = "a", Size = 1 },
            new Folder { Name = "sub", Children = { new FileN { Name = "b", Size = 2 } } }
        }
    }
};
var td = new F26M().Map(tree);
R.Check("F26 FlattenGraph", td.Label == "T" && td.Nodes.Count >= 3 && td.Nodes.OfType<FileNDto>().Any(n => n.Size == 2),
    $"nodes={td.Nodes.Count}");

// F27 Reinterpret array blit (Px[]->Qx[] by layout: A->X, B->Y)
var rd = new F27M().Map(new ReSrc { V = new[] { new Px { A = 1, B = 2 }, new Px { A = 3, B = 4 } } });
R.Check("F27 Reinterpret blit", rd.V.Length == 2 && rd.V[0].X == 1 && rd.V[0].Y == 2 && rd.V[1].X == 3);

// F28 OnCycle=SetNull (self-loop -> back-ref nulled)
var cn = new CnA { Id = 1 };
cn.Next = cn;
var cnb = new F28M().Map(cn);
R.Check("F28 OnCycle=SetNull", cnb.Id == 1 && (cnb.Next is null || cnb.Next.Next is null),
    cnb.Next is null ? "next=null" : "next.next=null");

// F29 ambient registry (public [GenerateMap] self-registered via ModuleInitializer)
R.Check("F29 ambient IsProvided", DwarfMapperRegistry.IsProvided(typeof(AmbSrc), typeof(AmbDst)));
R.Check("F29 ambient resolve+map", ((AmbDst)DwarfMapperRegistry.Map(new AmbSrc { V = 41 }, typeof(AmbDst))).V == 41);

// F30 RoundTrip fuzz (backward(forward(x)) == x over 50 random seeds)
R.Check("F30 RoundTrip verify", R.NoThrow(() => new F30M().VerifyRoundTrip_ToB(7, 50)));

Console.WriteLine("\n-- dirty / unexpected paths --");

// D1 null source -> ArgumentNullException (generated guard)
R.Check("D1 null source throws", R.Throws<ArgumentNullException>(() => new F01M().Map(null!)));

// D2 long->int narrowing overflow -> OverflowException (CreateChecked)
R.Check("D2 narrowing overflow", R.Throws<OverflowException>(() => new DovM().Map(new OvS { V = long.MaxValue })));

// D3 unparseable string->int -> FormatException
R.Check("D3 bad parse throws", R.Throws<FormatException>(() => new DpsM().Map(new PsS { V = "abc" })));

// D4 cycle with OnCycle=Throw -> throws
var ct = new CtA { Id = 1 };
ct.Next = ct;
R.Check("D4 cycle -> throw", R.Throws<Exception>(() => new DctM().Map(ct)));

// D5 nested source null -> nested dest null
R.Check("D5 nested null -> null", new DnnM().Map(new NnS { Inner = null }).Inner is null);

// D6 default null-collection -> empty (AsEmpty)
R.Check("D6 null coll -> empty", new DecM().Map(new EcS { Xs = null }).Xs is { Count: 0 });

// D7 enum by-value undefined numeric -> raw value passes through
R.Check("D7 enum raw value", (int)new DreM().Map(new ReS { Code = 999 }).Code == 999);

// D8 unicode + empty-string passthrough
R.Check("D8 unicode passthrough", new DuniM().Map(new UniS { Text = "héllo 🐉 世界" }).Text == "héllo 🐉 世界");
R.Check("D8 empty string", new DuniM().Map(new UniS { Text = "" }).Text.Length == 0);

Console.WriteLine("\n-- options with no prior coverage (Round 18) --");

// F31 SkipNullSourceMembers scoped per method with [MapNullSkip]
var f31Patched = new F31D { Name = "kept", Note = "kept" };
var f31Replaced = new F31D { Name = "kept", Note = "kept" };
new F31M().Patch(new F31S { Name = "new" }, f31Patched);
new F31M().Replace(new F31S { Name = "new" }, f31Replaced);

// CA1508 fires because the analyzer can see INTO the generated method bodies and prove these hold — which
// is a compliment to the generator, not dead code. The assertion is the point of the conformance run.
#pragma warning disable CA1508
R.Check("F31 [MapNullSkip] patch keeps", f31Patched is { Name: "new", Note: "kept" });
R.Check("F31 replace clears", f31Replaced is { Name: "new", Note: null });
#pragma warning restore CA1508

// F32 AllowNonPublic reaches an internal constructor
R.Check("F32 AllowNonPublic ctor", new F32M().Map(new F32S { Id = 11 }).Id == 11);

// F33 AutoMatchMembers = false — nothing is wired by name
var f33 = new F33M().Map(new F33S { Name = "n", IsAdmin = true });
R.Check("F33 explicit-only maps named", f33.Name == "n");
R.Check("F33 explicit-only drops unnamed", !f33.IsAdmin);

// F34 IgnoreObsoleteMembers — the obsolete destination needs no [MapIgnore]
// Reading the obsolete member is exactly what the assertion is for: proving it was LEFT at its default.
#pragma warning disable CS0618, CA1508
R.Check("F34 obsolete member skipped", new F34M().Map(new F34S { Id = 4 }) is { Id: 4, LegacyId: 0 });
#pragma warning restore CS0618, CA1508

// F35 [MapConstructor<S,D>] pair-scoped factory owns construction
var f35 = new F35M().Map(new F35S { Text = "t", Number = 9 });
R.Check("F35 [MapConstructor] factory", f35 is { Text: "t", Number: 9, Origin: "factory" });

// F36 [assembly: DwarfMapperDefaults] — F36M declares no options, so a case-mismatched member maps only
// because the assembly default was read.
R.Check("F36 assembly defaults read", new F36M().Map(new F36S { itemcount = 6 }).ItemCount == 6);

// F37 RegisterCollectionShapes — one declared element map answers a COLLECTION request through the facade.
// This is the Round-18 blocking defect, closed: before it, this call threw at first use.
RuntimeHelpers.RunModuleConstructor(typeof(F37M).Module.ModuleHandle);
var f37 = (ICollection<F37D>)DwarfMapperRegistry.Map(
    new List<F37S> { new() { Id = 1 }, new() { Id = 2 } }, typeof(ICollection<F37D>));
R.Check("F37 ambient collection shape", f37.Count == 2 && f37.First().Id == 1);

// F38 [MapCollectionKey] — matched keys update in place, new keys are added, unmentioned ones survive
var f38Dest = new F38Order
{
    Lines = [new() { Id = 1, Text = "old" }, new() { Id = 2, Text = "keep" }]
};
new F38M().Merge(
    new F38Order { Lines = [new() { Id = 1, Text = "new" }, new() { Id = 3, Text = "added" }] },
    f38Dest);
R.Check("F38 [MapCollectionKey] merge",
    f38Dest.Lines.Count == 3
    && f38Dest.Lines.Single(l => l.Id == 1).Text == "new"
    && f38Dest.Lines.Single(l => l.Id == 2).Text == "keep"
    && f38Dest.Lines.Single(l => l.Id == 3).Text == "added");

// F39 [AutoNest(false)] — the nested pair is declared by hand, and nothing is invented
R.Check("F39 [AutoNest(false)]", new F39M().Map(new F39S { Inner = new F39Inner { V = 8 } }).Inner.V == 8);

// F40 [GenerateWrapperMap] — the envelope's payload is mapped, the rest carried across
var f40 = new F40M().Map(new F40Envelope<F40Payload> { Value = new F40Payload { Id = 2 }, Trace = "t" });
R.Check("F40 [GenerateWrapperMap]", f40 is { Trace: "t", Value.Id: 2 });

// F41 [ProvidesMap] — a hand-written object-to-collection map, reachable through the ambient registry
RuntimeHelpers.RunModuleConstructor(typeof(F41M).Module.ModuleHandle);
var f41 = (ICollection<F41ItemDto>)DwarfMapperRegistry.Map(
    new F41Doc { Items = [new() { V = 3 }, new() { V = 4 }] }, typeof(ICollection<F41ItemDto>));
R.Check("F41 [ProvidesMap] hand-written", f41.Count == 2 && f41.Last().V == 4);

// F42 A declared pair is THE mapping for its types — the same [MapConstructor] factory has to run whichever
// route reaches the pair. The +100 is the tell: a route that constructs the target itself returns the raw V.
var f42M = new F42M();
var f42Direct = f42M.Map(new F42Item { V = 1 });
var f42Element = f42M.Map([new F42Item { V = 2 }]);
var f42Nested = f42M.Map(new F42Box { Only = new F42Item { V = 3 }, Many = [new F42Item { V = 4 }] });
R.Check("F42 declared pair — direct", f42Direct.V == 101);
R.Check("F42 declared pair — collection element", f42Element[0].V == 102);
R.Check("F42 declared pair — nested member", f42Nested.Only.V == 103);
R.Check("F42 declared pair — nested collection element", f42Nested.Many[0].V == 104);

// F43 [MapDerivedType] through a collection — the element must dispatch on its RUNTIME type, or the member
// only the derived type has is silently dropped. That was a real API regression, not a hypothetical.
var f43 = new F43M().Map(new F43Page
{
    Items = [new F43Command { Name = "plain" }, new F43AliasCommand { Name = "aka", Alias = "a" }]
});
R.Check("F43 [MapDerivedType] base element", f43.Items[0] is { Name: "plain" } and not F43AliasCommandDto);
R.Check("F43 [MapDerivedType] derived element keeps Alias",
    f43.Items[1] is F43AliasCommandDto { Name: "aka", Alias: "a" });

// F44 Two sources, one target — the arm's target is the dispatching method's own return type. The arm must
// not resolve back to the dispatching method; that emission compiled and overflowed the stack.
var f44 = new F44M().ToDto(new F44AliasCommand { Name = "aka", Alias = "a" });
R.Check("F44 [MapDerivedType] shared target fills the derived member", f44 is { Name: "aka", Alias: "a" });

Console.WriteLine($"\n{R.Pass} passed, {R.Fail} failed  (of {R.Pass + R.Fail})");
return R.Fail == 0 ? 0 : 1;

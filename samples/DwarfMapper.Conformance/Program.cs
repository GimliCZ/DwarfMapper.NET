// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper;
using DwarfMapper.Conformance;

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

Console.WriteLine($"\n{R.Pass} passed, {R.Fail} failed  (of {R.Pass + R.Fail})");
return R.Fail == 0 ? 0 : 1;

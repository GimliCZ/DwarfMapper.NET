// SPDX-License-Identifier: GPL-2.0-only
//
// Round 35 probe, not part of any build: a console program referencing DwarfMapper and its generator.
// Usage: cveprobe <none|preserve|setnull> <ctor|rec|poly|polymember|iface|dict|conv|tree>, one process per case.
// Results: ROUND35-TASKS.md, "The 2026-11-13 AutoMapper advisories".

using System;
using System.Collections.Generic;
using DwarfMapper;

// Round 35 probe: the shapes of AutoMapper advisories GHSA-6vhm-j9h7-f9gh and GHSA-gpwp-5797-mjff (2026-11-13)
// against DwarfMapper, 30,000 levels deep. A stack overflow kills the process: one case per run.
namespace P
{
    // a. recursion through a constructor parameter (the PoC class of the advisory)
    public class Ctor { public Ctor(int v, Ctor? next) { V = v; Next = next; } public int V { get; } public Ctor? Next { get; set; } }
    public class CtorDto { public CtorDto(int v, CtorDto? next) { V = v; Next = next; } public int V { get; } public CtorDto? Next { get; } }
    // b. positional record
    public record Rec(int V, Rec? Next);
    public record RecDto(int V, RecDto? Next);
    // c. polymorphic chain: every level a derived object held as the base
    public abstract class Animal { public string Name { get; set; } = ""; }
    public class Dog : Animal { public Animal? Next { get; set; } }
    public abstract class AnimalDto { public string Name { get; set; } = ""; }
    public class DogDto : AnimalDto { public AnimalDto? Next { get; set; } }
    public class Kennel { public Animal? First { get; set; } }
    public class KennelDto { public AnimalDto? First { get; set; } }
    // d. interface-typed member
    public interface IShape { string Name { get; } }
    public class Box : IShape { public string Name { get; set; } = ""; public IShape? Inner { get; set; } }
    public interface IShapeDto { string Name { get; } }
    public class BoxDto : IShapeDto { public string Name { get; set; } = ""; public IShapeDto? Inner { get; set; } }
    // e. recursion through a dictionary value
    public class Dir { public Dictionary<string, Dir> Kids { get; set; } = new(); }
    public class DirDto { public Dictionary<string, DirDto> Kids { get; set; } = new(); }
    // f. a hand-written converter that calls the mapper again
    public class N { public int V { get; set; } public N? Next { get; set; } }
    public class NDto { public int V { get; set; } public NDto? Next { get; set; } }
    // g. recursion through a list element of a constructor-only type
    public class Tree { public Tree(List<Tree> kids) { Kids = kids; } public List<Tree> Kids { get; } }
    public class TreeDto { public TreeDto(List<TreeDto> kids) { Kids = kids; } public List<TreeDto> Kids { get; } }

    [DwarfMapper]
    public partial class MNone
    {
        public partial CtorDto MapCtor(Ctor c);
        public partial RecDto MapRec(Rec r);
        [MapDerivedType<Dog, DogDto>] public partial AnimalDto MapAnimal(Animal a);
        public partial KennelDto MapKennel(Kennel k);
        [MapDerivedType<Box, BoxDto>] public partial IShapeDto MapShape(IShape s);
        public partial DirDto MapDir(Dir d);
        [MapProperty(nameof(N.Next), nameof(NDto.Next), Use = nameof(Conv))] public partial NDto MapN(N n);
        private NDto? Conv(N? n) => n is null ? null : MapN(n);
        public partial TreeDto MapTree(Tree t);
    }

    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class MPreserve
    {
        [MapDerivedType<Dog, DogDto>] public partial AnimalDto MapAnimal(Animal a);
        public partial KennelDto MapKennel(Kennel k);
        [MapDerivedType<Box, BoxDto>] public partial IShapeDto MapShape(IShape s);
        public partial DirDto MapDir(Dir d);
    }

    [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
    public partial class MSetNull
    {
        public partial CtorDto MapCtor(Ctor c);
        public partial RecDto MapRec(Rec r);
        [MapDerivedType<Dog, DogDto>] public partial AnimalDto MapAnimal(Animal a);
        public partial KennelDto MapKennel(Kennel k);
        [MapDerivedType<Box, BoxDto>] public partial IShapeDto MapShape(IShape s);
        public partial DirDto MapDir(Dir d);
        [MapProperty(nameof(N.Next), nameof(NDto.Next), Use = nameof(Conv))] public partial NDto MapN(N n);
        private NDto? Conv(N? n) => n is null ? null : MapN(n);
        public partial TreeDto MapTree(Tree t);
    }

    internal static class Program
    {
        private const int Depth = 30_000;

        private static int Main(string[] args)
        {
            var mode = args[0];
            var shape = args[1];
            Func<string> run = (mode, shape) switch
            {
                ("none", "ctor") => () => Go(new MNone().MapCtor, Ctor()),
                ("setnull", "ctor") => () => Go(new MSetNull().MapCtor, Ctor()),
                ("none", "rec") => () => Go(new MNone().MapRec, Rec()),
                ("setnull", "rec") => () => Go(new MSetNull().MapRec, Rec()),
                ("none", "poly") => () => Go(new MNone().MapAnimal, Poly()),
                ("preserve", "poly") => () => Go(new MPreserve().MapAnimal, Poly()),
                ("setnull", "poly") => () => Go(new MSetNull().MapAnimal, Poly()),
                ("none", "polymember") => () => Go(new MNone().MapKennel, new Kennel { First = Poly() }),
                ("preserve", "polymember") => () => Go(new MPreserve().MapKennel, new Kennel { First = Poly() }),
                ("setnull", "polymember") => () => Go(new MSetNull().MapKennel, new Kennel { First = Poly() }),
                ("none", "iface") => () => Go(new MNone().MapShape, Shape()),
                ("preserve", "iface") => () => Go(new MPreserve().MapShape, Shape()),
                ("setnull", "iface") => () => Go(new MSetNull().MapShape, Shape()),
                ("none", "dict") => () => Go(new MNone().MapDir, Dirs()),
                ("preserve", "dict") => () => Go(new MPreserve().MapDir, Dirs()),
                ("setnull", "dict") => () => Go(new MSetNull().MapDir, Dirs()),
                ("none", "conv") => () => Go(new MNone().MapN, Ns()),
                ("setnull", "conv") => () => Go(new MSetNull().MapN, Ns()),
                ("none", "tree") => () => Go(new MNone().MapTree, Trees()),
                ("setnull", "tree") => () => Go(new MSetNull().MapTree, Trees()),
                _ => () => "unknown case",
            };
            Console.WriteLine($"{mode} {shape}: {run()}");
            return 0;
        }

        private static string Go<S, D>(Func<S, D> map, S source)
        {
            try
            {
                map(source);
                return "mapped all " + Depth + " levels";
            }
            catch (Exception e)
            {
                return "THROWS " + e.GetType().Name;
            }
        }

        private static Ctor Ctor() { Ctor? c = null; for (var i = 0; i < Depth; i++) c = new Ctor(i, c); return c!; }

        private static Rec Rec() { Rec? r = null; for (var i = 0; i < Depth; i++) r = new Rec(i, r); return r!; }

        private static Animal Poly() { Animal? a = null; for (var i = 0; i < Depth; i++) a = new Dog { Next = a }; return a!; }

        private static IShape Shape() { IShape? s = null; for (var i = 0; i < Depth; i++) s = new Box { Inner = s }; return s!; }

        private static Dir Dirs() { var d = new Dir(); for (var i = 0; i < Depth; i++) { var p = new Dir(); p.Kids["k"] = d; d = p; } return d; }

        private static N Ns() { N? n = null; for (var i = 0; i < Depth; i++) n = new N { V = i, Next = n }; return n!; }

        private static Tree Trees() { var t = new Tree(new List<Tree>()); for (var i = 0; i < Depth; i++) t = new Tree(new List<Tree> { t }); return t; }
    }
}

// SPDX-License-Identifier: GPL-2.0-only
//
// Round 35 probe, not part of any build: a console program referencing DwarfMapper and its generator, with
// <InterceptorsNamespaces>$(InterceptorsNamespaces);DwarfMapper.Generated</InterceptorsNamespaces> for the ambient case.
// Results: ROUND35-TASKS.md, "T20 done".

using System;
using System.Collections.Generic;
using DwarfMapper;

// Round 35 T20 probe: every way user-written code bound to a mapper can call the mapper again, on a chain 200,000
// levels deep. A stack overflow kills the process: one case per run. Usage: reentryprobe <case>
namespace P
{
    public class N { public int V { get; set; } public N? Next { get; set; } public List<N> Kids { get; set; } = new(); }
    public class NDto { public int V { get; set; } public NDto? Next { get; set; } public List<NDto> Kids { get; set; } = new(); }

    // A two-type loop for the adopted-by-signature route: N2 holds an M2, whose hand-written converter maps the N2 inside.
    public class N2 { public int V { get; set; } public M2? Child { get; set; } }
    public class M2 { public N2? Back { get; set; } }
    public class N2Dto { public int V { get; set; } public M2Dto? Child { get; set; } }
    public class M2Dto { public N2Dto? Back { get; set; } }

    // 1. [MapProperty(Use = ...)] on a member
    [DwarfMapper]
    public partial class UseMember
    {
        [MapProperty(nameof(N.Next), nameof(NDto.Next), Use = nameof(Conv))]
        [MapIgnore(nameof(NDto.Kids))]
        public partial NDto Map(N n);
        private NDto? Conv(N? n) => n is null ? null : Map(n);
    }

    // 2. a hand-written method adopted by signature, no attribute
    [DwarfMapper]
    public partial class Adopted
    {
        public partial N2Dto Map(N2 n);
        private M2Dto Convert(M2 m) => new M2Dto { Back = m.Back is null ? null : Map(m.Back) };
    }

    // 3. [AfterMap] hook that maps the rest
    [DwarfMapper]
    public partial class AfterHook
    {
        [MapIgnore(nameof(NDto.Next))]
        [MapIgnore(nameof(NDto.Kids))]
        public partial NDto Map(N n);
        [AfterMap] private void Finish(N s, NDto d) { if (s.Next is not null) d.Next = Map(s.Next); }
    }

    // 4. the same hook under Preserve and under SetNull
    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class AfterHookPreserve
    {
        [MapIgnore(nameof(NDto.Next))]
        [MapIgnore(nameof(NDto.Kids))]
        public partial NDto Map(N n);
        [AfterMap] private void Finish(N s, NDto d) { if (s.Next is not null) d.Next = Map(s.Next); }
    }

    [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
    public partial class UseMemberSetNull
    {
        [MapProperty(nameof(N.Next), nameof(NDto.Next), Use = nameof(Conv))]
        [MapIgnore(nameof(NDto.Kids))]
        public partial NDto Map(N n);
        private NDto? Conv(N? n) => n is null ? null : Map(n);
    }

    // 5. update-into, re-entered from its converter
    [DwarfMapper]
    public partial class UpdateInto
    {
        [MapProperty(nameof(N.Next), nameof(NDto.Next), Use = nameof(Conv))]
        [MapIgnore(nameof(NDto.Kids))]
        public partial void Update(N n, NDto d);
        private NDto? Conv(N? n) { if (n is null) return null; var d = new NDto(); Update(n, d); return d; }
    }

    // 6. the converter calls the mapper through the ambient facade instead of directly
    [DwarfMapper]
    public partial class Ambient
    {
        [MapProperty(nameof(N.Next), nameof(NDto.Next), Use = nameof(Conv))]
        [MapIgnore(nameof(NDto.Kids))]
        public partial NDto Map(N n);
        private static NDto? Conv(N? n) => n is null ? null : Dwarf.Map<N, NDto>(n);
    }

    // 7. a span map whose element converter is hand-written and maps a span of the children
    [DwarfMapper]
    public partial class SpanElement
    {
        public partial void MapSpan(ReadOnlySpan<N> s, Span<NDto> d);
        private NDto Convert(N n)
        {
            var dto = new NDto { V = n.V };
            if (n.Next is not null)
            {
                var one = new NDto[1];
                MapSpan(new[] { n.Next }, one);
                dto.Next = one[0];
            }

            return dto;
        }
    }

    internal static class Program
    {
        private const int Depth = 200_000;

        private static N Chain() { N? n = null; for (var i = 0; i < Depth; i++) n = new N { V = i, Next = n }; return n!; }

        private static N2 Chain2() { N2? n = null; for (var i = 0; i < Depth; i++) n = new N2 { V = i, Child = new M2 { Back = n } }; return n!; }

        private static int Main(string[] args)
        {
            var which = args[0];
            string result;
            try
            {
                switch (which)
                {
                    case "use": new UseMember().Map(Chain()); break;
                    case "adopted": new Adopted().Map(Chain2()); break;
                    case "after": new AfterHook().Map(Chain()); break;
                    case "afterpreserve": new AfterHookPreserve().Map(Chain()); break;
                    case "usesetnull": new UseMemberSetNull().Map(Chain()); break;
                    case "update": new UpdateInto().Update(Chain(), new NDto()); break;
                    case "ambient": new Ambient().Map(Chain()); break;
                    case "span": { var one = new NDto[1]; new SpanElement().MapSpan(new[] { Chain() }, one); break; }
                    default: Console.WriteLine("unknown case"); return 1;
                }

                result = "mapped all " + Depth + " levels";
            }
            catch (Exception e)
            {
                result = "THROWS " + e.GetType().Name;
            }

            Console.WriteLine(which + ": " + result);
            return 0;
        }
    }
}

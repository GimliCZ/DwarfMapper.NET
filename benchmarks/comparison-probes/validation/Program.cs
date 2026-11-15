// SPDX-License-Identifier: GPL-2.0-only
// Round 35 T25 validation probe (benchmarks/comparison-probes/README.md). One mistake CASE per build, one LIBRARY per build:
//   -p:DefineConstants="C_<case>;L_<lib>"   lib = DWARF | MAPPERLY | MAPSTER | MAPSTER_STRICT
// The build's diagnostics are the build-time cell; running the program is the runtime cell.
#nullable enable
using System;
using Mapster;

namespace Probe;

// ── The shapes, one pair per case ────────────────────────────────────────────────────────────────────────────────
#if C_UNMAPPED_TARGET
public class Src { public int Id { get; set; } }
public class Dst { public int Id { get; set; } public string Name { get; set; } = "unset"; }
public static class Sample { public static Src Make() => new() { Id = 1 }; public static string Show(Dst d) => $"Id={d.Id} Name={d.Name}"; }
#elif C_UNMAPPED_SOURCE
public class Src { public int Id { get; set; } public string Secret { get; set; } = "s3cret"; }
public class Dst { public int Id { get; set; } }
public static class Sample { public static Src Make() => new() { Id = 1 }; public static string Show(Dst d) => $"Id={d.Id}"; }
#elif C_TYPE_MISMATCH
public class Src { public string Age { get; set; } = "abc"; }
public class Dst { public int Age { get; set; } }
public static class Sample { public static Src Make() => new() { Age = "abc" }; public static string Show(Dst d) => $"Age={d.Age}"; }
#elif C_NULL_INTO_NONNULL
public class Src { public string? Name { get; set; } }
public class Dst { public string Name { get; set; } = "unset"; }
public static class Sample { public static Src Make() => new() { Name = null }; public static string Show(Dst d) => $"Name={(d.Name is null ? "<null>" : d.Name)}"; }
#elif C_ENUM_MEMBER_MISSING
public enum Level { Low, High, Critical }
public enum LevelDto { Low, High }
public class Src { public Level Level { get; set; } }
public class Dst { public LevelDto Level { get; set; } }
public static class Sample { public static Src Make() => new() { Level = Level.Critical }; public static string Show(Dst d) => $"Level={d.Level} ({(int)d.Level})"; }
#elif C_READONLY_TARGET
public class Src { public int Id { get; set; } }
public class Dst { public int Id { get; } }
public static class Sample { public static Src Make() => new() { Id = 7 }; public static string Show(Dst d) => $"Id={d.Id}"; }
#elif C_CTOR_PARAM_NO_SOURCE
public class Src { public int Id { get; set; } }
public class Dst { public Dst(int id, string name) { Id = id; Name = name; } public int Id { get; } public string Name { get; } }
public static class Sample { public static Src Make() => new() { Id = 7 }; public static string Show(Dst d) => $"Id={d.Id} Name={(d.Name is null ? "<null>" : d.Name)}"; }
#elif C_CONVENTION_FLATTEN
public class Customer { public string Name { get; set; } = "Ada"; }
public class Src { public Customer Customer { get; set; } = new(); }
public class Dst { public string CustomerName { get; set; } = "unset"; }
public static class Sample { public static Src Make() => new(); public static string Show(Dst d) => $"CustomerName={d.CustomerName}"; }
#endif

// ── The mappers ──────────────────────────────────────────────────────────────────────────────────────────────────
#if L_DWARF
[DwarfMapper.DwarfMapper]
public partial class M { public partial Dst Map(Src s); }
public static class Run { public static Dst Map(Src s) => new M().Map(s); }
#elif L_MAPPERLY
[Riok.Mapperly.Abstractions.Mapper]
public partial class M { public partial Dst Map(Src s); }
public static class Run { public static Dst Map(Src s) => new M().Map(s); }
#elif L_MAPSTER
public static class Run { public static Dst Map(Src s) => s.Adapt<Dst>(new TypeAdapterConfig()); }
#elif L_MAPSTER_STRICT
public static class Run
{
    // Mapster's opt-in strictness: every destination member needs a source, checked when the config compiles.
    public static Dst Map(Src s)
    {
        var config = new TypeAdapterConfig { RequireDestinationMemberSource = true };
        config.NewConfig<Src, Dst>();
        config.Compile();
        return s.Adapt<Dst>(config);
    }
}
#endif

public static class Program
{
    public static int Main()
    {
        try
        {
            Console.WriteLine("PROBE-RUNTIME " + Sample.Show(Run.Map(Sample.Make())));
        }
        catch (Exception e)
        {
            var inner = e;
            while (inner.InnerException is not null) inner = inner.InnerException;
            Console.WriteLine($"PROBE-RUNTIME THROWS {e.GetType().Name} | innermost {inner.GetType().Name}: {inner.Message.Split('\n')[0]}");
        }

        return 0;
    }
}

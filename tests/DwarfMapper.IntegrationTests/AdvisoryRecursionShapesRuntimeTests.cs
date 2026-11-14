// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // The shapes of the two AutoMapper advisories of 2026-11-13, GHSA-6vhm-j9h7-f9gh (CVE-2026-128082) and
    // GHSA-gpwp-5797-mjff (CVE-2026-128081): paths on which the MaxDepth mitigation for CVE-2026-32933 did not
    // apply, so a deep input still ended in a StackOverflowException. Each is declared here with NO MaxDepth, as
    // DeepRecursionGuardRuntimeTests declares its two.

    // Recursion through a constructor parameter: the destination can only be built once its child has been.
    public sealed class AdvCtor
    {
        public AdvCtor(int v, AdvCtor? next)
        {
            V = v;
            Next = next;
        }

        public int V { get; }

        public AdvCtor? Next { get; }
    }

    public sealed class AdvCtorDto
    {
        public AdvCtorDto(int v, AdvCtorDto? next)
        {
            V = v;
            Next = next;
        }

        public int V { get; }

        public AdvCtorDto? Next { get; }
    }

    // A positional record, for which the advisory names no workaround.
    public sealed record AdvRec(int V, AdvRec? Next);

    public sealed record AdvRecDto(int V, AdvRecDto? Next);

    // A constructor-only type whose children arrive as a list.
    public sealed class AdvTree
    {
        public AdvTree(List<AdvTree> kids)
        {
            Kids = kids;
        }

        public List<AdvTree> Kids { get; }
    }

    public sealed class AdvTreeDto
    {
        public AdvTreeDto(List<AdvTreeDto> kids)
        {
            Kids = kids;
        }

        public List<AdvTreeDto> Kids { get; }
    }

    // A polymorphic chain: every level is a derived object held as its abstract base.
    public abstract class AdvAnimal
    {
        public int V { get; set; }
    }

    public sealed class AdvDog : AdvAnimal
    {
        public AdvAnimal? Next { get; set; }
    }

    public abstract class AdvAnimalDto
    {
        public int V { get; set; }
    }

    public sealed class AdvDogDto : AdvAnimalDto
    {
        public AdvAnimalDto? Next { get; set; }
    }

    // The same through an interface-typed member.
    public interface IAdvShape
    {
        int V { get; }
    }

    public sealed class AdvBox : IAdvShape
    {
        public int V { get; set; }

        public IAdvShape? Inner { get; set; }
    }

    public interface IAdvShapeDto
    {
        int V { get; }
    }

    public sealed class AdvBoxDto : IAdvShapeDto
    {
        public int V { get; set; }

        public IAdvShapeDto? Inner { get; set; }
    }

    // Recursion through a dictionary value.
    public sealed class AdvDir
    {
        public Dictionary<string, AdvDir> Kids { get; set; } = new();
    }

    public sealed class AdvDirDto
    {
        public Dictionary<string, AdvDirDto> Kids { get; set; } = new();
    }

    [DwarfMapper]
    public partial class AdvNoneMapper
    {
        public partial AdvCtorDto MapCtor(AdvCtor c);

        public partial AdvRecDto MapRec(AdvRec r);

        public partial AdvTreeDto MapTree(AdvTree t);

        [MapDerivedType<AdvDog, AdvDogDto>]
        public partial AdvAnimalDto MapAnimal(AdvAnimal a);

        [MapDerivedType<AdvBox, AdvBoxDto>]
        public partial IAdvShapeDto MapShape(IAdvShape s);

        public partial AdvDirDto MapDir(AdvDir d);
    }

    [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
    public partial class AdvSetNullMapper
    {
        public partial AdvCtorDto MapCtor(AdvCtor c);

        public partial AdvRecDto MapRec(AdvRec r);

        public partial AdvTreeDto MapTree(AdvTree t);

        [MapDerivedType<AdvDog, AdvDogDto>]
        public partial AdvAnimalDto MapAnimal(AdvAnimal a);

        [MapDerivedType<AdvBox, AdvBoxDto>]
        public partial IAdvShapeDto MapShape(IAdvShape s);

        public partial AdvDirDto MapDir(AdvDir d);
    }

    // Under Preserve a member set through a constructor cannot take part in a cycle at all - that is a build error
    // (DWARF030), held by the generator's tests - so this mapper declares the three shapes that build.
    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class AdvPreserveMapper
    {
        [MapDerivedType<AdvDog, AdvDogDto>]
        public partial AdvAnimalDto MapAnimal(AdvAnimal a);

        [MapDerivedType<AdvBox, AdvBoxDto>]
        public partial IAdvShapeDto MapShape(IAdvShape s);

        public partial AdvDirDto MapDir(AdvDir d);
    }

    /// <summary>
    ///     <b>The recursion paths of AutoMapper's 2026-11-13 advisories end in a catchable exception here.</b>
    ///     <para>
    ///         Measured first in a probe that runs one process per case, 30,000 levels deep, because the failure being
    ///         looked for kills the process: all of these threw <see cref="DwarfMappingDepthException" />
    ///         (Issues/round35, "The 2026-11-13 AutoMapper advisories"). The depth here is 500, as in
    ///         <see cref="DeepRecursionGuardRuntimeTests" />: far past the default bound of 64, and shallow enough that
    ///         a guard that regressed fails an assertion rather than ending the test host.
    ///     </para>
    /// </summary>
    public class AdvisoryRecursionShapesRuntimeTests
    {
        private const int Depth = 500;

        private static AdvCtor Ctors()
        {
            AdvCtor? c = null;
            for (var i = 0; i < Depth; i++) c = new AdvCtor(i, c);
            return c!;
        }

        private static AdvRec Recs()
        {
            AdvRec? r = null;
            for (var i = 0; i < Depth; i++) r = new AdvRec(i, r);
            return r!;
        }

        private static AdvTree Trees()
        {
            var t = new AdvTree(new List<AdvTree>());
            for (var i = 0; i < Depth; i++) t = new AdvTree(new List<AdvTree> { t });
            return t;
        }

        private static AdvAnimal Animals()
        {
            AdvAnimal? a = null;
            for (var i = 0; i < Depth; i++) a = new AdvDog { V = i, Next = a };
            return a!;
        }

        private static IAdvShape Shapes()
        {
            IAdvShape? s = null;
            for (var i = 0; i < Depth; i++) s = new AdvBox { V = i, Inner = s };
            return s!;
        }

        private static AdvDir Dirs()
        {
            var d = new AdvDir();
            for (var i = 0; i < Depth; i++)
            {
                var parent = new AdvDir();
                parent.Kids["k"] = d;
                d = parent;
            }

            return d;
        }

        [Fact]
        public void A_deep_chain_through_a_constructor_parameter_is_stopped()
        {
            Assert.Throws<DwarfMappingDepthException>(() => new AdvNoneMapper().MapCtor(Ctors()));
            Assert.Throws<DwarfMappingDepthException>(() => new AdvSetNullMapper().MapCtor(Ctors()));
        }

        [Fact]
        public void A_deep_chain_of_positional_records_is_stopped()
        {
            Assert.Throws<DwarfMappingDepthException>(() => new AdvNoneMapper().MapRec(Recs()));
            Assert.Throws<DwarfMappingDepthException>(() => new AdvSetNullMapper().MapRec(Recs()));
        }

        [Fact]
        public void A_deep_chain_through_a_constructor_parameter_that_is_a_list_is_stopped()
        {
            Assert.Throws<DwarfMappingDepthException>(() => new AdvNoneMapper().MapTree(Trees()));
            Assert.Throws<DwarfMappingDepthException>(() => new AdvSetNullMapper().MapTree(Trees()));
        }

        [Fact]
        public void A_deep_chain_of_derived_objects_is_stopped_in_every_mode()
        {
            Assert.Throws<DwarfMappingDepthException>(() => new AdvNoneMapper().MapAnimal(Animals()));
            Assert.Throws<DwarfMappingDepthException>(() => new AdvSetNullMapper().MapAnimal(Animals()));
            Assert.Throws<DwarfMappingDepthException>(() => new AdvPreserveMapper().MapAnimal(Animals()));
        }

        [Fact]
        public void A_deep_chain_through_an_interface_typed_member_is_stopped_in_every_mode()
        {
            Assert.Throws<DwarfMappingDepthException>(() => new AdvNoneMapper().MapShape(Shapes()));
            Assert.Throws<DwarfMappingDepthException>(() => new AdvSetNullMapper().MapShape(Shapes()));
            Assert.Throws<DwarfMappingDepthException>(() => new AdvPreserveMapper().MapShape(Shapes()));
        }

        [Fact]
        public void A_deep_chain_through_a_dictionary_value_is_stopped_in_every_mode()
        {
            Assert.Throws<DwarfMappingDepthException>(() => new AdvNoneMapper().MapDir(Dirs()));
            Assert.Throws<DwarfMappingDepthException>(() => new AdvSetNullMapper().MapDir(Dirs()));
            Assert.Throws<DwarfMappingDepthException>(() => new AdvPreserveMapper().MapDir(Dirs()));
        }

        // The guard is not trigger-happy on these paths either: a chain inside the bound maps, level for level.
        [Fact]
        public void A_shallow_chain_of_each_shape_maps_completely()
        {
            AdvCtor? c = null;
            AdvRec? r = null;
            AdvAnimal? a = null;
            for (var i = 0; i < 8; i++)
            {
                c = new AdvCtor(i, c);
                r = new AdvRec(i, r);
                a = new AdvDog { V = i, Next = a };
            }

            var mapper = new AdvNoneMapper();
            var ctor = mapper.MapCtor(c!);
            var rec = mapper.MapRec(r!);
            var animal = mapper.MapAnimal(a!);

            var levels = 0;
            for (var cur = ctor; cur is not null; cur = cur.Next) Assert.Equal(7 - levels++, cur.V);
            Assert.Equal(8, levels);

            levels = 0;
            for (var cur = rec; cur is not null; cur = cur.Next) Assert.Equal(7 - levels++, cur.V);
            Assert.Equal(8, levels);

            levels = 0;
            for (var cur = animal; cur is not null; cur = Assert.IsType<AdvDogDto>(cur).Next) Assert.Equal(7 - levels++, cur.V);
            Assert.Equal(8, levels);
        }
    }
}

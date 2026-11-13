// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // Four families of one shape: a zoo holding a [MapDerivedType] pair as a member, in a list, in an array and as a
    // dictionary value. "c"/"a": the base is concrete / abstract. "D"/"E": the derived type can / cannot reach back
    // to the zoo, which decides whether the dispatch is on a recursion cycle. One mapper per mode for each.

    public class DcAnimal { public string Name { get; set; } = ""; }

    public class DcDog : DcAnimal { public int Bark { get; set; } public DcZoo? Home { get; set; } }

    public class DcAnimalDto { public string Name { get; set; } = ""; }

    public class DcDogDto : DcAnimalDto { public int Bark { get; set; } public DcZooDto? Home { get; set; } }

    public class DcZoo
    {
        public DcAnimal? Star { get; set; }

        public List<DcAnimal> All { get; set; } = new();

        public DcAnimal[] Arr { get; set; } = Array.Empty<DcAnimal>();

        public Dictionary<string, DcAnimal> ByName { get; set; } = new();
    }

    public class DcZooDto
    {
        public DcAnimalDto? Star { get; set; }

        public List<DcAnimalDto> All { get; set; } = new();

        public DcAnimalDto[] Arr { get; set; } = Array.Empty<DcAnimalDto>();

        public Dictionary<string, DcAnimalDto> ByName { get; set; } = new();
    }

    [DwarfMapper]
    public partial class DcNoneMapper
    {
        [MapDerivedType<DcDog, DcDogDto>]
        public partial DcAnimalDto MapAnimal(DcAnimal a);

        public partial DcZooDto Map(DcZoo z);
    }

    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class DcPreserveMapper
    {
        [MapDerivedType<DcDog, DcDogDto>]
        public partial DcAnimalDto MapAnimal(DcAnimal a);

        public partial DcZooDto Map(DcZoo z);
    }

    [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
    public partial class DcSetNullMapper
    {
        [MapDerivedType<DcDog, DcDogDto>]
        public partial DcAnimalDto MapAnimal(DcAnimal a);

        public partial DcZooDto Map(DcZoo z);
    }

    public abstract class DaAnimal { public string Name { get; set; } = ""; }

    public class DaDog : DaAnimal { public int Bark { get; set; } public DaZoo? Home { get; set; } }

    public abstract class DaAnimalDto { public string Name { get; set; } = ""; }

    public class DaDogDto : DaAnimalDto { public int Bark { get; set; } public DaZooDto? Home { get; set; } }

    public class DaZoo
    {
        public DaAnimal? Star { get; set; }

        public List<DaAnimal> All { get; set; } = new();

        public DaAnimal[] Arr { get; set; } = Array.Empty<DaAnimal>();

        public Dictionary<string, DaAnimal> ByName { get; set; } = new();
    }

    public class DaZooDto
    {
        public DaAnimalDto? Star { get; set; }

        public List<DaAnimalDto> All { get; set; } = new();

        public DaAnimalDto[] Arr { get; set; } = Array.Empty<DaAnimalDto>();

        public Dictionary<string, DaAnimalDto> ByName { get; set; } = new();
    }

    [DwarfMapper]
    public partial class DaNoneMapper
    {
        [MapDerivedType<DaDog, DaDogDto>]
        public partial DaAnimalDto MapAnimal(DaAnimal a);

        public partial DaZooDto Map(DaZoo z);
    }

    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class DaPreserveMapper
    {
        [MapDerivedType<DaDog, DaDogDto>]
        public partial DaAnimalDto MapAnimal(DaAnimal a);

        public partial DaZooDto Map(DaZoo z);
    }

    [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
    public partial class DaSetNullMapper
    {
        [MapDerivedType<DaDog, DaDogDto>]
        public partial DaAnimalDto MapAnimal(DaAnimal a);

        public partial DaZooDto Map(DaZoo z);
    }

    public class EcAnimal { public string Name { get; set; } = ""; }

    public class EcDog : EcAnimal { public int Bark { get; set; } }

    public class EcAnimalDto { public string Name { get; set; } = ""; }

    public class EcDogDto : EcAnimalDto { public int Bark { get; set; } }

    public class EcZoo
    {
        public EcAnimal? Star { get; set; }

        public List<EcAnimal> All { get; set; } = new();

        public EcAnimal[] Arr { get; set; } = Array.Empty<EcAnimal>();

        public Dictionary<string, EcAnimal> ByName { get; set; } = new();
    }

    public class EcZooDto
    {
        public EcAnimalDto? Star { get; set; }

        public List<EcAnimalDto> All { get; set; } = new();

        public EcAnimalDto[] Arr { get; set; } = Array.Empty<EcAnimalDto>();

        public Dictionary<string, EcAnimalDto> ByName { get; set; } = new();
    }

    [DwarfMapper]
    public partial class EcNoneMapper
    {
        [MapDerivedType<EcDog, EcDogDto>]
        public partial EcAnimalDto MapAnimal(EcAnimal a);

        public partial EcZooDto Map(EcZoo z);
    }

    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class EcPreserveMapper
    {
        [MapDerivedType<EcDog, EcDogDto>]
        public partial EcAnimalDto MapAnimal(EcAnimal a);

        public partial EcZooDto Map(EcZoo z);
    }

    [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
    public partial class EcSetNullMapper
    {
        [MapDerivedType<EcDog, EcDogDto>]
        public partial EcAnimalDto MapAnimal(EcAnimal a);

        public partial EcZooDto Map(EcZoo z);
    }

    public abstract class EaAnimal { public string Name { get; set; } = ""; }

    public class EaDog : EaAnimal { public int Bark { get; set; } }

    public abstract class EaAnimalDto { public string Name { get; set; } = ""; }

    public class EaDogDto : EaAnimalDto { public int Bark { get; set; } }

    public class EaZoo
    {
        public EaAnimal? Star { get; set; }

        public List<EaAnimal> All { get; set; } = new();

        public EaAnimal[] Arr { get; set; } = Array.Empty<EaAnimal>();

        public Dictionary<string, EaAnimal> ByName { get; set; } = new();
    }

    public class EaZooDto
    {
        public EaAnimalDto? Star { get; set; }

        public List<EaAnimalDto> All { get; set; } = new();

        public EaAnimalDto[] Arr { get; set; } = Array.Empty<EaAnimalDto>();

        public Dictionary<string, EaAnimalDto> ByName { get; set; } = new();
    }

    [DwarfMapper]
    public partial class EaNoneMapper
    {
        [MapDerivedType<EaDog, EaDogDto>]
        public partial EaAnimalDto MapAnimal(EaAnimal a);

        public partial EaZooDto Map(EaZoo z);
    }

    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class EaPreserveMapper
    {
        [MapDerivedType<EaDog, EaDogDto>]
        public partial EaAnimalDto MapAnimal(EaAnimal a);

        public partial EaZooDto Map(EaZoo z);
    }

    [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
    public partial class EaSetNullMapper
    {
        [MapDerivedType<EaDog, EaDogDto>]
        public partial EaAnimalDto MapAnimal(EaAnimal a);

        public partial EaZooDto Map(EaZoo z);
    }

    /// <summary>
    ///     <b>A <c>[MapDerivedType]</c> pair inside another map dispatches, keeps the shared context, and survives a
    ///     cycle</b> - as a member, a list element, an array element and a dictionary value, with a concrete and an
    ///     abstract base, in each mode (round 35, T12 to T14).
    ///     <para>
    ///         Before: with an abstract base the collection helpers called the public dispatch on a fresh context
    ///         (a cycle through the list was a stack overflow in every mode - the process died, which is why these
    ///         facts could not have been written as tests then - and Preserve made two objects of one); with a
    ///         concrete base under Preserve or SetNull the dispatch was skipped and a dog came out as an
    ///         <c>AnimalDto</c>; SetNull with a cycle through the arm did not compile.
    ///     </para>
    /// </summary>
    public class DispatchPairNestedRuntimeTests
    {

        [Fact]
        public void Dc_None_every_nested_use_dispatches_on_the_runtime_type()
        {
            var dog = new DcDog { Name = "rex", Bark = 3 };
            var zoo = new DcZoo { Star = dog, All = { dog, dog }, Arr = new DcAnimal[] { dog }, ByName = { ["rex"] = dog } };

            var dto = new DcNoneMapper().Map(zoo);

            Assert.Equal(3, Assert.IsType<DcDogDto>(dto.Star).Bark);
            Assert.Equal(3, Assert.IsType<DcDogDto>(dto.All[0]).Bark);
            Assert.Equal(3, Assert.IsType<DcDogDto>(dto.Arr[0]).Bark);
            Assert.Equal(3, Assert.IsType<DcDogDto>(dto.ByName["rex"]).Bark);

        }


        [Fact]
        public void Dc_None_a_cycle_through_the_member_ends_as_the_mode_says()
        {
            var zoo = new DcZoo();
            var dog = new DcDog { Name = "rex", Home = zoo };
            zoo.Star = dog;


            Assert.Throws<DwarfMappingDepthException>(() => new DcNoneMapper().Map(zoo));
        }


        [Fact]
        public void Dc_None_a_cycle_through_the_list_ends_as_the_mode_says()
        {
            var zoo = new DcZoo();
            var dog = new DcDog { Name = "rex", Home = zoo };
            zoo.All.Add(dog);


            Assert.Throws<DwarfMappingDepthException>(() => new DcNoneMapper().Map(zoo));
        }


        [Fact]
        public void Dc_None_a_cycle_through_the_array_ends_as_the_mode_says()
        {
            var zoo = new DcZoo();
            var dog = new DcDog { Name = "rex", Home = zoo };
            zoo.Arr = new DcAnimal[] { dog };


            Assert.Throws<DwarfMappingDepthException>(() => new DcNoneMapper().Map(zoo));
        }


        [Fact]
        public void Dc_None_a_cycle_through_the_dictionary_ends_as_the_mode_says()
        {
            var zoo = new DcZoo();
            var dog = new DcDog { Name = "rex", Home = zoo };
            zoo.ByName["rex"] = dog;


            Assert.Throws<DwarfMappingDepthException>(() => new DcNoneMapper().Map(zoo));
        }


        // No arm names the base type itself. Called directly the dispatch refuses such an instance, and it refuses it
        // the same on every nested route and in every mode. (Under Preserve and SetNull the nested routes used to
        // map it as the base type - by skipping the dispatch, which is also what turned a dog into an AnimalDto.)
        [Fact]
        public void Dc_None_an_instance_of_the_base_type_itself_is_refused_on_every_route()
        {
            var plain = new DcAnimal { Name = "plain" };
            var mapper = new DcNoneMapper();

            Assert.Throws<ArgumentException>(() => mapper.MapAnimal(plain));
            Assert.Throws<ArgumentException>(() => mapper.Map(new DcZoo { Star = plain }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new DcZoo { All = { plain } }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new DcZoo { Arr = new[] { plain } }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new DcZoo { ByName = { ["plain"] = plain } }));
        }


        [Fact]
        public void Dc_Preserve_every_nested_use_dispatches_on_the_runtime_type()
        {
            var dog = new DcDog { Name = "rex", Bark = 3 };
            var zoo = new DcZoo { Star = dog, All = { dog, dog }, Arr = new DcAnimal[] { dog }, ByName = { ["rex"] = dog } };

            var dto = new DcPreserveMapper().Map(zoo);

            Assert.Equal(3, Assert.IsType<DcDogDto>(dto.Star).Bark);
            Assert.Equal(3, Assert.IsType<DcDogDto>(dto.All[0]).Bark);
            Assert.Equal(3, Assert.IsType<DcDogDto>(dto.Arr[0]).Bark);
            Assert.Equal(3, Assert.IsType<DcDogDto>(dto.ByName["rex"]).Bark);

            // One source object, one target object, whichever edge reached it.
            Assert.Same(dto.Star, dto.All[0]);
            Assert.Same(dto.All[0], dto.All[1]);
            Assert.Same(dto.Star, dto.Arr[0]);
            Assert.Same(dto.Star, dto.ByName["rex"]);

        }


        [Fact]
        public void Dc_Preserve_a_cycle_through_the_member_ends_as_the_mode_says()
        {
            var zoo = new DcZoo();
            var dog = new DcDog { Name = "rex", Home = zoo };
            zoo.Star = dog;


            var dto = new DcPreserveMapper().Map(zoo);

            Assert.Same(dto, Assert.IsType<DcDogDto>(dto.Star).Home);
        }


        [Fact]
        public void Dc_Preserve_a_cycle_through_the_list_ends_as_the_mode_says()
        {
            var zoo = new DcZoo();
            var dog = new DcDog { Name = "rex", Home = zoo };
            zoo.All.Add(dog);


            var dto = new DcPreserveMapper().Map(zoo);

            Assert.Same(dto, Assert.IsType<DcDogDto>(dto.All[0]).Home);
        }


        [Fact]
        public void Dc_Preserve_a_cycle_through_the_array_ends_as_the_mode_says()
        {
            var zoo = new DcZoo();
            var dog = new DcDog { Name = "rex", Home = zoo };
            zoo.Arr = new DcAnimal[] { dog };


            var dto = new DcPreserveMapper().Map(zoo);

            Assert.Same(dto, Assert.IsType<DcDogDto>(dto.Arr[0]).Home);
        }


        [Fact]
        public void Dc_Preserve_a_cycle_through_the_dictionary_ends_as_the_mode_says()
        {
            var zoo = new DcZoo();
            var dog = new DcDog { Name = "rex", Home = zoo };
            zoo.ByName["rex"] = dog;


            var dto = new DcPreserveMapper().Map(zoo);

            Assert.Same(dto, Assert.IsType<DcDogDto>(dto.ByName["rex"]).Home);
        }


        // No arm names the base type itself. Called directly the dispatch refuses such an instance, and it refuses it
        // the same on every nested route and in every mode. (Under Preserve and SetNull the nested routes used to
        // map it as the base type - by skipping the dispatch, which is also what turned a dog into an AnimalDto.)
        [Fact]
        public void Dc_Preserve_an_instance_of_the_base_type_itself_is_refused_on_every_route()
        {
            var plain = new DcAnimal { Name = "plain" };
            var mapper = new DcPreserveMapper();

            Assert.Throws<ArgumentException>(() => mapper.MapAnimal(plain));
            Assert.Throws<ArgumentException>(() => mapper.Map(new DcZoo { Star = plain }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new DcZoo { All = { plain } }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new DcZoo { Arr = new[] { plain } }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new DcZoo { ByName = { ["plain"] = plain } }));
        }


        [Fact]
        public void Dc_SetNull_every_nested_use_dispatches_on_the_runtime_type()
        {
            var dog = new DcDog { Name = "rex", Bark = 3 };
            var zoo = new DcZoo { Star = dog, All = { dog, dog }, Arr = new DcAnimal[] { dog }, ByName = { ["rex"] = dog } };

            var dto = new DcSetNullMapper().Map(zoo);

            Assert.Equal(3, Assert.IsType<DcDogDto>(dto.Star).Bark);
            Assert.Equal(3, Assert.IsType<DcDogDto>(dto.All[0]).Bark);
            Assert.Equal(3, Assert.IsType<DcDogDto>(dto.Arr[0]).Bark);
            Assert.Equal(3, Assert.IsType<DcDogDto>(dto.ByName["rex"]).Bark);

        }


        [Fact]
        public void Dc_SetNull_a_cycle_through_the_member_ends_as_the_mode_says()
        {
            var zoo = new DcZoo();
            var dog = new DcDog { Name = "rex", Home = zoo };
            zoo.Star = dog;


            var dto = new DcSetNullMapper().Map(zoo);

            Assert.Null(Assert.IsType<DcDogDto>(dto.Star).Home);
        }


        [Fact]
        public void Dc_SetNull_a_cycle_through_the_list_ends_as_the_mode_says()
        {
            var zoo = new DcZoo();
            var dog = new DcDog { Name = "rex", Home = zoo };
            zoo.All.Add(dog);


            var dto = new DcSetNullMapper().Map(zoo);

            Assert.Null(Assert.IsType<DcDogDto>(dto.All[0]).Home);
        }


        [Fact]
        public void Dc_SetNull_a_cycle_through_the_array_ends_as_the_mode_says()
        {
            var zoo = new DcZoo();
            var dog = new DcDog { Name = "rex", Home = zoo };
            zoo.Arr = new DcAnimal[] { dog };


            var dto = new DcSetNullMapper().Map(zoo);

            Assert.Null(Assert.IsType<DcDogDto>(dto.Arr[0]).Home);
        }


        [Fact]
        public void Dc_SetNull_a_cycle_through_the_dictionary_ends_as_the_mode_says()
        {
            var zoo = new DcZoo();
            var dog = new DcDog { Name = "rex", Home = zoo };
            zoo.ByName["rex"] = dog;


            var dto = new DcSetNullMapper().Map(zoo);

            Assert.Null(Assert.IsType<DcDogDto>(dto.ByName["rex"]).Home);
        }


        // No arm names the base type itself. Called directly the dispatch refuses such an instance, and it refuses it
        // the same on every nested route and in every mode. (Under Preserve and SetNull the nested routes used to
        // map it as the base type - by skipping the dispatch, which is also what turned a dog into an AnimalDto.)
        [Fact]
        public void Dc_SetNull_an_instance_of_the_base_type_itself_is_refused_on_every_route()
        {
            var plain = new DcAnimal { Name = "plain" };
            var mapper = new DcSetNullMapper();

            Assert.Throws<ArgumentException>(() => mapper.MapAnimal(plain));
            Assert.Throws<ArgumentException>(() => mapper.Map(new DcZoo { Star = plain }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new DcZoo { All = { plain } }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new DcZoo { Arr = new[] { plain } }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new DcZoo { ByName = { ["plain"] = plain } }));
        }


        [Fact]
        public void Da_None_every_nested_use_dispatches_on_the_runtime_type()
        {
            var dog = new DaDog { Name = "rex", Bark = 3 };
            var zoo = new DaZoo { Star = dog, All = { dog, dog }, Arr = new DaAnimal[] { dog }, ByName = { ["rex"] = dog } };

            var dto = new DaNoneMapper().Map(zoo);

            Assert.Equal(3, Assert.IsType<DaDogDto>(dto.Star).Bark);
            Assert.Equal(3, Assert.IsType<DaDogDto>(dto.All[0]).Bark);
            Assert.Equal(3, Assert.IsType<DaDogDto>(dto.Arr[0]).Bark);
            Assert.Equal(3, Assert.IsType<DaDogDto>(dto.ByName["rex"]).Bark);

        }


        [Fact]
        public void Da_None_a_cycle_through_the_member_ends_as_the_mode_says()
        {
            var zoo = new DaZoo();
            var dog = new DaDog { Name = "rex", Home = zoo };
            zoo.Star = dog;


            Assert.Throws<DwarfMappingDepthException>(() => new DaNoneMapper().Map(zoo));
        }


        [Fact]
        public void Da_None_a_cycle_through_the_list_ends_as_the_mode_says()
        {
            var zoo = new DaZoo();
            var dog = new DaDog { Name = "rex", Home = zoo };
            zoo.All.Add(dog);


            Assert.Throws<DwarfMappingDepthException>(() => new DaNoneMapper().Map(zoo));
        }


        [Fact]
        public void Da_None_a_cycle_through_the_array_ends_as_the_mode_says()
        {
            var zoo = new DaZoo();
            var dog = new DaDog { Name = "rex", Home = zoo };
            zoo.Arr = new DaAnimal[] { dog };


            Assert.Throws<DwarfMappingDepthException>(() => new DaNoneMapper().Map(zoo));
        }


        [Fact]
        public void Da_None_a_cycle_through_the_dictionary_ends_as_the_mode_says()
        {
            var zoo = new DaZoo();
            var dog = new DaDog { Name = "rex", Home = zoo };
            zoo.ByName["rex"] = dog;


            Assert.Throws<DwarfMappingDepthException>(() => new DaNoneMapper().Map(zoo));
        }


        [Fact]
        public void Da_Preserve_every_nested_use_dispatches_on_the_runtime_type()
        {
            var dog = new DaDog { Name = "rex", Bark = 3 };
            var zoo = new DaZoo { Star = dog, All = { dog, dog }, Arr = new DaAnimal[] { dog }, ByName = { ["rex"] = dog } };

            var dto = new DaPreserveMapper().Map(zoo);

            Assert.Equal(3, Assert.IsType<DaDogDto>(dto.Star).Bark);
            Assert.Equal(3, Assert.IsType<DaDogDto>(dto.All[0]).Bark);
            Assert.Equal(3, Assert.IsType<DaDogDto>(dto.Arr[0]).Bark);
            Assert.Equal(3, Assert.IsType<DaDogDto>(dto.ByName["rex"]).Bark);

            // One source object, one target object, whichever edge reached it.
            Assert.Same(dto.Star, dto.All[0]);
            Assert.Same(dto.All[0], dto.All[1]);
            Assert.Same(dto.Star, dto.Arr[0]);
            Assert.Same(dto.Star, dto.ByName["rex"]);

        }


        [Fact]
        public void Da_Preserve_a_cycle_through_the_member_ends_as_the_mode_says()
        {
            var zoo = new DaZoo();
            var dog = new DaDog { Name = "rex", Home = zoo };
            zoo.Star = dog;


            var dto = new DaPreserveMapper().Map(zoo);

            Assert.Same(dto, Assert.IsType<DaDogDto>(dto.Star).Home);
        }


        [Fact]
        public void Da_Preserve_a_cycle_through_the_list_ends_as_the_mode_says()
        {
            var zoo = new DaZoo();
            var dog = new DaDog { Name = "rex", Home = zoo };
            zoo.All.Add(dog);


            var dto = new DaPreserveMapper().Map(zoo);

            Assert.Same(dto, Assert.IsType<DaDogDto>(dto.All[0]).Home);
        }


        [Fact]
        public void Da_Preserve_a_cycle_through_the_array_ends_as_the_mode_says()
        {
            var zoo = new DaZoo();
            var dog = new DaDog { Name = "rex", Home = zoo };
            zoo.Arr = new DaAnimal[] { dog };


            var dto = new DaPreserveMapper().Map(zoo);

            Assert.Same(dto, Assert.IsType<DaDogDto>(dto.Arr[0]).Home);
        }


        [Fact]
        public void Da_Preserve_a_cycle_through_the_dictionary_ends_as_the_mode_says()
        {
            var zoo = new DaZoo();
            var dog = new DaDog { Name = "rex", Home = zoo };
            zoo.ByName["rex"] = dog;


            var dto = new DaPreserveMapper().Map(zoo);

            Assert.Same(dto, Assert.IsType<DaDogDto>(dto.ByName["rex"]).Home);
        }


        [Fact]
        public void Da_SetNull_every_nested_use_dispatches_on_the_runtime_type()
        {
            var dog = new DaDog { Name = "rex", Bark = 3 };
            var zoo = new DaZoo { Star = dog, All = { dog, dog }, Arr = new DaAnimal[] { dog }, ByName = { ["rex"] = dog } };

            var dto = new DaSetNullMapper().Map(zoo);

            Assert.Equal(3, Assert.IsType<DaDogDto>(dto.Star).Bark);
            Assert.Equal(3, Assert.IsType<DaDogDto>(dto.All[0]).Bark);
            Assert.Equal(3, Assert.IsType<DaDogDto>(dto.Arr[0]).Bark);
            Assert.Equal(3, Assert.IsType<DaDogDto>(dto.ByName["rex"]).Bark);

        }


        [Fact]
        public void Da_SetNull_a_cycle_through_the_member_ends_as_the_mode_says()
        {
            var zoo = new DaZoo();
            var dog = new DaDog { Name = "rex", Home = zoo };
            zoo.Star = dog;


            var dto = new DaSetNullMapper().Map(zoo);

            Assert.Null(Assert.IsType<DaDogDto>(dto.Star).Home);
        }


        [Fact]
        public void Da_SetNull_a_cycle_through_the_list_ends_as_the_mode_says()
        {
            var zoo = new DaZoo();
            var dog = new DaDog { Name = "rex", Home = zoo };
            zoo.All.Add(dog);


            var dto = new DaSetNullMapper().Map(zoo);

            Assert.Null(Assert.IsType<DaDogDto>(dto.All[0]).Home);
        }


        [Fact]
        public void Da_SetNull_a_cycle_through_the_array_ends_as_the_mode_says()
        {
            var zoo = new DaZoo();
            var dog = new DaDog { Name = "rex", Home = zoo };
            zoo.Arr = new DaAnimal[] { dog };


            var dto = new DaSetNullMapper().Map(zoo);

            Assert.Null(Assert.IsType<DaDogDto>(dto.Arr[0]).Home);
        }


        [Fact]
        public void Da_SetNull_a_cycle_through_the_dictionary_ends_as_the_mode_says()
        {
            var zoo = new DaZoo();
            var dog = new DaDog { Name = "rex", Home = zoo };
            zoo.ByName["rex"] = dog;


            var dto = new DaSetNullMapper().Map(zoo);

            Assert.Null(Assert.IsType<DaDogDto>(dto.ByName["rex"]).Home);
        }


        [Fact]
        public void Ec_None_every_nested_use_dispatches_on_the_runtime_type()
        {
            var dog = new EcDog { Name = "rex", Bark = 3 };
            var zoo = new EcZoo { Star = dog, All = { dog, dog }, Arr = new EcAnimal[] { dog }, ByName = { ["rex"] = dog } };

            var dto = new EcNoneMapper().Map(zoo);

            Assert.Equal(3, Assert.IsType<EcDogDto>(dto.Star).Bark);
            Assert.Equal(3, Assert.IsType<EcDogDto>(dto.All[0]).Bark);
            Assert.Equal(3, Assert.IsType<EcDogDto>(dto.Arr[0]).Bark);
            Assert.Equal(3, Assert.IsType<EcDogDto>(dto.ByName["rex"]).Bark);

        }


        // No arm names the base type itself. Called directly the dispatch refuses such an instance, and it refuses it
        // the same on every nested route and in every mode. (Under Preserve and SetNull the nested routes used to
        // map it as the base type - by skipping the dispatch, which is also what turned a dog into an AnimalDto.)
        [Fact]
        public void Ec_None_an_instance_of_the_base_type_itself_is_refused_on_every_route()
        {
            var plain = new EcAnimal { Name = "plain" };
            var mapper = new EcNoneMapper();

            Assert.Throws<ArgumentException>(() => mapper.MapAnimal(plain));
            Assert.Throws<ArgumentException>(() => mapper.Map(new EcZoo { Star = plain }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new EcZoo { All = { plain } }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new EcZoo { Arr = new[] { plain } }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new EcZoo { ByName = { ["plain"] = plain } }));
        }


        [Fact]
        public void Ec_Preserve_every_nested_use_dispatches_on_the_runtime_type()
        {
            var dog = new EcDog { Name = "rex", Bark = 3 };
            var zoo = new EcZoo { Star = dog, All = { dog, dog }, Arr = new EcAnimal[] { dog }, ByName = { ["rex"] = dog } };

            var dto = new EcPreserveMapper().Map(zoo);

            Assert.Equal(3, Assert.IsType<EcDogDto>(dto.Star).Bark);
            Assert.Equal(3, Assert.IsType<EcDogDto>(dto.All[0]).Bark);
            Assert.Equal(3, Assert.IsType<EcDogDto>(dto.Arr[0]).Bark);
            Assert.Equal(3, Assert.IsType<EcDogDto>(dto.ByName["rex"]).Bark);

            // One source object, one target object, whichever edge reached it.
            Assert.Same(dto.Star, dto.All[0]);
            Assert.Same(dto.All[0], dto.All[1]);
            Assert.Same(dto.Star, dto.Arr[0]);
            Assert.Same(dto.Star, dto.ByName["rex"]);

        }


        // No arm names the base type itself. Called directly the dispatch refuses such an instance, and it refuses it
        // the same on every nested route and in every mode. (Under Preserve and SetNull the nested routes used to
        // map it as the base type - by skipping the dispatch, which is also what turned a dog into an AnimalDto.)
        [Fact]
        public void Ec_Preserve_an_instance_of_the_base_type_itself_is_refused_on_every_route()
        {
            var plain = new EcAnimal { Name = "plain" };
            var mapper = new EcPreserveMapper();

            Assert.Throws<ArgumentException>(() => mapper.MapAnimal(plain));
            Assert.Throws<ArgumentException>(() => mapper.Map(new EcZoo { Star = plain }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new EcZoo { All = { plain } }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new EcZoo { Arr = new[] { plain } }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new EcZoo { ByName = { ["plain"] = plain } }));
        }


        [Fact]
        public void Ec_SetNull_every_nested_use_dispatches_on_the_runtime_type()
        {
            var dog = new EcDog { Name = "rex", Bark = 3 };
            var zoo = new EcZoo { Star = dog, All = { dog, dog }, Arr = new EcAnimal[] { dog }, ByName = { ["rex"] = dog } };

            var dto = new EcSetNullMapper().Map(zoo);

            Assert.Equal(3, Assert.IsType<EcDogDto>(dto.Star).Bark);
            Assert.Equal(3, Assert.IsType<EcDogDto>(dto.All[0]).Bark);
            Assert.Equal(3, Assert.IsType<EcDogDto>(dto.Arr[0]).Bark);
            Assert.Equal(3, Assert.IsType<EcDogDto>(dto.ByName["rex"]).Bark);

        }


        // No arm names the base type itself. Called directly the dispatch refuses such an instance, and it refuses it
        // the same on every nested route and in every mode. (Under Preserve and SetNull the nested routes used to
        // map it as the base type - by skipping the dispatch, which is also what turned a dog into an AnimalDto.)
        [Fact]
        public void Ec_SetNull_an_instance_of_the_base_type_itself_is_refused_on_every_route()
        {
            var plain = new EcAnimal { Name = "plain" };
            var mapper = new EcSetNullMapper();

            Assert.Throws<ArgumentException>(() => mapper.MapAnimal(plain));
            Assert.Throws<ArgumentException>(() => mapper.Map(new EcZoo { Star = plain }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new EcZoo { All = { plain } }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new EcZoo { Arr = new[] { plain } }));
            Assert.Throws<ArgumentException>(() => mapper.Map(new EcZoo { ByName = { ["plain"] = plain } }));
        }


        [Fact]
        public void Ea_None_every_nested_use_dispatches_on_the_runtime_type()
        {
            var dog = new EaDog { Name = "rex", Bark = 3 };
            var zoo = new EaZoo { Star = dog, All = { dog, dog }, Arr = new EaAnimal[] { dog }, ByName = { ["rex"] = dog } };

            var dto = new EaNoneMapper().Map(zoo);

            Assert.Equal(3, Assert.IsType<EaDogDto>(dto.Star).Bark);
            Assert.Equal(3, Assert.IsType<EaDogDto>(dto.All[0]).Bark);
            Assert.Equal(3, Assert.IsType<EaDogDto>(dto.Arr[0]).Bark);
            Assert.Equal(3, Assert.IsType<EaDogDto>(dto.ByName["rex"]).Bark);

        }


        [Fact]
        public void Ea_Preserve_every_nested_use_dispatches_on_the_runtime_type()
        {
            var dog = new EaDog { Name = "rex", Bark = 3 };
            var zoo = new EaZoo { Star = dog, All = { dog, dog }, Arr = new EaAnimal[] { dog }, ByName = { ["rex"] = dog } };

            var dto = new EaPreserveMapper().Map(zoo);

            Assert.Equal(3, Assert.IsType<EaDogDto>(dto.Star).Bark);
            Assert.Equal(3, Assert.IsType<EaDogDto>(dto.All[0]).Bark);
            Assert.Equal(3, Assert.IsType<EaDogDto>(dto.Arr[0]).Bark);
            Assert.Equal(3, Assert.IsType<EaDogDto>(dto.ByName["rex"]).Bark);

            // One source object, one target object, whichever edge reached it.
            Assert.Same(dto.Star, dto.All[0]);
            Assert.Same(dto.All[0], dto.All[1]);
            Assert.Same(dto.Star, dto.Arr[0]);
            Assert.Same(dto.Star, dto.ByName["rex"]);

        }


        [Fact]
        public void Ea_SetNull_every_nested_use_dispatches_on_the_runtime_type()
        {
            var dog = new EaDog { Name = "rex", Bark = 3 };
            var zoo = new EaZoo { Star = dog, All = { dog, dog }, Arr = new EaAnimal[] { dog }, ByName = { ["rex"] = dog } };

            var dto = new EaSetNullMapper().Map(zoo);

            Assert.Equal(3, Assert.IsType<EaDogDto>(dto.Star).Bark);
            Assert.Equal(3, Assert.IsType<EaDogDto>(dto.All[0]).Bark);
            Assert.Equal(3, Assert.IsType<EaDogDto>(dto.Arr[0]).Bark);
            Assert.Equal(3, Assert.IsType<EaDogDto>(dto.ByName["rex"]).Bark);

        }
    }
}

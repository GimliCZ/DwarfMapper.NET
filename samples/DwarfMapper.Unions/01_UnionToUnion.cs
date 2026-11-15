// SPDX-License-Identifier: GPL-2.0-only

// 01 - One union into another. Declare the union map and a map for each case; the generated method switches over
// the source union's case types and wraps each result into the target union. Nothing is paired by member shape or
// by name: here each case is paired by the declared method that maps it into a case of PetDto.

namespace DwarfMapper.Unions.Ex01
{
    public record class Cat(string Name, int Lives);

    public record class Dog(string Name, bool Trained);

    public record class CatDto(string Name, int Lives);

    public record class DogDto(string Name, bool Trained);

    public union Pet(Cat, Dog);

    public union PetDto(CatDto, DogDto);

// <snippet: union-map>
    [DwarfMapper]
    public partial class PetMapper
    {
        public partial PetDto ToDto(Pet pet); // a switch over Pet's cases

        public partial CatDto ToDto(Cat cat); // pairs the Cat case with CatDto

        public partial DogDto ToDto(Dog dog); // pairs the Dog case with DogDto
    }
// </snippet>

    public static class Example
    {
        public static string Run()
        {
            var mapper = new PetMapper();
            var cat = mapper.ToDto(new Pet(new Cat("Tom", 9)));
            var dog = mapper.ToDto(new Pet(new Dog("Rex", true)));
            return cat.Value + " | " + dog.Value;
        }

        public const string Expected = "CatDto { Name = Tom, Lives = 9 } | DogDto { Name = Rex, Trained = True }";
    }
}

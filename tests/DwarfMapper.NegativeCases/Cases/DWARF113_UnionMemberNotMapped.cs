// SPDX-License-Identifier: GPL-2.0-only
// CASE: a member whose source is a C# 15 union type and whose destination is a different union type.
// WHY:  Round 31 T24 / research C3. .NET 11 ships unions on 2026-11-10 and DwarfMapper has no policy for them yet.
//       Before this id, a union was handled as an ordinary struct: into 'object' it boxed the WRAPPER silently, and
//       into another union it was refused as DWARF025 "ambiguous constructor" — the wrong problem, since one
//       constructor per case type is simply a union's shape. The union here is declared by hand the way the C# 15
//       compiler lowers `union Pet(Cat, Dog)`, which is also how the detection is keyed: by the attribute's name.
// EXPECT: DWARF113, DWARF078
// EXPECT-MESSAGE DWARF113: 'Pet' maps 'Demo.Pet' to 'Demo.PetDto'
// EXPECT-MESSAGE DWARF113: is a C# 15 union type
// EXPECT-MESSAGE DWARF113: Convert it yourself with [MapProperty(Use = nameof(...))]
// EXPECT-CS: CS8795
// NOTE: DWARF078 and CS8795 are the documented refusal cascade: an Error here means the mapper is not generated.

#nullable enable

using DwarfMapper;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class UnionAttribute : Attribute
    {
    }

    public interface IUnion
    {
        object? Value { get; }
    }
}

namespace Demo
{
    public record class Cat(string Name);

    public record class Dog(string Name);

    public record class CatDto(string Name);

    public record class DogDto(string Name);

    [System.Runtime.CompilerServices.Union]
    public struct Pet : System.Runtime.CompilerServices.IUnion
    {
        public Pet(Cat c) { Value = c; }

        public Pet(Dog d) { Value = d; }

        public object? Value { get; }
    }

    [System.Runtime.CompilerServices.Union]
    public struct PetDto : System.Runtime.CompilerServices.IUnion
    {
        public PetDto(CatDto c) { Value = c; }

        public PetDto(DogDto d) { Value = d; }

        public object? Value { get; }
    }

    public class Src
    {
        public Pet Pet { get; set; }
    }

    public class Dst
    {
        public PetDto Pet { get; set; }
    }

    [DwarfMapper]
    public partial class M
    {
        public partial Dst Map(Src s);
    }
}

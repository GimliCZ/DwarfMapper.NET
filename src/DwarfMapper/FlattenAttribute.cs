// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     Flattens a complex source member: its readable sub-members are mapped to
///     destination members of the same name (e.g. <c>Address.City → City</c>). Apply to a mapping method.
/// </summary>
[DwarfSurface(SurfaceCategory.ConsumerDirective, ProbeKey = "nested-pair")]
// `Child` is the fixture's only COMPLEX member, and a flatten directive naming anything else has no
// sub-members to pull up. The sampled "Id" named an int, so every cell measured the generator's reaction to
// nonsense rather than to a flatten.
[DwarfSurfaceProbe(constructorArity: 1, Arguments = "{Child}")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class FlattenAttribute : Attribute
{
    /// <summary>Creates a flatten directive for the named source member.</summary>
    /// <param name="sourceMember">Name of the complex source member to flatten.</param>
    public FlattenAttribute(string sourceMember)
    {
        SourceMember = sourceMember;
    }

    /// <summary>Name of the complex source member whose sub-members are pulled up.</summary>
    public string SourceMember { get; }
}

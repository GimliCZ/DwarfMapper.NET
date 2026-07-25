// SPDX-License-Identifier: GPL-2.0-only
#nullable enable
using Xunit;

namespace DwarfMapper.IntegrationTests;

// Preserve identity THROUGH a value-type (struct / record struct) node.
//
// ReferenceHandling=Preserve de-duplicates shared reference-type objects: a graph where two edges point at the
// same instance maps that instance once, and both mapped edges point at the same result. A struct in the path
// is the awkward case — the struct itself is a value (copied, never identity-tracked), but a REFERENCE it
// carries must still be de-duplicated. If the identity map were keyed on the struct, or the struct copy broke
// the tracking, two structs sharing one referent would map to two distinct referents — a silently wrong
// topology that no value-level comparison would catch.
//
// Verified here for both a plain struct and a readonly record struct node.

public sealed class PsShared { public int V { get; set; } }
public struct PsHolder { public PsShared Ref { get; set; } }
public sealed class PsRoot { public PsHolder H1 { get; set; } public PsHolder H2 { get; set; } }

public sealed class PsSharedDto { public int V { get; set; } }
public struct PsHolderDto { public PsSharedDto Ref { get; set; } }
public sealed class PsRootDto { public PsHolderDto H1 { get; set; } public PsHolderDto H2 { get; set; } }

public sealed class RsShared { public int V { get; set; } }
public readonly record struct RsHolder(RsShared Ref);
public sealed class RsRoot { public RsHolder H1 { get; set; } public RsHolder H2 { get; set; } }

public sealed class RsSharedDto { public int V { get; set; } }
public readonly record struct RsHolderDto(RsSharedDto Ref);
public sealed class RsRootDto { public RsHolderDto H1 { get; set; } public RsHolderDto H2 { get; set; } }

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class StructPreserveMapper
{
    public partial PsRootDto Map(PsRoot r);
    public partial RsRootDto MapRecordStruct(RsRoot r);
}

public class PreserveThroughStructRuntimeTests
{
    [Fact]
    public void Shared_reference_through_a_struct_node_is_deduplicated()
    {
        var shared = new PsShared { V = 42 };
        var root = new PsRoot { H1 = new PsHolder { Ref = shared }, H2 = new PsHolder { Ref = shared } };

        var dto = new StructPreserveMapper().Map(root);

        Assert.Equal(42, dto.H1.Ref.V);
        Assert.Equal(42, dto.H2.Ref.V);
        // The struct is copied, but the reference it carries keeps its identity: both holders map to one referent.
        Assert.Same(dto.H1.Ref, dto.H2.Ref);
    }

    [Fact]
    public void Shared_reference_through_a_record_struct_node_is_deduplicated()
    {
        var shared = new RsShared { V = 7 };
        var root = new RsRoot { H1 = new RsHolder(shared), H2 = new RsHolder(shared) };

        var dto = new StructPreserveMapper().MapRecordStruct(root);

        Assert.Equal(7, dto.H1.Ref.V);
        Assert.Same(dto.H1.Ref, dto.H2.Ref);
    }
}

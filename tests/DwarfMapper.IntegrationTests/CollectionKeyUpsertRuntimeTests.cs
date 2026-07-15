// SPDX-License-Identifier: GPL-2.0-only
#nullable enable
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// [MapCollectionKey] — key-based upsert for update-into. Instead of replacing the whole collection (which
// discards the existing list's identity and any elements the update didn't mention), the source list is merged
// into the existing one by key: matched keys update the slot, new keys are added, unmatched existing elements
// are kept.

public sealed class CkItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class CkEntity
{
    public List<CkItem> Items { get; set; } = new();
}

public sealed class CkUpdate
{
    public List<CkItem> Items { get; set; } = new();
}

[DwarfMapper]
public partial class CollectionKeyMapper
{
    [MapCollectionKey(nameof(CkEntity.Items), nameof(CkItem.Id))]
    public partial void Merge(CkUpdate src, CkEntity dst);
}

public class CollectionKeyUpsertRuntimeTests
{
    [Fact]
    public void Matched_keys_update_new_keys_add_unmatched_are_kept()
    {
        var existing = new CkEntity
        {
            Items =
            {
                new CkItem { Id = 1, Name = "one" },
                new CkItem { Id = 2, Name = "two" },
            },
        };
        var keep1 = existing.Items[0]; // the Id=1 element the update never mentions
        var list = existing.Items;      // the list instance itself

        var update = new CkUpdate
        {
            Items =
            {
                new CkItem { Id = 2, Name = "TWO-updated" }, // matches Id=2 → updates that slot
                new CkItem { Id = 3, Name = "three" },       // new key → appended
            },
        };

        new CollectionKeyMapper().Merge(update, existing);

        // Same list instance (identity preserved), not a fresh replacement.
        Assert.Same(list, existing.Items);
        // Id=1 element untouched (kept), including its original reference.
        Assert.Same(keep1, existing.Items.Single(i => i.Id == 1));
        // Id=1 kept its original value; Id=2 updated in place; Id=3 added.
        Assert.Equal("one", existing.Items.Single(i => i.Id == 1).Name);
        Assert.Equal("TWO-updated", existing.Items.Single(i => i.Id == 2).Name);
        Assert.Equal("three", existing.Items.Single(i => i.Id == 3).Name);
        Assert.Equal(3, existing.Items.Count);
    }

    [Fact]
    public void A_null_source_collection_leaves_the_destination_unchanged()
    {
        var existing = new CkEntity { Items = { new CkItem { Id = 1, Name = "one" } } };

        new CollectionKeyMapper().Merge(new CkUpdate { Items = null! }, existing);

        Assert.Single(existing.Items);
        Assert.Equal("one", existing.Items[0].Name);
    }

    [Fact]
    public void Merging_into_an_empty_existing_list_adds_all()
    {
        var existing = new CkEntity();
        var update = new CkUpdate { Items = { new CkItem { Id = 5, Name = "five" }, new CkItem { Id = 6 } } };

        new CollectionKeyMapper().Merge(update, existing);

        Assert.Equal(new[] { 5, 6 }, existing.Items.Select(i => i.Id).ToArray());
    }
}

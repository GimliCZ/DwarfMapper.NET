// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     On an <b>update-into</b> mapping method (<c>void Map(TSource src, TTarget dest)</c>), merges a
///     <c>List&lt;T&gt;</c> collection member <b>by key</b> instead of replacing it wholesale. The existing list
///     instance is kept and mutated in place: an element whose key matches an existing one <b>updates that
///     slot</b>, an element with a new key is <b>added</b>, and existing elements whose key is absent from the
///     source are <b>left untouched</b>.
///     <para>
///         Without this, update-into replaces the whole collection with a freshly-built one (see
///         <c>DWARF065</c>) — discarding the existing list's identity and any elements the update did not mention.
///         Key-based upsert is the merge semantics an update usually wants.
///     </para>
///     <para>
///         v1 scope: the collection member must be a <c>List&lt;T&gt;</c> whose element type is the same on
///         source and target (the common Entity↔Entity update), and the key must be a readable member of that
///         element type. Anything else reports <c>DWARF074</c>. The key value is compared with the type's
///         default equality (<c>EqualityComparer&lt;TKey&gt;.Default</c>).
///     </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MapCollectionKeyAttribute : Attribute
{
    /// <summary>Creates a key-based upsert rule for one collection member.</summary>
    /// <param name="collectionMember">The name of the <c>List&lt;T&gt;</c> member to merge by key.</param>
    /// <param name="keyMember">The name of the element-type member used as the match key.</param>
    public MapCollectionKeyAttribute(string collectionMember, string keyMember)
    {
        CollectionMember = collectionMember;
        KeyMember = keyMember;
    }

    /// <summary>The destination collection member to merge by key.</summary>
    public string CollectionMember { get; }

    /// <summary>The element-type member used as the match key.</summary>
    public string KeyMember { get; }
}

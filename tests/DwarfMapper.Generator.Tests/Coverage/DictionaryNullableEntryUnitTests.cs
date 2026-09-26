// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;

// DictionaryConverter.SourceKeyIsNullableRef / SourceValueIsNullableRef read the KEY and VALUE types off the
// IEnumerable<KeyValuePair<K, V>> a dictionary source implements. The converter only ever asks about a source it has
// already admitted as a dictionary, so the answers for a type that is NOT a pair sequence were never produced by a
// mapper — they are asked here directly (per-branch rule: extract and test what no input reaches).
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class DictionaryNullableEntryUnitTests
    {
        private const string Source = """
                                      #nullable enable
                                      using System.Collections.Generic;
                                      namespace T
                                      {
                                          public class Node { }
                                          public class KeyValuePair<TOnly> { }
                                          public class Holder
                                          {
                                              public Dictionary<Node?, Node?> Nullable = new();
                                              public Dictionary<Node, int> Plain = new();
                                              public List<int> Numbers = new();
                                              public List<int[]> Arrays = new();
                                              public List<KeyValuePair<int>> LookAlikes = new();
                                          }
                                      }
                                      """;

        private static ITypeSymbol Field(string name)
        {
            var compilation = GeneratorTestHarness.BuildCompilation("DictionaryNullableEntry", Source, NullableContextOptions.Enable);
            return compilation.GetTypeByMetadataName("T.Holder")!.GetMembers(name).OfType<IFieldSymbol>().Single().Type;
        }

        [Fact]
        public void A_dictionary_reports_its_annotated_key_and_value()
        {
            Assert.True(DictionaryConverter.SourceKeyIsNullableRef(Field("Nullable")));
            Assert.True(DictionaryConverter.SourceValueIsNullableRef(Field("Nullable")));
            Assert.False(DictionaryConverter.SourceKeyIsNullableRef(Field("Plain")));
            Assert.False(DictionaryConverter.SourceValueIsNullableRef(Field("Plain")));
        }

        [Theory]
        [InlineData("Numbers")]
        [InlineData("Arrays")]
        [InlineData("LookAlikes")]
        public void A_sequence_that_is_not_of_key_value_pairs_reports_neither(string field)
        {
            Assert.False(DictionaryConverter.SourceKeyIsNullableRef(Field(field)));
            Assert.False(DictionaryConverter.SourceValueIsNullableRef(Field(field)));
        }
    }
}

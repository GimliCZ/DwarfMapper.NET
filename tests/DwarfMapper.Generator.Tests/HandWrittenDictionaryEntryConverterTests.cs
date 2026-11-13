// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>A hand-written converter as a dictionary key or value</b> (round 35, T12 follow-up).
    ///     <para>
    ///         T12 widened which converters make a dictionary helper a candidate for re-synthesis: a declared
    ///         mapper method in every mode, anything else that is not synthesized only without reference handling.
    ///         "Anything else" is a method the user wrote in full. The dictionary site had no test with one - the
    ///         collection site had - which Codecov showed as two partial branches on PR #13.
    ///     </para>
    /// </summary>
    public class HandWrittenDictionaryEntryConverterTests
    {
        private static string Source(string mode)
        {
            return $$"""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class Tag { public string Text { get; set; } = ""; }
                     public class TagDto { public string Text { get; set; } = ""; }
                     public class Key { public int Id { get; set; } }
                     public class KeyDto { public int Id { get; set; } }
                     public class H    { public Dictionary<Key, Tag> ByKey { get; set; } = new(); }
                     public class HDto { public Dictionary<KeyDto, TagDto> ByKey { get; set; } = new(); }
                     {{mode}}
                     public partial class M
                     {
                         private TagDto ConvertTag(Tag t) => new TagDto { Text = t.Text };
                         private KeyDto ConvertKey(Key k) => new KeyDto { Id = k.Id };
                         public partial HDto Map(H h);
                     }
                     """;
        }

        [Theory]
        [InlineData(DispatchPairUnderReferenceHandlingTests.None)]
        [InlineData(DispatchPairUnderReferenceHandlingTests.Preserve)]
        [InlineData(DispatchPairUnderReferenceHandlingTests.SetNull)]
        public void A_hand_written_key_and_value_converter_is_called_as_written_in_every_mode(string mode)
        {
            var source = Source(mode);
            var (diagnostics, generated) = GeneratorTestHarness.Run(source);

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(source);
            Assert.Contains("ConvertKey(__kv.Key)", generated, StringComparison.Ordinal);
            Assert.Contains("ConvertTag(__kv.Value)", generated, StringComparison.Ordinal);
            // A hand-written method takes no context and has no companion to take one for it.
            Assert.DoesNotContain("__DwarfMap_Depth_Convert", generated, StringComparison.Ordinal);
        }
    }
}

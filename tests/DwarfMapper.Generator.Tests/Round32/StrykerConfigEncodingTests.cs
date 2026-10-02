// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DwarfMapper.Generator.Tests.Contracts;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round32
{
    /// <summary>
    ///     Every mutation config is plain UTF-8 JSON with no byte-order mark, because CI reads it with Python.
    ///     <para>
    ///         The <c>mutation</c> job checks each leg's thresholds with <c>json.load(open(path))</c>, and its
    ///         dashboard step reads <c>project-info</c> the same way. Python refuses a leading BOM
    ///         (<c>Unexpected UTF-8 BOM</c>), so a BOM kills the leg before it runs. Nothing local notices:
    ///         <c>housekeeping.ps1</c>'s <c>ConvertFrom-Json</c> and the .NET parser in
    ///         <c>RatchetInvariantScanTests</c> both accept it. That is how <c>stryker-config.runtime.json</c>
    ///         carried one from <c>ed69922</c> (round 31 T12), while every nightly's runtime leg died in its
    ///         config check (round 32 finding, note A).
    ///     </para>
    /// </summary>
    public sealed class StrykerConfigEncodingTests
    {
        private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

        [Fact]
        public void Every_mutation_config_is_json_without_a_byte_order_mark()
        {
            var configs = Directory.EnumerateFiles(RepoPaths.Root, "stryker-config*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            // Non-vacuity: a scan that found no configs would pass by looking at nothing.
            Assert.True(configs.Count >= 6, $"found only {configs.Count} stryker-config*.json at the repository root; there are six legs.");

            var offenders = new List<string>();
            foreach (var path in configs)
            {
                var bytes = File.ReadAllBytes(path);
                var name = Path.GetFileName(path);
                if (bytes.AsSpan().StartsWith(Utf8Bom))
                {
                    offenders.Add(name + ": starts with a UTF-8 byte-order mark");
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(bytes);
                    Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
                }
                catch (JsonException e)
                {
                    offenders.Add(name + ": not valid JSON (" + e.Message + ")");
                }
            }

            Assert.True(offenders.Count == 0,
                "CI's mutation job reads these files with Python's json.load, which refuses a byte-order mark, so the " +
                "leg dies in its config check before it runs. Save them as UTF-8 without a BOM:\n  " +
                string.Join("\n  ", offenders));
        }
    }
}

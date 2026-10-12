// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Every <c>stryker-config*.json</c> must be readable by CI's own reader: the mutation job's
    ///     "Assert the config is runnable" step and its dashboard step load each config with Python's
    ///     <c>json.load(open(path))</c>, which refuses a UTF-8 byte-order mark ("Unexpected UTF-8 BOM").
    /// </summary>
    /// <remarks>
    ///     Found 2026-10-11: round 31 T12 (<c>ed69922</c>) rewrote <c>stryker-config.runtime.json</c> with a BOM, and
    ///     from the merge on, every nightly failed the runtime leg at that assert - BEFORE Stryker ran - so the leg
    ///     the round most needed measured was never measured at all. Locally nothing noticed: .NET and PowerShell
    ///     both read a BOM silently. This test is the reader CI uses, run where the build runs.
    /// </remarks>
    public sealed class StrykerConfigEncodingTests
    {
        public static TheoryData<string> Configs()
        {
            var data = new TheoryData<string>();
            foreach (var path in Directory.GetFiles(RepoPaths.Root, "stryker-config*.json").OrderBy(p => p, StringComparer.Ordinal))
                data.Add(Path.GetFileName(path));
            return data;
        }

        [Theory]
        [MemberData(nameof(Configs))]
        public void A_stryker_config_has_no_byte_order_mark_so_CI_can_read_it(string config)
        {
            var bytes = File.ReadAllBytes(Path.Combine(RepoPaths.Root, config));

            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                config + " starts with a UTF-8 byte-order mark. CI reads it with Python's json.load(open(...)), which " +
                "refuses that, so the mutation leg fails before Stryker starts. Save the file as UTF-8 without BOM.");
        }

        [Fact]
        public void Every_leg_config_is_found()
        {
            // Guards the theory above against passing vacuously on a moved or renamed config set.
            Assert.Equal(6, Directory.GetFiles(RepoPaths.Root, "stryker-config*.json").Length);
        }
    }
}

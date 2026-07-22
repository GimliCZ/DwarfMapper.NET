// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>Everything one generator run produced: its diagnostics and EVERY file it emitted.</summary>
internal sealed record GeneratorRun(
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableDictionary<string, string> OutputsByHintName)
{
    /// <summary>All outputs concatenated in hint-name order — the stable form used for fingerprinting.</summary>
    public string AllOutputsConcatenated =>
        string.Concat(OutputsByHintName.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => "// ==== " + kv.Key + " ====\n" + kv.Value));
}

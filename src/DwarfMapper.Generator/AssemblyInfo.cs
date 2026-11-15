// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.CompilerServices;

// Exposes internal types (e.g. CollectionConverter.TargetKind) to the test project
// so self-validation scans can reflect them without modifying accessibility modifiers.
[assembly: InternalsVisibleTo("DwarfMapper.Generator.Tests")]

// Round 35 T23: the tests of the Roslyn 5.12 build. This file is compiled into both builds; the 5.12 build is the one
// that project references.
[assembly: InternalsVisibleTo("DwarfMapper.Generator.Roslyn512.Tests")]

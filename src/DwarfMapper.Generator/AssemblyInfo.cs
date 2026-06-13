// SPDX-License-Identifier: GPL-2.0-only
using System.Runtime.CompilerServices;

// Exposes internal types (e.g. CollectionConverter.TargetKind) to the test project
// so self-validation scans can reflect them without modifying accessibility modifiers.
[assembly: InternalsVisibleTo("DwarfMapper.Generator.Tests")]

// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.CompilerServices;

// Exposes internal types (e.g. DwarfSurfaceAttribute) to the test project so self-validation
// scans can reflect them without modifying accessibility modifiers or shipping them publicly.
[assembly: InternalsVisibleTo("DwarfMapper.Generator.Tests")]

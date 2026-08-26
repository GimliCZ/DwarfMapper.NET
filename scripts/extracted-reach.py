# SPDX-License-Identifier: GPL-2.0-only
#
# Does the golden corpus actually EXECUTE the methods the seam stage created?
#
# seam-reach.py answers this for ExtractCore's phases and ONLY for those: it is hardcoded to that method and
# that file. Byte-identity over the 973 cases proves nothing about a method no case reaches, so borrowing
# that proof for later cuts would be exactly the vacuous green this repository keeps finding in its own
# instruments. This measures the cuts themselves.
#
# Driven by scripts/extracted-reach.ps1. Self-maintaining: it reads every `private static` method out of the
# files the stage created, so a method added to one of them is measured without editing this list.

import io
import glob
import re
import sys
import xml.etree.ElementTree as ET

out = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

FILES = [
    # ExtractCore's own phases, moved out earlier in round 27. Added after a battery run caught the gap they
    # fell through: seam-reach measures phases by the seam COMMENTS still inside ExtractCore, so the moment a
    # phase was extracted it left that file and stopped being counted there -- and it was never in this list.
    # Ten phases were being measured by neither script, which is precisely the silence this instrument exists
    # to break.
    'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Phases.cs',
    'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs',
    'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Conversions.Arms.cs',
    'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Flatten.Directive.cs',
    'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Flatten.Hetero.cs',
    'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Hetero.Arms.cs',
    'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Flatten.Members.cs',
]

# Anchored on the MODIFIERS, with the name taken as the last identifier before the first '(' -- NOT on a
# pattern ending in '($'. That earlier form required the signature to WRAP, so it silently skipped every
# method whose parameters fit on one line: eleven of MapperExtractor.Phases.cs's eighteen, including all ten
# ExtractCore phases extracted earlier in round 27. A reach instrument that quietly measures a subset is the
# exact failure it exists to detect.
DECL_START = re.compile(r'^\s*(?:private|internal|public) static ')


def declared_name(line):
    """The method name on a declaration line, or None when the line is not one."""
    head = line.split('(')[0]
    if '=' in head or ' readonly ' in head:
        return None                                 # a field initialised with new(...), not a method
    names = re.findall(r'\w+', head)
    return names[-1] if names else None


def methods(path):
    lines = io.open(path, encoding='utf-8').read().split('\n')
    for i, line in enumerate(lines):
        if not DECL_START.match(line) or '(' not in line:
            continue
        name = declared_name(line)
        if name is None:
            continue
        depth, seen = 0, False
        for j in range(i, len(lines)):
            depth += lines[j].count('{') - lines[j].count('}')
            if '{' in lines[j]:
                seen = True
            if seen and depth <= 0:
                yield name, i + 1, j + 1
                break


reports = [sys.argv[1]] if len(sys.argv) > 1 else glob.glob(
    'TestResults/extractedcov/**/coverage.cobertura.xml', recursive=True)

covered = {}
for xml in reports:
    for cls in ET.parse(xml).getroot().iter('class'):
        fn = (cls.get('filename') or '').replace(chr(92), '/')
        for f in FILES:
            if fn.endswith(f.split('Pipeline/')[1]):
                hits = covered.setdefault(f, set())
                for line in cls.iter('line'):
                    if int(line.get('hits') or 0) > 0:
                        hits.add(int(line.get('number')))

missing_files = [f for f in FILES if f not in covered]
if missing_files:
    print('These files are absent from the coverage report, so the measurement read NOTHING for them:', file=out)
    for f in missing_files:
        print('   %s' % f, file=out)
    out.flush()
    raise SystemExit(1)

print('%-38s %-7s %-9s %s' % ('method', 'lines', 'covered', 'file'), file=out)
print('-' * 108, file=out)
unreached, total = [], 0
for f in FILES:
    hits = covered[f]
    for name, lo, hi in methods(f):
        total += 1
        n = len([l for l in hits if lo <= l <= hi])
        if not n:
            unreached.append((name, f))
        print('%-38s %-7d %-9d %s%s' % (name, hi - lo + 1, n, f.split('/')[-1],
                                        '   <== NOT REACHED' if not n else ''), file=out)

print('', file=out)
print('METHODS REACHED BY THE GOLDEN CORPUS: %d / %d' % (total - len(unreached), total), file=out)
if unreached:
    print('', file=out)
    print('UNREACHED -- byte-identity over the corpus locks NOTHING for these:', file=out)
    for name, f in unreached:
        print('   %s  (%s)' % (name, f.split('/')[-1]), file=out)
    out.flush()
    raise SystemExit(1)
out.flush()

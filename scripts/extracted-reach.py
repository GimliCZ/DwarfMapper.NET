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
    'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs',
    'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Conversions.Arms.cs',
    'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Flatten.Directive.cs',
    'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Flatten.Hetero.cs',
    'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Hetero.Arms.cs',
    'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Flatten.Members.cs',
]

DECL = re.compile(r'^\s*private static (?:void|bool|[\w\.\?<>,\[\] ]+?)\s*(\w+)\($')


def methods(path):
    lines = io.open(path, encoding='utf-8').read().split('\n')
    for i, line in enumerate(lines):
        m = DECL.match(line)
        if not m:
            continue
        depth, seen = 0, False
        for j in range(i, len(lines)):
            depth += lines[j].count('{') - lines[j].count('}')
            if '{' in lines[j]:
                seen = True
            if seen and depth <= 0:
                yield m.group(1), i + 1, j + 1
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

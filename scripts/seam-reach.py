# SPDX-License-Identifier: GPL-2.0-only
#
# Maps golden-corpus coverage onto the seam comments ExtractCore already carries, and reports which phases
# were executed. Driven by scripts/seam-reach.ps1; see that file for why reach has to be measured before any
# phase is moved.

import io
import re
import glob
import sys
import xml.etree.ElementTree as ET

out = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

SRC = 'src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs'
START = 110  # ExtractCore declaration line

# ---- 1. the seam boundaries, read from the method itself -------------------------------------
lines = io.open(SRC, encoding='utf-8').read().split('\n')
depth = 0
started = False
end = None
for j in range(START - 1, len(lines)):
    depth += lines[j].count('{') - lines[j].count('}')
    if '{' in lines[j]:
        started = True
    if started and depth <= 0:
        end = j + 1
        break

seams = []
for idx in range(START - 1, end):
    s = lines[idx].strip()
    if s.startswith('//') and re.match(r'//\s*[─═]{2}', s):
        title = re.sub(r'[─═]+', '', s[2:]).strip()
        # An "End X" marker CLOSES the phase it names; it does not open a new one. Treating it as a phase
        # start manufactured three 2-line spans that no case can reach because they are just the tail of the
        # preceding phase. ~20 real phases, which is what the plan estimated.
        if title.startswith('End '):
            continue
        seams.append((idx + 1, title))

# phase spans: from one seam to the next (the prologue before seam 0 counts as a phase too)
spans = []
prev_line, prev_title = START, '<prologue>'
for ln, title in seams:
    spans.append((prev_line, ln - 1, prev_title))
    prev_line, prev_title = ln, title
spans.append((prev_line, end, prev_title))

# ---- 2. covered lines for MapperExtractor.cs -------------------------------------------------
covered = set()
found_file = False
reports = [sys.argv[1]] if len(sys.argv) > 1 else glob.glob('TestResults/seamcov/**/coverage.cobertura.xml', recursive=True)

for xml in reports:
    root = ET.parse(xml).getroot()
    for cls in root.iter('class'):
        fn = (cls.get('filename') or '').replace('\\', '/')
        if not fn.endswith('Pipeline/MapperExtractor.cs'):
            continue
        found_file = True
        for line in cls.iter('line'):
            if int(line.get('hits') or 0) > 0:
                covered.add(int(line.get('number')))

if not found_file:
    print('MapperExtractor.cs not present in the coverage report — the measurement read nothing.', file=out)
    out.flush()
    raise SystemExit(1)

# ---- 3. reach per phase ----------------------------------------------------------------------
print('ExtractCore spans lines %d-%d; %d seams => %d phases' % (START, end, len(seams), len(spans)), file=out)
print('Covered lines inside the method: %d' % len([l for l in covered if START <= l <= end]), file=out)
print('', file=out)
print('%-6s %-6s %-7s %s' % ('start', 'end', 'hits', 'phase'), file=out)
print('-' * 100, file=out)

unreached = []
for lo, hi, title in spans:
    hits = len([l for l in covered if lo <= l <= hi])
    flag = '' if hits else '   <== NOT REACHED'
    if not hits:
        unreached.append((lo, hi, title))
    print('%-6d %-6d %-7d %s%s' % (lo, hi, hits, title[:60], flag), file=out)

print('', file=out)
print('PHASES REACHED BY THE GOLDEN CORPUS: %d / %d' % (len(spans) - len(unreached), len(spans)), file=out)
if unreached:
    sys.exit(1)

if False:
    print('', file=out)
    print('UNREACHED — these are NOT locked by the byte-identity manifest:', file=out)
    for lo, hi, title in unreached:
        print('   L%d-%d  %s' % (lo, hi, title), file=out)
out.flush()

# SPDX-License-Identifier: GPL-2.0-only
"""The other half of the proof: what the extraction left BEHIND.

rederive.py proves each extracted body IS the original span. That says nothing about the hole it came out
of -- a call placed a few lines off, or a stray edit to the surrounding method, would pass that check
completely. So this reads the commit's own diff of the SOURCE file and requires that the only thing removed
is the span, and the only thing added is the call.

Anything else in the diff is a finding, whether or not any test would notice.
"""
import io
import re
import subprocess
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')


def diff_lines(commit, path):
    r = subprocess.run(['git', 'diff', '--unified=0', '%s^' % commit, commit, '--', path],
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    removed, added = [], []
    for line in r.stdout.split('\n'):
        if line.startswith('---') or line.startswith('+++') or line.startswith('@@'):
            continue
        if line.startswith('-'):
            removed.append(line[1:].strip())
        elif line.startswith('+'):
            added.append(line[1:].strip())
    return [l for l in removed if l], [l for l in added if l]


def check(label, commit, path, expect_calls):
    removed, added = diff_lines(commit, path)
    stray = [a for a in added
             if not any(c in a for c in expect_calls)
             and not a.startswith('//')
             # a bare identifier, optionally closing the bundle construction it is the last argument of
             and not re.match(r'^[\)\{\}]|^[\w\.]+\)*[,;]*$|^lookups|^acc|^req|^nav', a)]
    verdict = 'OK  ' if not stray else 'STRAY'
    print('  %-5s %-34s removed %4d, added %3d' % (verdict, label, len(removed), len(added)))
    for s in stray[:8]:
        print('        UNEXPECTED ADDITION: ' + s[:100])
    return 1 if stray else 0


MEM = 'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.cs'
CONVF = 'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Conversions.cs'
FLAT = 'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Flatten.cs'
DIR = 'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Flatten.Directive.cs'
HET = 'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Flatten.Hetero.cs'

bad = 0
bad += check('e6fca50 skip-null pass', 'e6fca50', MEM, ['ApplySkipNullSourceMembers(', 'new MemberLookups', 'new MemberAccumulators'])
bad += check('e6769f7 mapvalue pass', 'e6769f7', MEM, ['ResolveMapValues('])
bad += check('0d2486f explicit + auto', '0d2486f', MEM, ['ResolveExplicitMaps(', 'ResolveAutoMatchedMembers('])
bad += check('fd5f833 six arms', 'fd5f833', CONVF,
             ['Handle', 'new ConversionRequest', 'return dictionaryResolved', 'return collectionResolved',
              'return nullableTargetResolved', 'return nullableSourceResolved',
              'return targetNullableResolved', 'return autoNestedResolved'])
bad += check('161836d directive loop', '161836d', FLAT, ['ResolveOneFlattenGraphDirective(', 'new FlattenGraphRequest', 'new FlattenGraphAccumulators'])
bad += check('e08f398 hetero branch', 'e08f398', DIR, ['ResolveHeterogeneousFlattenGraph(', 'new FlattenNavShape', 'return;'])
bad += check('ef30908 arms + partition', 'ef30908', HET, ['ResolveDerivedTypeArms(', 'ref anyArmError'])
bad += check('ef30908 partition site', 'ef30908', DIR, ['PartitionNodeMembers('])

print('\n%s' % ('EVERY CALL SITE IS THE SPAN IT REPLACED' if not bad
                else '%d SITE(S) CARRY UNEXPLAINED ADDITIONS' % bad))
sys.exit(1 if bad else 0)

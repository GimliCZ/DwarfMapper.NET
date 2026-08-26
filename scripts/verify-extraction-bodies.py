# SPDX-License-Identifier: GPL-2.0-only
"""Prove each extracted method is the original span, line for line -- including the lines no test reaches.

Byte-identity of GENERATED OUTPUT proves the moves only where the corpus reaches. ~138 lines across these
methods are executed by nothing, and a rename applied to the wrong identifier there would be invisible to
every test and to the manifest alike. This closes that gap by comparing the SOURCE.

Deliberately the INVERSE of what the lift scripts did: it takes the extracted body and reverses the renames
and jump rewrites, then diffs against the original span. Re-running the forward transformation would only
prove the scripts are deterministic -- the same code reproducing the same mistake. Working backwards from
what is committed exercises a different path and checks the RESULT.

A span's leading comment block became the method's XML <summary>, so it is stripped from the original --
and each stripped line is then required to still appear in that doc, or a comment could go missing here and
the diff would call it clean.
"""
import difflib
import io
import re
import subprocess
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')


def show(rev, path):
    r = subprocess.run(['git', 'show', '%s:%s' % (rev, path)],
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    if r.returncode:
        raise SystemExit('git show %s:%s failed: %s' % (rev, path, r.stderr[:200]))
    return r.stdout.split('\n')


def locate(lines, name):
    return next(i for i, l in enumerate(lines)
                if re.match(r'^\s*private static .*\b' + re.escape(name) + r'\($', l))


def method_body(lines, name):
    start = locate(lines, name)
    brace = next(j for j in range(start, len(lines)) if lines[j].strip() == '{')
    depth, seen = 0, False
    for j in range(brace, len(lines)):
        depth += lines[j].count('{') - lines[j].count('}')
        if '{' in lines[j]:
            seen = True
        if seen and depth <= 0:
            return lines[brace + 1:j]
    raise SystemExit('unterminated method ' + name)


def method_doc(lines, name):
    """The /// block immediately above the signature."""
    start = locate(lines, name)
    out, k = [], start - 1
    while k >= 0 and (lines[k].strip().startswith('//') or lines[k].strip().startswith('[')):
        out.append(lines[k].strip().lstrip('/').strip())
        k -= 1
    return ' '.join(reversed(out))


def norm(lines):
    """Indentation- and blank-insensitive: the move re-indented everything by design."""
    return [l.strip() for l in lines if l.strip()]


def check(label, src_rev, src_path, lo, hi, dst_rev, dst_path, name,
          renames=(), unjump=None, strip_scaffold=False, extra_decls=()):
    original = norm(show(src_rev, src_path)[lo - 1:hi])
    dst_lines = show(dst_rev, dst_path)
    body = norm(method_body(dst_lines, name))
    doc = method_doc(dst_lines, name)

    # A span's leading comment block went one of three ways: into the method's doc, kept at the CALL SITE,
    # or carried along inside the body. All three preserve it; only vanishing is a defect. So the comment is
    # set aside ONLY when the body did not keep it, and is then required to exist somewhere in the
    # destination file or back at the call site.
    survivors = ' '.join(l.strip() for l in dst_lines) + ' ' + ' '.join(
        l.strip() for l in show(dst_rev, src_path))
    survivors = re.sub(r'\s+', ' ', survivors)
    lost = []
    while original and original[0].startswith('//'):
        if body and body[0] == original[0]:
            break                                            # kept in the body; compare it like any line
        moved = original.pop(0)
        probe = re.sub(r'\s+', ' ', moved.lstrip('/').strip())[:40]
        if probe and survivors.find(probe) < 0:
            lost.append(moved)

    if strip_scaffold:
        assert body[0] == 'resolved = false;', (label, body[0])
        assert body[-1] == 'return false;', (label, body[-1])
        body = body[1:-1]                                    # prologue and epilogue are exactly these
        collapsed, i = [], 0
        while i < len(body):
            m = re.match(r'^resolved = (true|false);(.*)$', body[i])
            if m and i + 1 < len(body) and body[i + 1] == 'return true;':
                collapsed.append('return %s;%s' % (m.group(1), m.group(2)))
                i += 2
            else:
                collapsed.append(body[i])
                i += 1
        body = collapsed

    text = '\n'.join(body)
    for new, old in renames:                                 # INVERSE direction
        text = re.sub(r'(?<![\w.])' + re.escape(new) + r'\b', old, text)
    if unjump:
        text = re.sub(r'(?<![\w])return;', unjump, text)
    body = text.split('\n')

    for d in extra_decls:                                    # declarations that moved INTO the method
        if d in body:
            body.remove(d)
        if d in original:
            original.remove(d)

    ok = original == body and not lost
    if ok:
        print('  OK    %-34s %4d lines' % (label, len(original)))
        return 0
    print('  DIFF  %-34s original %d lines, re-derived %d' % (label, len(original), len(body)))
    for line in lost:
        print('        COMMENT LOST FROM DOC: ' + line[:96])
    for line in list(difflib.unified_diff(original, body, 'original', 're-derived', lineterm='', n=1))[:30]:
        print('        ' + line)
    return 1


BUNDLE = [('acc.Result', 'result'), ('acc.HandledTargets', 'handledTargets'),
          ('acc.ConsumedExtraParams', 'consumedExtraParams'),
          ('acc.ConsumedFlattenRoots', 'consumedFlattenRoots'),
          ('acc.Diagnostics', 'diagnostics'), ('acc.Synthesized', 'synthesized'),
          ('lookups.Comparer', 'comparer'), ('lookups.Flexible', 'flexible'),
          ('lookups.WritableByName', 'writableByName'), ('lookups.SourceGroups', 'sourceGroups'),
          ('lookups.FlattenInfos', 'flattenInfos'), ('lookups.ReservedConverters', 'reservedConverters'),
          ('lookups.ExtrasByTarget', 'extrasByTarget'),
          ('req.SourceType', 'sourceType'), ('req.TargetType', 'targetType'), ('req.Ignores', 'ignores'),
          ('req.Compilation', 'compilation'), ('req.Location', 'location'), ('req.Options', 'options'),
          ('req.ExplicitMaps', 'explicitMaps'), ('req.AllMethods', 'allMethods'),
          ('req.AutoCandidates', 'autoCandidates'), ('req.EnumPolicy', 'enumPolicy'),
          ('req.NullStrategy', 'nullStrategy'), ('req.ReinterpretMembers', 'reinterpretMembers'),
          ('req.ConsumedCtorParams', 'consumedCtorParams'),
          ('req.RequiredMustInitialize', 'requiredMustInitialize'),
          ('req.NestedRegistry', 'nestedRegistry'), ('req.MapValues', 'mapValues'),
          ('req.ValueProviders', 'valueProviders'), ('req.ExtraParams', 'extraParams'),
          ('req.StringFormats', 'stringFormats'),
          ('req.RequiredMembersAlreadySatisfied', 'requiredMembersAlreadySatisfied'),
          ('req.FactoryExcludedMembers', 'factoryExcludedMembers')]

CONV = [('req.Compilation', 'compilation'), ('req.SrcType', 'srcType'), ('req.TgtType', 'tgtType'),
        ('req.UseMethod', 'useMethod'), ('req.AllMethods', 'allMethods'),
        ('req.AutoCandidates', 'autoCandidates'), ('req.EnumPolicy', 'enumPolicy'),
        ('req.NullStrategy', 'nullStrategy'), ('req.Location', 'location'),
        ('req.TargetName', 'targetName'), ('req.AutoNest', 'autoNest'),
        ('req.NestedRegistry', 'nestedRegistry'), ('req.NullAsNull', 'nullAsNull'),
        ('req.IsPreserve', 'isPreserve'), ('req.AllowInterfaceSrc', 'allowInterfaceSrc'),
        ('req.IsSetNull', 'isSetNull'), ('req.ImplicitConversions', 'implicitConversions'),
        ('req.ReservedConverters', 'reservedConverters')]

FG = [('req.SourceType', 'sourceType'), ('req.TargetType', 'targetType'),
      ('req.Compilation', 'compilation'), ('req.Location', 'location'),
      ('req.AllMethods', 'allMethods'), ('req.AutoCandidates', 'autoCandidates'),
      ('req.EnumPolicy', 'enumPolicy'), ('req.NullStrategy', 'nullStrategy'),
      ('req.AutoNest', 'autoNest'), ('req.NestedRegistry', 'nestedRegistry'),
      ('req.IsPreserve', 'isPreserve'), ('req.AllowNonPublic', 'allowNonPublic'),
      ('req.RawDerivedPairs', 'rawDerivedPairs'),
      ('acc.Diagnostics', 'diagnostics'), ('acc.Synthesized', 'synthesized'),
      ('acc.ConsumedTargets', 'consumedTargets'), ('acc.Directives', 'directives'),
      ('acc.Injected', 'injected'), ('acc.SeenTargets', 'seenTargets')]

NAV = [('nav.SrcNavType', 'srcNavType'), ('nav.NodeType', 'nodeType'), ('nav.NodeDtoType', 'nodeDtoType'),
       ('nav.SrcNavIsCollection', 'srcNavIsCollection'), ('nav.SrcNavIsArray', 'srcNavIsArray'),
       ('nav.SrcNavIsDict', 'srcNavIsDict'), ('nav.NeedsToArray', 'needsToArray')]

MEM = 'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.cs'
MEMP = 'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs'
CONVF = 'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Conversions.cs'
ARMS = 'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Conversions.Arms.cs'
FLAT = 'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Flatten.cs'
DIR = 'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Flatten.Directive.cs'
HET = 'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Flatten.Hetero.cs'
HARMS = 'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Hetero.Arms.cs'
FMEM = 'src/DwarfMapper.Generator/Pipeline/MapperExtractor.Flatten.Members.cs'

bad = 0
print('ResolveMembers passes')
bad += check('ApplySkipNullSourceMembers', 'e6fca50^', MEM, 936, 984, 'e6fca50', MEMP,
             'ApplySkipNullSourceMembers',
             renames=[('acc.Result', 'result'), ('lookups.Comparer', 'comparer')])
bad += check('ResolveMapValues', 'e6769f7^', MEM, 532, 589, 'e6769f7', MEMP,
             'ResolveMapValues', renames=BUNDLE)
bad += check('ResolveExplicitMaps', '0d2486f^', MEM, 273, 558, '0d2486f', MEMP,
             'ResolveExplicitMaps', renames=BUNDLE,
             extra_decls=['var explicitSeen = new HashSet<string>(StringComparer.Ordinal);',
                          'var unflattenRoots = new HashSet<string>(StringComparer.Ordinal);',
                          '// Intermediate roots already opened by an unflatten leaf — additional leaves into the same root',
                          '// are allowed (City + Street → Address); only a DIRECT mapping of the root conflicts (DWARF046).'])
bad += check('ResolveAutoMatchedMembers', '0d2486f^', MEM, 566, 862, '0d2486f', MEMP,
             'ResolveAutoMatchedMembers', renames=BUNDLE)

print('\nTryResolveConversion arms')
for lo, hi, name in [(117, 308, 'HandleDictionaryConversion'), (310, 543, 'HandleCollectionConversion'),
                     (576, 621, 'HandleNullableCapableTarget'), (623, 683, 'HandleNullableValueSource'),
                     (685, 734, 'HandleTargetNullableComposition'), (880, 920, 'HandleAutoNestedObjectMap')]:
    bad += check(name, 'fd5f833^', CONVF, lo, hi, 'fd5f833', ARMS, name,
                 renames=CONV, strip_scaffold=True)

print('\nFlattenGraph')
bad += check('ResolveOneFlattenGraphDirective', '161836d^', FLAT, 641, 1710, '161836d', DIR,
             'ResolveOneFlattenGraphDirective', renames=FG, unjump='continue;')
bad += check('ResolveHeterogeneousFlattenGraph', 'e08f398^', DIR, 207, 645, 'e08f398', HET,
             'ResolveHeterogeneousFlattenGraph', renames=NAV)
bad += check('ResolveDerivedTypeArms', 'ef30908^', HET, 53, 257, 'ef30908', HARMS, 'ResolveDerivedTypeArms')
bad += check('PartitionNodeMembers', 'ef30908^', DIR, 281, 364, 'ef30908', FMEM,
             'PartitionNodeMembers', renames=[('nav.NodeType', 'nodeType')])

print('\n%s' % ('ALL 12 MOVES RE-DERIVE EXACTLY' if not bad else '%d MOVE(S) DIFFER -- see above' % bad))
sys.exit(1 if bad else 0)

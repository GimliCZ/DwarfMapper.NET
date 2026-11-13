# SPDX-License-Identifier: GPL-2.0-only
"""Rank Generator.Tests' test classes and methods for KillersFirstOrderer (round 34 E4, cost-weighted since round 34's
mutation study).

Usage:  python3 scripts/kill-ranking.py --durations TESTS.trx REPORT.json [REPORT.json ...]

Reads Stryker mutation-report.json files (the legs that run Generator.Tests: generator, pipeline, codefixes,
doctooling; CI uploads each as the artifact mutation-report-<leg>) and one trx of a Generator.Tests run
(dotnet test tests/DwarfMapper.Generator.Tests --filter "Category!=Perf" --logger trx), and rewrites
tests/DwarfMapper.Generator.Tests/MutationKillRanking.tsv, which KillersFirstOrderer reads.

WHY COST-WEIGHTED, NOT MOST KILLS. Stryker tests several mutants in one run when their covering tests do not overlap,
and a run ends only when EVERY mutant in it is decided. Ranked by total kills, the classes that kill everything - golden
and fuzz suites whose rows each re-kill the same broad mutants - ran first, and a narrow mutant whose only killer is a
small unit class waited behind them: in the round-34 EquatableArray trace leg, one run killed its broad mutant 13 s in
and then ran four FeatureCombinationFuzz parts (40 s each, every row failing) before EquatableArrayCoverageTests killed
the other two at 218 s. So the order is the min-sum set cover greedy: repeatedly take the class that kills the most
mutants no class before it kills, per second of its serial runtime; then every class without a recorded kill, cheapest
first. A theory split into part classes (TheoryParts) is ranked under its outer name, as KillersFirstOrderer reads it.

The order changes no verdict: the same tests run, a killed mutant is killed by whichever fails first, and a surviving
one runs every covering test as before. A stale ranking only costs time; refresh it when a round renames or adds test
classes, or when their cost moves.

The file keeps KillersFirstOrderer's format - kind, score, name - with the score falling by rank (higher runs first):
classes over the whole assembly, methods within their class.
"""

import collections
import io
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

OUT = os.path.join('tests', 'DwarfMapper.Generator.Tests', 'MutationKillRanking.tsv')
PREFIX = 'DwarfMapper.Generator.Tests.'
PART = re.compile(r'Part\d+(?=\.[^.]+$|$)')
MIN_COST = 0.005  # seconds: a class trx reports as free still costs its host round trip


def outer(name):
    """A part class's or part method's name as KillersFirstOrderer ranks it (Ns.OuterPart2.M -> Ns.Outer.M)."""
    return PART.sub('', name, count=1)


def costs_from_trx(path):
    """Serial seconds per class and per method, from one trx: the sum of the test durations, as a serial run pays."""
    root = ET.parse(path).getroot()
    ns = {'t': root.tag.split('}')[0].strip('{')}
    method_of = {}
    for d in root.iterfind('.//t:UnitTest', ns):
        tm = d.find('t:TestMethod', ns)
        method_of[d.get('id')] = outer(tm.get('className') + '.' + tm.get('name'))
    by_class, by_method = collections.Counter(), collections.Counter()
    for r in root.iterfind('.//t:UnitTestResult', ns):
        method = method_of.get(r.get('testId'))
        if method is None:
            continue
        h, m, s = r.get('duration', '0:0:0').split(':')
        secs = int(h) * 3600 + int(m) * 60 + float(s)
        by_method[method] += secs
        by_class[method.rpartition('.')[0]] += secs
    return by_class, by_method


def greedy(candidates, killers, cost):
    """Min-sum set cover greedy: most not-yet-killed mutants per second first, then the rest cheapest first."""
    uncovered = {k for k, ks in killers.items() if ks & candidates}
    order = []
    while uncovered:
        gain = collections.Counter(c for k in uncovered for c in killers[k] if c in candidates)
        best = max(gain, key=lambda c: (gain[c] / max(cost.get(c, 0.0), MIN_COST), gain[c], c))
        order.append(best)
        uncovered = {k for k in uncovered if best not in killers[k]}
    taken = set(order)
    return order + sorted((c for c in candidates if c not in taken), key=lambda c: (max(cost.get(c, 0.0), MIN_COST), c))


def main(args):
    if len(args) < 3 or args[0] != '--durations':
        sys.exit(__doc__)
    trx, paths = args[1], args[2:]
    class_cost, method_cost = costs_from_trx(trx)
    class_killers, method_killers = {}, {}
    for path in paths:
        report = json.load(io.open(path, encoding='utf-8'))
        names = {t['id']: t['name'] for tf in report.get('testFiles', {}).values() for t in tf.get('tests', [])}
        for f in report['files'].values():
            for m in f['mutants']:
                if m['status'] != 'Killed':
                    continue
                methods = {outer(re.sub(r'\(.*$', '', names[t])) for t in m.get('killedBy') or []
                           if t in names and names[t].startswith(PREFIX)}  # a theory row counts toward its method
                methods = {x for x in methods if x in method_cost}  # renamed or deleted tests drop out
                if methods:
                    key = (path, m['id'])
                    method_killers[key] = methods
                    class_killers[key] = {x.rpartition('.')[0] for x in methods}
    classes = greedy(set(class_cost), class_killers, class_cost)
    with io.open(OUT, 'w', encoding='utf-8', newline='\n') as w:
        w.write('# Generator.Tests run order for KillersFirstOrderer: most not-yet-killed mutants per second of serial runtime\n')
        w.write('# first (score falls by rank). Generated by scripts/kill-ranking.py - do not edit by hand. Timing:\n')
        w.write('#   ' + os.path.basename(trx) + f' ({sum(class_cost.values()):.0f} s of tests). Reports:\n')
        for path in paths:
            w.write('#   ' + '/'.join(os.path.abspath(path).replace(os.sep, '/').split('/')[-4:]) + '\n')
        for i, name in enumerate(classes):
            w.write(f'class\t{len(classes) - i}\t{name}\n')
        for cls in classes:
            own = {x for x in method_cost if x.rpartition('.')[0] == cls}
            ranked = greedy(own, {k: ms & own for k, ms in method_killers.items() if ms & own}, method_cost)
            for i, name in enumerate(ranked):
                w.write(f'method\t{len(ranked) - i}\t{name}\n')
    killed = len(class_killers)
    print(f'{OUT}: {len(classes)} classes, {len(method_cost)} methods; {killed} killed mutants with a Generator.Tests killer')


if __name__ == '__main__':
    main(sys.argv[1:])

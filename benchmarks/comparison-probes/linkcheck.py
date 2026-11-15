# SPDX-License-Identifier: GPL-2.0-only
# Every RELATIVE link in every tracked Markdown file, resolved against the file's folder: the ones whose target does
# not exist. Fragments are reported separately (heading anchors are checked against GitHub's slug rule).
# Usage: linkcheck.py <repo-root>
import os
import re
import subprocess
import sys
import unicodedata

root = sys.argv[1]
files = subprocess.run(["git", "-C", root, "ls-files", "*.md"], capture_output=True, text=True, check=True).stdout.split()
link = re.compile(r"(?<!!)\[[^\]]*\]\(([^)\s]+)(?:\s+\"[^\"]*\")?\)")
image = re.compile(r"!\[[^\]]*\]\(([^)\s]+)\)")
fence = re.compile(r"^\s*(```|~~~)")


def slug(heading):
    # GitHub: lowercase, drop punctuation except hyphens and spaces, spaces to hyphens.
    text = unicodedata.normalize("NFKC", heading.strip().lower())
    text = re.sub(r"[^\w\- ]", "", text)
    return text.replace(" ", "-")


def anchors(path):
    found, seen, in_fence = set(), {}, False
    with open(path, encoding="utf-8") as f:
        for line in f:
            if fence.match(line):
                in_fence = not in_fence
                continue
            if in_fence:
                continue
            m = re.match(r"^(#{1,6})\s+(.*?)\s*#*\s*$", line)
            if m:
                s = slug(re.sub(r"`|\*|_", "", m.group(2)))
                n = seen.get(s, 0)
                found.add(s if n == 0 else f"{s}-{n}")
                seen[s] = n + 1
            for a in re.findall(r'<a\s+(?:name|id)="([^"]+)"', line):
                found.add(a)
    return found


broken, broken_anchor, checked = [], [], 0
for rel in files:
    path = os.path.join(root, rel)
    in_fence = False
    with open(path, encoding="utf-8") as f:
        lines = f.readlines()
    for no, line in enumerate(lines, 1):
        if fence.match(line):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        for target in link.findall(line) + image.findall(line):
            if re.match(r"^[a-z][a-z0-9+.-]*:", target, re.I) or target.startswith("<"):
                continue  # http(s), mailto, ...
            checked += 1
            target_path, _, fragment = target.partition("#")
            if target_path:
                resolved = os.path.normpath(os.path.join(os.path.dirname(path), target_path))
                if not os.path.exists(resolved):
                    broken.append(f"{rel}:{no}: {target}")
                    continue
            else:
                resolved = path
            if fragment and resolved.endswith(".md") and os.path.isfile(resolved):
                if fragment.lower() not in anchors(resolved):
                    broken_anchor.append(f"{rel}:{no}: {target}")

print(f"{len(files)} Markdown files, {checked} relative links")
print(f"{len(broken)} links to a missing file or folder:")
for b in broken:
    print("  " + b)
print(f"{len(broken_anchor)} links to a missing heading anchor:")
for b in broken_anchor:
    print("  " + b)

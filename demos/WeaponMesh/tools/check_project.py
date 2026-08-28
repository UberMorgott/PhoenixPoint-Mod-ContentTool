#!/usr/bin/env python3
"""
Read ppcontent.json exactly the way ContentTool reads it, and check every row before you spend a
403 MB bake and a restart on a typo.

The regexes below are `ContentProject.ParseReplace` (src\\Project\\ContentProject.cs) transcribed:
the tool does NOT use a JSON parser for the "replace" array, so a check that used one could pass on
a file the tool reads differently. What is asserted here is what the tool asserts - exactly one of
texture/material/mesh/clip/video per row, plus "bundle" and "asset" - and then one thing the tool
can only discover much later: that the stem each row names is a file that exists.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
KINDS = {
    "texture": ("Textures", (".png", ".jpg", ".jpeg")),
    "mesh": ("Meshes", (".obj", ".glb")),
    "video": ("Videos", (".webm", ".mp4", ".mov")),
}

text = open(os.path.join(ROOT, "ppcontent.json"), encoding="utf-8").read()
array = re.search(r'"replace"\s*:\s*\[(.*?)\]', text, re.S)
assert array, "no \"replace\" array"

rows, bad = 0, 0
for obj in re.findall(r"\{[^{}]*\}", array.group(1), re.S):
    field = lambda n: (re.search(r'"%s"\s*:\s*"([^"]*)"' % n, obj) or [None, ""])[1]
    got = {k: field(k) for k in ("texture", "material", "mesh", "clip", "video") if field(k)}
    rows += 1
    if len(got) != 1 or not field("bundle") or not field("asset"):
        print("BAD  %s" % obj.strip())
        bad += 1
        continue
    kind, stem = next(iter(got.items()))
    if kind in KINDS:
        folder, exts = KINDS[kind]
        found = [e for e in exts if os.path.isfile(os.path.join(ROOT, "Content", folder, stem + e))]
        if not found:
            print("BAD  %s '%s' names no file in Content\\%s\\" % (kind, stem, folder))
            bad += 1
            continue
    print("ok   %-9s %-45s <- %s" % (kind, field("asset"), stem))

assert rows, "the array parsed to zero rows - the tool would refuse this file"
assert not bad, "%d of %d rows would not install" % (bad, rows)
print("OK  %d row(s)" % rows)
sys.exit(0)

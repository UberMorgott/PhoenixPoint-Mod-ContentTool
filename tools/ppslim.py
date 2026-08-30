"""Drop animation clips from a GLB and garbage-collect the bytes they owned.

DO NOT RUN THIS ON A PLAYABLE CHARACTER. It is OPTIONAL and it is the last resort, not a
pipeline step. A soldier has to be able to play the WHOLE game - any weapon, any stance,
any situation - and every clip the game may ask for has to be in the file. A trimmed
character stalls the moment it reaches a state whose clip was dropped: the measured symptom
is an AIMED PISTOL SHOT that never returns, leaving the camera frozen on the actor forever,
because the ability waits on an animation event that no clip will ever fire. Shipping the
full 300-clip set is CORRECT even though the file is ~100 MB; size is not a reason to trim.

What it is genuinely for: a non-playable prop or a bench model whose complete state list you
KNOW, and diagnostics - `--list` names the clip families and the bytes each owns, which is
how the size question gets answered without touching the file. ppretarget.py writes every PP
human clip it can rewrite (300 on tiffany_ppfit.glb), and on such a model 89.3 MB of the
97.6 MB BIN chunk is animation sampler data against a 4.2 MB mesh and 2.9 MB of textures -
so the clip list is the only lever there is. That makes trimming the ONLY way to make the
file smaller, not a safe one.

Deleting an animation is only half the job: its sampler accessors and their bufferViews stay
in the file until something removes them, so this walks the reachable set from what SURVIVES
(mesh primitives, skin inverseBindMatrices, images, kept animations), keeps exactly those
accessors/bufferViews, and rewrites a compacted BIN chunk. Everything that is not sampler
data is copied through byte for byte - no mesh decode, no image recompression.

    python tools\\ppslim.py --list  MODEL.glb                     # families + owned bytes
    python tools\\ppslim.py MODEL.glb OUT.glb --drop RE [--keep RE] [--require NAME,...]

--drop / --keep are Python regexes matched with re.search against the clip name; --keep wins
over --drop. --require is the safety net: the run FAILS if a named clip did not survive.

ponytail: stdlib only, single pass, no glTF library. Accessors with no bufferView (sparse /
zero-filled) are passed through untouched rather than special-cased.
"""
import argparse
import collections
import json
import re
import struct
import sys


def read_glb(path):
    with open(path, "rb") as f:
        magic, _ver, _total = struct.unpack("<4sII", f.read(12))
        if magic != b"glTF":
            raise SystemExit("%s is not a GLB" % path)
        jlen, jtag = struct.unpack("<I4s", f.read(8))
        if jtag != b"JSON":
            raise SystemExit("first chunk is not JSON")
        gltf = json.loads(f.read(jlen))
        blen, btag = struct.unpack("<I4s", f.read(8))
        if btag != b"BIN\x00":
            raise SystemExit("second chunk is not BIN")
        return gltf, f.read(blen)


def write_glb(path, gltf, bin_blob):
    js = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    js += b" " * (-len(js) % 4)
    bin_blob += b"\x00" * (-len(bin_blob) % 4)
    total = 12 + 8 + len(js) + 8 + len(bin_blob)
    with open(path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, total))
        f.write(struct.pack("<I4s", len(js), b"JSON") + js)
        f.write(struct.pack("<I4s", len(bin_blob), b"BIN\x00") + bin_blob)


def clip_views(gltf, anim):
    """bufferViews reachable from one animation's samplers."""
    acc = gltf["accessors"]
    out = set()
    for s in anim["samplers"]:
        for key in ("input", "output"):
            a = acc[s[key]]
            if "bufferView" in a:
                out.add(a["bufferView"])
    return out


def do_list(gltf):
    views = gltf["bufferViews"]
    per = collections.Counter()
    cnt = collections.Counter()
    for an in gltf.get("animations", []):
        fam = "_".join(an["name"].split("_")[:2])
        cnt[fam] += 1
        per[fam] += sum(views[i]["byteLength"] for i in clip_views(gltf, an))
    for fam in sorted(per, key=per.get, reverse=True):
        print("%-30s n=%-4d %10d" % (fam, cnt[fam], per[fam]))
    print("%-30s n=%-4d %10d" % ("TOTAL", sum(cnt.values()), sum(per.values())))


def slim(gltf, blob, drop, keep, require):
    anims = gltf.get("animations", [])
    kept_anims = [a for a in anims
                  if not (drop.search(a["name"]) and not (keep and keep.search(a["name"])))]
    missing = [n for n in require if n not in {a["name"] for a in kept_anims}]
    if missing:
        raise SystemExit("required clips dropped: %s" % ", ".join(missing))
    gltf["animations"] = kept_anims

    # reachable accessors: mesh attributes/indices/targets, skins, kept animation samplers
    live_acc = set()
    for m in gltf.get("meshes", []):
        for pr in m["primitives"]:
            live_acc.update(pr["attributes"].values())
            if "indices" in pr:
                live_acc.add(pr["indices"])
            for tgt in pr.get("targets", []):
                live_acc.update(tgt.values())
    for sk in gltf.get("skins", []):
        if "inverseBindMatrices" in sk:
            live_acc.add(sk["inverseBindMatrices"])
    for an in kept_anims:
        for s in an["samplers"]:
            live_acc.update((s["input"], s["output"]))

    acc_old = gltf["accessors"]
    acc_map = {}
    new_acc = []
    for i in sorted(live_acc):
        acc_map[i] = len(new_acc)
        new_acc.append(acc_old[i])

    live_bv = {a["bufferView"] for a in new_acc if "bufferView" in a}
    live_bv |= {im["bufferView"] for im in gltf.get("images", []) if "bufferView" in im}
    bv_old = gltf["bufferViews"]
    bv_map = {}
    new_bv = []
    out = bytearray()
    for i in sorted(live_bv):
        v = dict(bv_old[i])
        off = v.get("byteOffset", 0)
        chunk = blob[off:off + v["byteLength"]]
        out += b"\x00" * (-len(out) % 4)          # keep 4-byte alignment for typed reads
        v["byteOffset"] = len(out)
        out += chunk
        bv_map[i] = len(new_bv)
        new_bv.append(v)

    for a in new_acc:
        if "bufferView" in a:
            a["bufferView"] = bv_map[a["bufferView"]]
    for im in gltf.get("images", []):
        if "bufferView" in im:
            im["bufferView"] = bv_map[im["bufferView"]]
    for m in gltf.get("meshes", []):
        for pr in m["primitives"]:
            pr["attributes"] = {k: acc_map[v] for k, v in pr["attributes"].items()}
            if "indices" in pr:
                pr["indices"] = acc_map[pr["indices"]]
            if "targets" in pr:
                pr["targets"] = [{k: acc_map[v] for k, v in t.items()} for t in pr["targets"]]
    for sk in gltf.get("skins", []):
        if "inverseBindMatrices" in sk:
            sk["inverseBindMatrices"] = acc_map[sk["inverseBindMatrices"]]
    for an in kept_anims:
        for s in an["samplers"]:
            s["input"] = acc_map[s["input"]]
            s["output"] = acc_map[s["output"]]

    gltf["accessors"] = new_acc
    gltf["bufferViews"] = new_bv
    gltf["buffers"] = [{"byteLength": len(out)}]
    return gltf, bytes(out), len(anims) - len(kept_anims), len(kept_anims)


def selfcheck():
    """Minimal end-to-end: build a 2-clip GLB, drop one, assert the other still resolves."""
    blob = struct.pack("<4f", 0.0, 1.0, 2.0, 3.0) + struct.pack("<3f", 9.0, 9.0, 9.0)
    g = {
        "asset": {"version": "2.0"},
        "buffers": [{"byteLength": len(blob)}],
        "bufferViews": [{"buffer": 0, "byteOffset": 0, "byteLength": 16},
                        {"buffer": 0, "byteOffset": 16, "byteLength": 12}],
        "accessors": [{"bufferView": 0, "componentType": 5126, "count": 4, "type": "SCALAR"},
                      {"bufferView": 0, "componentType": 5126, "count": 4, "type": "SCALAR"},
                      {"bufferView": 1, "componentType": 5126, "count": 1, "type": "VEC3"}],
        "meshes": [{"primitives": [{"attributes": {"POSITION": 2}}]}],
        "nodes": [{"mesh": 0}],
        "animations": [
            {"name": "KEEP_Me", "samplers": [{"input": 0, "output": 0}],
             "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}]},
            {"name": "DROP_Me", "samplers": [{"input": 1, "output": 1}],
             "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}]},
        ],
    }
    out, blob2, dropped, kept = slim(g, blob, re.compile("^DROP_"), None, ["KEEP_Me"])
    assert (dropped, kept) == (1, 1), (dropped, kept)
    assert len(out["animations"]) == 1 and out["animations"][0]["name"] == "KEEP_Me"
    assert len(out["accessors"]) == 2 and len(out["bufferViews"]) == 2  # orphan gone, mesh kept
    pos = out["accessors"][out["meshes"][0]["primitives"][0]["attributes"]["POSITION"]]
    v = out["bufferViews"][pos["bufferView"]]
    assert struct.unpack_from("<3f", blob2, v["byteOffset"]) == (9.0, 9.0, 9.0)
    assert len(blob2) == 28, len(blob2)
    try:
        slim(json.loads(json.dumps(g)), blob, re.compile("_Me$"), None, ["KEEP_Me"])
    except SystemExit:
        pass
    else:
        raise AssertionError("--require did not fail on a dropped clip")
    print("ppslim selfcheck: ok")


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("src", nargs="?")
    ap.add_argument("dst", nargs="?")
    ap.add_argument("--list", action="store_true")
    ap.add_argument("--drop")
    ap.add_argument("--keep")
    ap.add_argument("--require", default="")
    ap.add_argument("--selfcheck", action="store_true")
    a = ap.parse_args(argv)
    if a.selfcheck:
        return selfcheck()
    if not a.src:
        ap.error("need a GLB")
    gltf, blob = read_glb(a.src)
    if a.list:
        return do_list(gltf)
    if not (a.dst and a.drop):
        ap.error("need dst and --drop")
    was = len(blob)
    gltf, blob, dropped, kept = slim(gltf, blob, re.compile(a.drop),
                                     re.compile(a.keep) if a.keep else None,
                                     [n for n in a.require.split(",") if n])
    write_glb(a.dst, gltf, blob)
    print("dropped %d clips, kept %d, bin %d -> %d bytes" % (dropped, kept, was, len(blob)))


if __name__ == "__main__":
    main(sys.argv[1:])

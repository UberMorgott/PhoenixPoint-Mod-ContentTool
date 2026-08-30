"""Shrink the ANIMATION half of a GLB without dropping a single clip or a single key of motion.

This is the other answer to "why does the humanoid demo weigh 104 MB". ppslim.py's answer is to
delete clips, and its own header explains at length why that is a last resort - a soldier that
reaches a state whose clip was dropped stalls forever on an animation event nobody will fire. This
tool never touches the clip list. It rewrites how the SAME curves are stored, so every clip, every
channel and every key survives and the file gets smaller anyway.

MEASURED on local\\PpFit\\Content\\Models\\tiffany_ppfit.glb (104,511,576 B, 300 clips, 29,082
sampler accessors, 89,281,672 B of animation), which is where both numbers below come from:

  --constant  14,283 of its 27,284 rotation channels (52.3%) hold ONE quaternion for the whole
              clip and spend up to 805 keys restating it - 44,574,192 B, 52.7% of all rotation
              bytes. Collapsing each to its two endpoint keys is EXACTLY LOSSLESS: the importer
              resamples every curve onto a uniform grid anyway (GlbReader.cs:1084-1112) and a
              two-key constant samples to the same value at every frame a 805-key constant did.

  --quantise  rotation values are quaternion components, so they are already in [-1, 1] and int16
              normalized stores them at a measured worst-case error of 1.53e-05 per component -
              about 0.002 degrees - for half the bytes. src\\Import\\GlbReader.cs:2099-2108 already
              DECODES normalized SHORT on every accessor path including animation, so this needs
              nothing on the C# side. Translation is NOT quantised: it is in metres with no bound,
              which is exactly what a normalized type cannot express.

WHAT THIS TOOL DELIBERATELY DOES NOT DO: resample to a lower rate. Every sampler in that file is at
120 Hz and that is not laziness in the exporter, it is load-bearing - tools\\ClipCensus\\Export.cs
picks 120 because GlbReader bakes onto the coarsest rate every key time lands on and looks no higher
(src\\Import\\GlbReader.cs:44), and it MEASURED 60 Hz costing up to 0.023 of a quaternion component,
about 2.6 degrees. Halving the rate would be the biggest single saving on paper and the only one
here that a player could see. So it is not offered.

    python tools\\ppzip.py MODEL.glb OUT.glb [--constant] [--quantise]   (both, if neither is named)
    python tools\\ppzip.py --selfcheck

ponytail: stdlib only, and the whole garbage-collect/compact/rewrite pass is ppslim.slim() with a
regex that matches no clip name - this file only decides what the samplers should say, it does not
re-solve where the bytes go.
"""
import argparse
import json
import struct
import sys

import ppslim

FLOAT, SHORT = 5126, 5122
COMPONENTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
SIZE = {5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4}
# A quaternion component that survives the int16 round trip to within this is the same rotation.
# 1/32767 is the quantum itself; anything at or under half of it is representation, not error.
QUANT_MAX_ERROR = 1.0 / 65534.0


def read_floats(gltf, blob, index):
    """One accessor as a flat list of floats. Float and normalized-short are both understood, so
    the tool is idempotent - running it twice reads back what the first run wrote."""
    a = gltf["accessors"][index]
    view = gltf["bufferViews"][a["bufferView"]]
    n = a["count"] * COMPONENTS[a["type"]]
    at = view.get("byteOffset", 0) + a.get("byteOffset", 0)
    if a["componentType"] == FLOAT:
        return list(struct.unpack_from("<%df" % n, blob, at))
    if a["componentType"] == SHORT and a.get("normalized"):
        return [max(v / 32767.0, -1.0) for v in struct.unpack_from("<%dh" % n, blob, at)]
    raise SystemExit("accessor %d is componentType %d, which this tool does not rewrite"
                     % (index, a["componentType"]))


def tightly_packed(gltf, index):
    """False for an accessor this tool must not touch, because read_floats would misread it.

    glTF lets a bufferView declare a byteStride and lets an accessor be sparse; both are legal on a
    sampler and both mean the values are NOT the flat little-endian run struct.unpack_from assumes.
    Reading one as if it were would splice padding or a neighbouring attribute into the curve, and
    the constant test and the quantiser would then rewrite that corruption into the file. Nothing
    ppretarget.py writes is ever strided, so this is a guard and not a feature: a sampler that fails
    it is copied through byte for byte, exactly as ppslim.py passes sparse accessors through.
    """
    a = gltf["accessors"][index]
    if "bufferView" not in a or "sparse" in a:
        return False
    if a["componentType"] not in SIZE:
        return False
    stride = gltf["bufferViews"][a["bufferView"]].get("byteStride")
    return stride is None or stride == SIZE[a["componentType"]] * COMPONENTS[a["type"]]


def is_constant(values, stride, eps=1e-6):
    """True when every element of the curve equals the first one. eps is a QUANTITY of rotation,
    not a float tolerance: 1e-6 of a quaternion component is ~1e-4 degrees, far under what the
    int16 form below can even represent, so a curve this still is a curve that does not move."""
    if len(values) <= stride:
        return False
    first = values[:stride]
    for i in range(stride, len(values)):
        if abs(values[i] - first[i % stride]) > eps:
            return False
    return True


def pack(values, quantise):
    if not quantise:
        return struct.pack("<%df" % len(values), *values)
    out = bytearray()
    for v in values:
        # Clamped to +-32767, not +-32768: GlbReader decodes with max(x / 32767, -1), so -32768
        # would come back as -1.0000305 and then be clamped anyway. Round-trip exactness first.
        q = int(round(v * 32767.0))
        out += struct.pack("<h", -32767 if q < -32767 else (32767 if q > 32767 else q))
    return bytes(out)


def zip_anims(gltf, blob, constant, quantise):
    """Rewrite every animation sampler in place. Returns (gltf, blob, stats)."""
    out = bytearray(blob)
    stats = {"collapsed": 0, "quantised": 0, "keys_before": 0, "keys_after": 0, "skipped": 0}

    def add_view(data):
        out.extend(b"\x00" * (-len(out) % 4))
        gltf["bufferViews"].append(
            {"buffer": 0, "byteOffset": len(out), "byteLength": len(data)})
        out.extend(data)
        return len(gltf["bufferViews"]) - 1

    for anim in gltf.get("animations", []):
        samplers = anim["samplers"]
        # A sampler drives whatever its CHANNEL says; only rotation is quantisable and only a
        # channel tells us which is which. A sampler nothing points at is left alone.
        path_of = {}
        for ch in anim["channels"]:
            path_of[ch["sampler"]] = ch["target"]["path"]

        curves = {}
        for si, s in enumerate(samplers):
            if si not in path_of or s.get("interpolation", "LINEAR") != "LINEAR":
                continue
            if not tightly_packed(gltf, s["output"]) or not tightly_packed(gltf, s["input"]):
                stats["skipped"] += 1
                continue
            acc = gltf["accessors"][s["output"]]
            stride = COMPONENTS[acc["type"]]
            values = read_floats(gltf, blob, s["output"])
            curves[si] = (values, stride, is_constant(values, stride))

        # LEAVE A WHOLLY CONSTANT CLIP ALONE. GlbReader picks a clip's frame rate from the coarsest
        # rate every key time lands on (GlbReader.cs:1085) and derives the clip's LENGTH from it. As
        # long as one channel keeps its dense key times that rate is unchanged, which is the case in
        # every real clip; but if this collapsed the last dense channel too, the only times left
        # would be the two endpoints, the rate could drop to 1 Hz and the clip would come out
        # LONGER than it was authored. Not worth a special case - such a clip is a static pose.
        collapse = constant and not all(c[2] for c in curves.values()) if curves else False

        new_input = {}          # old input accessor -> 2-key replacement, one per clip
        data = bytearray()

        def place(accessor, payload, count, component, normalized):
            data.extend(b"\x00" * (-len(data) % 4))   # >= every component size we emit
            accessor["byteOffset"] = len(data)
            accessor["count"] = count
            accessor["componentType"] = component
            if normalized:
                accessor["normalized"] = True
            else:
                accessor.pop("normalized", None)
            # min/max describe the OLD data and glTF only requires them on animation input
            # accessors, which this never rewrites in place.
            accessor.pop("min", None)
            accessor.pop("max", None)
            data.extend(payload)

        for si, (values, stride, const) in curves.items():
            s = samplers[si]
            acc = gltf["accessors"][s["output"]]
            rotation = path_of[si] == "rotation"
            quant = quantise and rotation
            stats["keys_before"] += acc["count"]

            if collapse and const:
                values = values[:stride] * 2
                times = read_floats(gltf, blob, s["input"])
                key = (s["input"], times[0], times[-1])
                if key not in new_input:
                    view = add_view(struct.pack("<2f", times[0], times[-1]))
                    gltf["accessors"].append({
                        "bufferView": view, "componentType": FLOAT, "count": 2, "type": "SCALAR",
                        "min": [times[0]], "max": [times[-1]]})
                    new_input[key] = len(gltf["accessors"]) - 1
                s["input"] = new_input[key]
                stats["collapsed"] += 1

            if quant:
                stats["quantised"] += 1
            place(acc, pack(values, quant), len(values) // stride,
                  SHORT if quant else FLOAT, quant)
            stats["keys_after"] += acc["count"]

        if data:
            view = add_view(bytes(data))
            for si in curves:
                gltf["accessors"][samplers[si]["output"]]["bufferView"] = view

    # ppslim's own pass drops every accessor and bufferView nothing points at any more - which is
    # exactly the dense arrays just replaced - and rewrites a compacted BIN. "(?!)" matches nothing,
    # so no clip is dropped: this tool never removes an animation.
    import re
    gltf, packed, dropped, kept = ppslim.slim(gltf, bytes(out), re.compile("(?!)"), None, [])
    assert dropped == 0, dropped
    return gltf, packed, stats


def selfcheck():
    """One constant curve and one moving curve through the real path, then read the result back."""
    still = [0.0, 0.0, 0.0, 1.0] * 4                       # 4 identical quaternion keys
    moving = [0.0, 0.0, 0.0, 1.0, 0.5, 0.0, 0.0, 0.86602540]
    times4 = struct.pack("<4f", 0.0, 0.25, 0.5, 0.75)
    times2 = struct.pack("<2f", 0.0, 0.25)
    blob = (times4 + times2 + struct.pack("<16f", *still) + struct.pack("<8f", *moving))
    g = {
        "asset": {"version": "2.0"},
        "buffers": [{"byteLength": len(blob)}],
        "bufferViews": [{"buffer": 0, "byteOffset": 0, "byteLength": 16},
                        {"buffer": 0, "byteOffset": 16, "byteLength": 8},
                        {"buffer": 0, "byteOffset": 24, "byteLength": 64},
                        {"buffer": 0, "byteOffset": 88, "byteLength": 32}],
        "accessors": [
            {"bufferView": 0, "componentType": FLOAT, "count": 4, "type": "SCALAR",
             "min": [0.0], "max": [0.75]},
            {"bufferView": 1, "componentType": FLOAT, "count": 2, "type": "SCALAR",
             "min": [0.0], "max": [0.25]},
            {"bufferView": 2, "componentType": FLOAT, "count": 4, "type": "VEC4"},
            {"bufferView": 3, "componentType": FLOAT, "count": 2, "type": "VEC4"}],
        "meshes": [], "skins": [], "nodes": [{}, {}],
        "animations": [{
            "name": "Clip",
            "samplers": [{"input": 0, "output": 2}, {"input": 1, "output": 3}],
            "channels": [{"sampler": 0, "target": {"node": 0, "path": "rotation"}},
                         {"sampler": 1, "target": {"node": 1, "path": "rotation"}}]}],
    }
    out, packed, stats = zip_anims(json.loads(json.dumps(g)), blob, True, True)
    assert stats["collapsed"] == 1, stats            # the still curve, and only it
    assert stats["quantised"] == 2, stats            # both are rotation
    assert stats["keys_before"] == 6 and stats["keys_after"] == 4, stats
    assert len(out["animations"]) == 1 and len(out["animations"][0]["channels"]) == 2

    sam = out["animations"][0]["samplers"]
    got = [read_floats(out, packed, s["output"]) for s in sam]
    assert len(got[0]) == 8 and all(abs(got[0][i] - still[i]) <= QUANT_MAX_ERROR for i in range(8)), got[0]
    assert len(got[1]) == 8 and all(abs(got[1][i] - moving[i]) <= QUANT_MAX_ERROR for i in range(8)), got[1]
    # the collapsed curve kept its own start and end instants, so the clip still runs 0 -> 0.75
    ends = read_floats(out, packed, sam[0]["input"])
    assert ends == [0.0, 0.75], ends
    # and the moving curve's key times were not touched at all
    assert read_floats(out, packed, sam[1]["input"]) == [0.0, 0.25]
    assert len(packed) < len(blob), (len(packed), len(blob))

    # A clip whose every curve is constant is left dense, so its frame rate cannot drift.
    solo = json.loads(json.dumps(g))
    solo["animations"][0]["samplers"] = [solo["animations"][0]["samplers"][0]]
    solo["animations"][0]["channels"] = [solo["animations"][0]["channels"][0]]
    out2, packed2, stats2 = zip_anims(solo, blob, True, False)
    assert stats2["collapsed"] == 0, stats2
    assert read_floats(out2, packed2, out2["animations"][0]["samplers"][0]["input"]) == \
        [0.0, 0.25, 0.5, 0.75]
    # A strided sampler is copied through untouched rather than misread as a flat run.
    strided = json.loads(json.dumps(g))
    strided["bufferViews"][2]["byteStride"] = 32
    out3, packed3, stats3 = zip_anims(strided, blob, True, True)
    assert stats3["skipped"] == 1 and stats3["collapsed"] == 0, stats3
    assert stats3["quantised"] == 1, stats3                  # the other, packed, curve still shrinks
    kept = out3["accessors"][out3["animations"][0]["samplers"][0]["output"]]
    assert kept["componentType"] == FLOAT and kept["count"] == 4, kept
    assert out3["bufferViews"][kept["bufferView"]].get("byteStride") == 32
    assert struct.unpack_from("<16f", packed3,
                              out3["bufferViews"][kept["bufferView"]]["byteOffset"] +
                              kept.get("byteOffset", 0)) == tuple(still)
    print("ppzip selfcheck: ok")


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("model", nargs="?")
    ap.add_argument("out", nargs="?")
    ap.add_argument("--constant", action="store_true",
                    help="collapse a curve that never moves to its two endpoint keys (lossless)")
    ap.add_argument("--quantise", action="store_true",
                    help="store rotation as normalized int16 (worst measured error 1.53e-05)")
    ap.add_argument("--selfcheck", action="store_true")
    a = ap.parse_args(argv)
    if a.selfcheck:
        selfcheck()
        return 0
    if not a.model or not a.out:
        ap.error("give a model and an output path, or --selfcheck")
    constant, quantise = a.constant, a.quantise
    if not constant and not quantise:
        constant = quantise = True

    gltf, blob = ppslim.read_glb(a.model)
    before = 12 + 8 + len(json.dumps(gltf, separators=(",", ":")).encode("utf-8")) + 8 + len(blob)
    gltf, packed, stats = zip_anims(gltf, blob, constant, quantise)
    ppslim.write_glb(a.out, gltf, packed)

    import os
    after = os.path.getsize(a.out)
    print("ppzip: %d curve(s) collapsed to 2 keys, %d rotation curve(s) as int16, %d left alone "
          "(strided or sparse); %d keys -> %d; %d B -> %d B (-%.1f%%)"
          % (stats["collapsed"], stats["quantised"], stats["skipped"], stats["keys_before"],
             stats["keys_after"], before, after, 100.0 * (before - after) / max(1, before)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

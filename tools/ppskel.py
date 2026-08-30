"""Rewrite a foreign GLB skeleton so its transform PATHS match Phoenix Point's human rig.

WHY paths and nothing else: PP rigs are Unity GENERIC (not Humanoid), and a generic clip binds
every curve to CRC32 of the transform path RELATIVE TO THE ANIMATOR's GameObject
(src/Bake/ClipFields.cs:34-41 - measured, not remembered). The game ships no retargeter and no
path remapper, so a clip authored on PP's soldier drives a foreign model if, and only if, that
model spells the same paths. Renaming makes the curves BIND; making them LOOK right is a
separate, later problem (see NEXT LEVER at the bottom of this file).

Three edits, each geometry-preserving:
  RENAME   - a name change moves no vertex.
  INSERT   - an identity-transform node slipped between a parent and a child preserves the
             child's world transform exactly. PP carries roll bones INSIDE the chain
             (L.UpLeg/L.UpLeg_Roll_1/L.UpLeg_Roll_2/L.Leg); this rig has its twist bones as
             SIBLINGS, so the chain must grow the missing links.
  COLLAPSE - reparent a node onto its grandparent with the skipped node's local matrix composed
             in; world transform unchanged, the skipped node stays as a childless leaf. Needed
             once: this rig has neck_01/neck_02 where PP has a single Neck.

Skinning in glTF is index-based (skin.joints[] parallel to inverseBindMatrices), so renames and
inserts cannot disturb it as long as nothing is deleted or reordered. Nothing here deletes or
reorders - new nodes are appended past the end of the original array - and --check asserts it.

    python tools\\ppskel.py            # convert, write the map, then check
    python tools\\ppskel.py --check    # check an already-converted file only

ponytail: stdlib only, JSON-chunk only. A rename/insert never touches the BIN blob, so there is
no glTF library here and no mesh decode - the binary chunk is copied through byte for byte.
"""
import json
import math
import os
import struct
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PP_PREFAB = os.path.join(ROOT, "..", "extracted", "GameData", "prefabs", "CHR_Human_Rig_Ready.json")
SRC = os.path.join(ROOT, "tiffany_cox_idle_animation.glb")
DST = os.path.join(ROOT, "tiffany_cox_ppskel.glb")
MAP = os.path.join(ROOT, "tools", "ppskel-bone-map.json")
ANIM_ROOT = "CHR_Human_Rig_Ready"   # the node the Animator must sit on in the converted model

# ---------------------------------------------------------------- foreign name -> PP bone name
RENAME = {
    "_rootJoint": ANIM_ROOT,
    "normal_stand_idle1_02": "BaseManReference",
    "pelvis_03": "Root",
    "spine_01_04": "Spine_1", "spine_02_05": "Spine_2", "spine_03_06": "Chest",
    "neck_01_051": "Neck", "head_053": "Head",
}
_SIDE = {
    "l": dict(clav="07", up="08", low="09", hand="010", idx=("011", "012", "013"),
              mid=("014", "015", "016"), pnk=("017", "018", "019"), rng=("020", "021", "022"),
              thb=("023", "024", "025"), wpn="026", thigh="0128", calf="0129", foot="0131",
              ball="0132", toe="0133"),
    "r": dict(clav="029", up="030", low="031", hand="032", idx=("033", "034", "035"),
              mid=("036", "037", "038"), pnk=("039", "040", "041"), rng=("042", "043", "044"),
              thb=("045", "046", "047"), wpn="048", thigh="0135", calf="0136", foot="0138",
              ball="0139", toe="0140"),
}
for _s, _S in (("l", "L"), ("r", "R")):
    _n = _SIDE[_s]
    RENAME["clavicle_%s_%s" % (_s, _n["clav"])] = _S + ".Shoulder"
    RENAME["upperarm_%s_%s" % (_s, _n["up"])] = _S + ".Arm"
    RENAME["lowerarm_%s_%s" % (_s, _n["low"])] = _S + ".ForeArm"
    RENAME["hand_%s_%s" % (_s, _n["hand"])] = _S + ".Hand"
    for _k, _stem, _pp in (("idx", "index", "Index"), ("mid", "middle", "Middle"),
                           ("pnk", "pinky", "Pinky"), ("rng", "ring", "Ring"),
                           ("thb", "thumb", "Thumb")):
        for _i in range(3):
            RENAME["%s_0%d_%s_%s" % (_stem, _i + 1, _s, _n[_k][_i])] = "%s.%s_%d" % (_S, _pp, _i + 1)
    # PP's only hand sockets: gun_point_hand on the right, gun_point_shield on the left.
    RENAME["weapon_attach_%s_%s" % (_s, _n["wpn"])] = "gun_point_hand" if _s == "r" else "gun_point_shield"
    RENAME["thigh_%s_%s" % (_s, _n["thigh"])] = _S + ".UpLeg"
    RENAME["calf_%s_%s" % (_s, _n["calf"])] = _S + ".Leg"
    RENAME["foot_%s_%s" % (_s, _n["foot"])] = _S + ".Foot"
    RENAME["ball_%s_%s" % (_s, _n["ball"])] = _S + ".Foot_Toes"
    RENAME["middle_proximal_phalange_%s_%s" % (_s, _n["toe"])] = _S + ".Foot_Tip"

# links PP has inside the chain that this rig lacks: (PP child, [names to insert above it])
INSERT_ABOVE = [("Chest", ["Spine_3"])]
for _S in ("L", "R"):
    INSERT_ABOVE += [
        (_S + ".ForeArm", [_S + ".Arm_Roll_1", _S + ".Arm_Roll_2"]),
        (_S + ".Hand", [_S + ".ForeArm_Roll_1", _S + ".ForeArm_Roll_2"]),
        (_S + ".Leg", [_S + ".UpLeg_Roll_1", _S + ".UpLeg_Roll_2"]),
        (_S + ".Foot", [_S + ".Leg_Roll_1"]),
    ]
COLLAPSE = [("Head", "neck_02_052")]   # (node kept, node it is hoisted past)


# ------------------------------------------------------------------------------- GLB container
def glb_read(path):
    b = open(path, "rb").read()
    assert b[:4] == b"glTF", "not a GLB: " + path
    total = struct.unpack_from("<I", b, 8)[0]
    off, gltf, binc = 12, None, b""
    while off < total:
        clen, ctype = struct.unpack_from("<II", b, off)
        data = b[off + 8:off + 8 + clen]
        if ctype == 0x4E4F534A:
            gltf = json.loads(data.decode("utf-8"))
        elif ctype == 0x004E4942:
            binc = data
        off += 8 + clen + ((4 - clen % 4) % 4)
    return gltf, binc


def glb_write(path, gltf, binc):
    js = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    js += b" " * ((4 - len(js) % 4) % 4)
    bc = binc + b"\0" * ((4 - len(binc) % 4) % 4)
    total = 12 + 8 + len(js) + (8 + len(bc) if bc else 0)
    with open(path, "wb") as f:
        f.write(b"glTF" + struct.pack("<II", 2, total))
        f.write(struct.pack("<II", len(js), 0x4E4F534A) + js)
        if bc:
            f.write(struct.pack("<II", len(bc), 0x004E4942) + bc)


# ------------------------------------------------------------------------------ 4x4 TRS helpers
def trs(n):
    t = n.get("translation", [0, 0, 0])
    x, y, z, w = n.get("rotation", [0, 0, 0, 1])
    s = n.get("scale", [1, 1, 1])
    r = [[1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w)],
         [2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w)],
         [2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y)]]
    return [[r[i][j] * s[i] for j in range(3)] + [0.0] for i in range(3)] + [[t[0], t[1], t[2], 1.0]]


def mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def decompose(m):
    s = [math.sqrt(sum(m[i][j] ** 2 for j in range(3))) for i in range(3)]
    r = [[m[i][j] / s[i] for j in range(3)] for i in range(3)]
    tr = r[0][0] + r[1][1] + r[2][2]
    if tr > 0:
        k = math.sqrt(tr + 1.0) * 2
        q = [(r[1][2] - r[2][1]) / k, (r[2][0] - r[0][2]) / k, (r[0][1] - r[1][0]) / k, 0.25 * k]
    elif r[0][0] > r[1][1] and r[0][0] > r[2][2]:
        k = math.sqrt(1.0 + r[0][0] - r[1][1] - r[2][2]) * 2
        q = [0.25 * k, (r[1][0] + r[0][1]) / k, (r[2][0] + r[0][2]) / k, (r[1][2] - r[2][1]) / k]
    elif r[1][1] > r[2][2]:
        k = math.sqrt(1.0 + r[1][1] - r[0][0] - r[2][2]) * 2
        q = [(r[1][0] + r[0][1]) / k, 0.25 * k, (r[2][1] + r[1][2]) / k, (r[2][0] - r[0][2]) / k]
    else:
        k = math.sqrt(1.0 + r[2][2] - r[0][0] - r[1][1]) * 2
        q = [(r[2][0] + r[0][2]) / k, (r[2][1] + r[1][2]) / k, 0.25 * k, (r[0][1] - r[1][0]) / k]
    return m[3][:3], q, s


# ------------------------------------------------------- the PP rig's own paths, from the prefab
def pp_paths():
    """Every transform path under the PP soldier rig's Animator root, exactly as a clip spells
    it. The Animator sits on the prefab's root GameObject, so the root itself is the empty path
    and is excluded."""
    d = json.load(open(PP_PREFAB, encoding="utf-8"))
    go = {o["pathID"]: o for o in d["objects"] if o["classID"] == "1"}
    tr = {o["pathID"]: o for o in d["objects"] if o["classID"] == "4"}
    out = []

    def walk(t, prefix):
        f = t["fields"]
        name = go[str(f["m_GameObject"]["fileID"])]["fields"]["m_Name"]
        path = name if prefix is None else (name if prefix == "" else prefix + "/" + name)
        if prefix is not None:
            out.append(path)
        kids = [tr[str(c["fileID"])] for c in f["m_Children"] if str(c["fileID"]) in tr]
        for k in sorted(kids, key=lambda k: k["fields"]["m_RootOrder"]):
            walk(k, "" if prefix is None else path)

    for t in tr.values():
        if str(t["fields"]["m_Father"]["fileID"]) not in tr:
            walk(t, None)
    return out


def pp_rest():
    """path -> (localPosition xyz, localRotation xyzw), UNITY space, straight off the prefab.

    Same walk as pp_paths, carrying the two fields a rest pose is. The retarget tool
    (tools/ppretarget.py) and the clip exporter (tools/ClipCensus, --export) both need it: one to
    give the foreign rig PP's rest ORIENTATION, the other to recognise a position curve that only
    ever restates the rest offset."""
    d = json.load(open(PP_PREFAB, encoding="utf-8"))
    go = {o["pathID"]: o for o in d["objects"] if o["classID"] == "1"}
    tr = {o["pathID"]: o for o in d["objects"] if o["classID"] == "4"}
    # The Animator's own GameObject is the EMPTY relative path, and ROOT MOTION binds to CRC32("").
    # Leaving it out of the table is not a missing row, it is a clip's travel thrown away.
    out = {"": ([0.0, 0.0, 0.0], [0.0, 0.0, 0.0, 1.0])}

    def walk(t, prefix):
        f = t["fields"]
        name = go[str(f["m_GameObject"]["fileID"])]["fields"]["m_Name"]
        path = name if prefix is None else (name if prefix == "" else prefix + "/" + name)
        if prefix is not None:
            p, q = f["m_LocalPosition"], f["m_LocalRotation"]
            out[path] = ([p["x"], p["y"], p["z"]], [q["x"], q["y"], q["z"], q["w"]])
        kids = [tr[str(c["fileID"])] for c in f["m_Children"] if str(c["fileID"]) in tr]
        for k in sorted(kids, key=lambda k: k["fields"]["m_RootOrder"]):
            walk(k, "" if prefix is None else path)

    for t in tr.values():
        if str(t["fields"]["m_Father"]["fileID"]) not in tr:
            walk(t, None)
    return out


def rest_tsv(stream=sys.stdout):
    """The same table as TSV, for the C# side, which has no JSON parser."""
    for p, (t, q) in pp_rest().items():
        stream.write("%s\t%s\t%s\n" % (p, ",".join("%.9g" % v for v in t),
                                       ",".join("%.9g" % v for v in q)))
    return 0


def resolver(nodes, root):
    def resolve(path):
        cur = root
        for part in path.split("/"):
            nxt = None
            for c in nodes[cur].get("children", []):
                if nodes[c].get("name") == part:
                    nxt = c
                    break
            if nxt is None:
                return None, cur, part
            cur = nxt
        return cur, None, None
    return resolve


# ------------------------------------------------------------------------------------ the check
def check(path=DST):
    """Fails loudly if a PP clip path would find nothing, or if the skin was disturbed."""
    g, _ = glb_read(path)
    nodes = g["nodes"]
    root = [i for i, n in enumerate(nodes) if n.get("name") == ANIM_ROOT]
    assert len(root) == 1, "expected exactly one '%s' node, found %d" % (ANIM_ROOT, len(root))
    resolve = resolver(nodes, root[0])
    want = pp_paths()
    miss = [p for p in want if resolve(p)[0] is None]
    assert not miss, "%d PP paths unresolved, e.g. %s" % (len(miss), miss[:3])

    src, _ = glb_read(SRC)
    assert len(g["skins"]) == len(src["skins"])
    for a, b in zip(g["skins"], src["skins"]):
        assert a["joints"] == b["joints"], "skin joint list changed"
        assert a.get("inverseBindMatrices") == b.get("inverseBindMatrices"), "IBM accessor changed"
        assert a.get("skeleton") == b.get("skeleton")
    for i, n in enumerate(src["nodes"]):
        assert g["nodes"][i].get("mesh") == n.get("mesh"), "node %d changed mesh" % i
        assert g["nodes"][i].get("skin") == n.get("skin"), "node %d changed skin" % i
    seen = set()
    for n in nodes:
        for c in n.get("children", []):
            assert c not in seen, "node %d has two parents" % c
            seen.add(c)
    print("ppskel check OK: %d/%d PP paths resolve; %d skin joints intact; %d nodes"
          % (len(want) - len(miss), len(want), len(g["skins"][0]["joints"]), len(nodes)))
    return 0


# ---------------------------------------------------------------------------------- the convert
def convert():
    g, binc = glb_read(SRC)
    nodes = g["nodes"]
    orig_n = len(nodes)
    src_paths = _paths(nodes)
    skin_joints = set(g["skins"][0]["joints"])
    by = {n.get("name"): i for i, n in enumerate(nodes)}
    parent = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}

    unmatched = [k for k in RENAME if k not in by]
    assert not unmatched, "source model does not carry: %s" % unmatched
    mapping = {src_paths[by[o]]: {"pp": n, "skinJoint": by[o] in skin_joints}
               for o, n in RENAME.items()}
    for old, new in RENAME.items():
        nodes[by[old]]["name"] = new
    by = {n.get("name"): i for i, n in enumerate(nodes)}

    for keep, drop in COLLAPSE:
        ki, di = by[keep], by[drop]
        assert parent[ki] == di
        gi = parent[di]
        nodes[di]["children"] = [c for c in nodes[di].get("children", []) if c != ki]
        if not nodes[di]["children"]:
            del nodes[di]["children"]
        nodes[gi].setdefault("children", []).append(ki)
        parent[ki] = gi
        t, q, s = decompose(mul(trs(nodes[ki]), trs(nodes[di])))
        nodes[ki].pop("matrix", None)
        nodes[ki]["translation"], nodes[ki]["rotation"], nodes[ki]["scale"] = t, q, s
        nodes[di]["name"] = drop + "_unused"
    by = {n.get("name"): i for i, n in enumerate(nodes)}

    inserted = []
    for child, chain in INSERT_ABOVE:
        ci = by[child]
        pi = parent[ci]
        nodes[pi]["children"] = [c for c in nodes[pi]["children"] if c != ci]
        for nm in chain:
            ni = len(nodes)
            nodes.append({"name": nm})          # no TRS = identity = world-preserving
            nodes[pi].setdefault("children", []).append(ni)
            parent[ni] = pi
            pi = ni
            inserted.append(nm)
        nodes[pi].setdefault("children", []).append(ci)
        parent[ci] = pi
    by = {n.get("name"): i for i, n in enumerate(nodes)}

    # every PP path that still does not resolve becomes an empty leaf at the right parent.
    # ponytail: identity local TRS. PP's offsets are metres and this rig's bone space is ~1/3.1
    # of a metre (see NEXT LEVER), so a borrowed number would be a lie - and PP's clips overwrite
    # localPosition on every bone they bind anyway. Upgrade path is the same rest-pose rebind.
    resolve = resolver(nodes, by[ANIM_ROOT])
    created = []
    for p in sorted(pp_paths(), key=lambda p: p.count("/")):
        idx, par, part = resolve(p)
        if idx is not None:
            continue
        nodes.append({"name": part})
        nodes[par].setdefault("children", []).append(len(nodes) - 1)
        created.append(p)

    glb_write(DST, g, binc)
    json.dump({
        "source": os.path.basename(SRC),
        "ppRig": "extracted/GameData/prefabs/CHR_Human_Rig_Ready.json",
        "animatorRootInConverted": ANIM_ROOT + " (was _rootJoint) - PP paths start below it",
        "renames": mapping,
        "insertedIntermediates": dict(INSERT_ABOVE),
        "collapsed": [{"kept": k, "hoistedPast": d} for k, d in COLLAPSE],
        "createdEmptyPpNodes": created,
    }, open(MAP, "w", encoding="utf-8"), indent=1)

    print("nodes %d -> %d | renamed %d | inserted %d | created %d"
          % (orig_n, len(nodes), len(RENAME), len(inserted), len(created)))
    print("wrote " + DST)
    print("wrote " + MAP)
    return 0


def _paths(nodes):
    par = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}
    res = {}

    def full(i):
        if i not in res:
            nm = nodes[i].get("name", "node%d" % i)
            res[i] = nm if i not in par else full(par[i]) + "/" + nm
        return res[i]
    return {i: full(i) for i in range(len(nodes))}


# NEXT LEVER (deliberately NOT done here): binding is not looking right.
#  - this rig's bone space is ~3.1x a metre (Sketchfab .fbx 0.01 * a 2.54 node scale) and its
#    segments differ from PP's by -13%..+21% AFTER that factor is removed, so PP's metre-valued
#    localPosition curves land in the wrong unit and the wrong lengths;
#  - its rest pose is arms-straight-down where PP's is an A-pose, and bone roll differs, so the
#    full local rotations PP writes will twist limbs.
#  The single fix for both: re-bind - scale mesh POSITION and skin inverseBindMatrices into
#  metres, then replace each inverseBindMatrix with the inverse of the PP bone's REST world
#  matrix (available from CHR_Human_Rig_Ready.json). That makes PP's rest pose reproduce this
#  mesh, at which point every PP clip is correct by construction. It touches the BIN chunk,
#  which is why it is not in this pass.

if __name__ == "__main__":
    if "--rest" in sys.argv:
        sys.exit(rest_tsv())
    if "--check" in sys.argv:
        sys.exit(check())
    convert()
    sys.exit(check())

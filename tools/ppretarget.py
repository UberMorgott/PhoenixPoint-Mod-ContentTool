"""Option 3: give a foreign rig Phoenix Point's rest ORIENTATION and PP's clips, and let it keep
its OWN proportions.

`ppskel.py` made the foreign model spell PP's transform paths, so PP's curves BIND. They still did
not LOOK right, for two reasons this file fixes together:

  1. PP's generic clips write localPosition on every bone, and 95% of those curves never move -
     they restate the PP prefab's own rest offset, i.e. they PIN PP's segment lengths onto whoever
     plays them. `tools\\ClipCensus --export` drops exactly those (per curve, from the curve's own
     samples - no bone list), keeps the ones that actually travel, drops the unit scale curves, and
     leaves every rotation curve alone. What is left is a rotation-driven clip.
  2. A rotation curve is only meaningful against the rest pose it was authored on. So each bone's
     rest ROTATION here becomes PP's, while its rest TRANSLATION keeps the model's own length
     (converted to metres). The mesh is reposed into that new rest and the inverse bind matrices
     are rewritten to match, which is what keeps the model looking like itself while PP's rotations
     produce PP's poses.

The metre factor and every per-bone length come from the two rigs' own numbers - see `--check`,
which re-derives them from the WRITTEN file rather than trusting what convert() believed.

    python tools\\ppretarget.py             # convert, then check
    python tools\\ppretarget.py --check     # check the written file only
    python tools\\ppretarget.py --selftest  # check + the negative controls that arm it

WHERE THE CLIPS LIVE AT RUNTIME: in the mod's OWN bundle. They are written into this .glb as glTF
animations, so the existing creature route (`ct_project` bake -> AnimatorOverrideController, the
path `demos\\CustomCreature` already proves) bakes them like any other imported clip. No game
bundle is read, copied or repointed at runtime, and nothing is written into the game install. A
runtime rewrite is not even possible: AnimationUtility, the only way to read a shipped generic
clip's curves back, is editor-only - so the filtering has to happen offline, here.

ponytail: stdlib only, and no new runtime code in the mod at all - the whole feature is two offline
tools feeding a bake that already existed.
"""
import json
import math
import os
import struct
import sys

import ppskel
from ppskel import ANIM_ROOT, glb_read, glb_write, mul, decompose, resolver

ROOT = ppskel.ROOT
ORIGINAL = ppskel.SRC                 # the untouched download - read only, never written
SRC = ppskel.DST                      # what ppskel.py wrote: PP paths, original geometry
DST = os.path.join(ROOT, "tiffany_cox_ppfit.glb")
CLIPS = os.path.join(ROOT, "tools", "pp-clips.json")
REPORT = os.path.join(ROOT, "tools", "ppretarget-report.json")

# Tolerances, all stated rather than felt.
LENGTH_TOL = 1e-4      # relative: a converted segment may differ from the model's by 0.01%
ANGLE_TOL = 1e-5       # 1 - |dot| between the converted rest rotation and PP's
PIN_TOL = 1e-4         # metres: a translation channel this still is a length statement, not motion
UNIT_TOL = 8.7e-5      # must equal ClipCensus's UnitScale, or the two disagree about bake noise
SCATTER = (0.70, 1.45)  # per-bone length / metre factor must stay inside this, or the map is wrong


# ------------------------------------------------------------------------------ 4x4, row-vector
# Same convention as ppskel.trs: a matrix is four ROWS, a point is a row vector, and world =
# local * parentWorld. A glTF MAT4 accessor is column-major for a column-vector matrix, which is
# the same sixteen floats in the same order as this - flatten the rows and it round-trips.
IDENTITY = [[1.0, 0, 0, 0], [0, 1.0, 0, 0], [0, 0, 1.0, 0], [0, 0, 0, 1.0]]


def flat(m):
    return [m[i][j] for i in range(4) for j in range(4)]


def unflat(f):
    return [list(f[i * 4:i * 4 + 4]) for i in range(4)]


def inverse(m):
    """General 4x4 inverse by Gauss-Jordan. General, because these matrices carry a uniform scale
    (the file's bind poses run at 2.54), so the rigid-inverse shortcut would be wrong."""
    a = [list(m[i]) + list(IDENTITY[i]) for i in range(4)]
    for c in range(4):
        p = max(range(c, 4), key=lambda r: abs(a[r][c]))
        assert abs(a[p][c]) > 1e-12, "singular matrix"
        a[c], a[p] = a[p], a[c]
        d = a[c][c]
        a[c] = [v / d for v in a[c]]
        for r in range(4):
            if r == c:
                continue
            f = a[r][c]
            if f:
                a[r] = [a[r][k] - f * a[c][k] for k in range(8)]
    return [row[4:] for row in a]


def point(p, m):
    return [sum(p[i] * m[i][j] for i in range(3)) + m[3][j] for j in range(3)]


def direction(v, m):
    return [sum(v[i] * m[i][j] for i in range(3)) for j in range(3)]


def norm(v):
    return math.sqrt(sum(c * c for c in v))


def rot3(m):
    """The rotation part of a uniform-scale matrix, scale divided out."""
    s = [norm(m[i][:3]) for i in range(3)]
    return [[m[i][j] / s[i] for j in range(3)] for i in range(3)], s


def rot4(r3, t):
    return [r3[0] + [0.0], r3[1] + [0.0], r3[2] + [0.0], list(t) + [1.0]]


def transpose3(r):
    return [[r[j][i] for j in range(3)] for i in range(3)]


def mul3(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def unit(v):
    n = norm(v)
    return [c / n for c in v] if n > 1e-12 else [0.0, 1.0, 0.0]


def shortest(a, b):
    """The MINIMAL rotation taking unit a onto unit b - a swing, with no twist about either.
    Returns the row-convention 3x3 and the angle in degrees, so the repose can be reported."""
    d = max(-1.0, min(1.0, sum(a[i] * b[i] for i in range(3))))
    axis = [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]
    n = norm(axis)
    if n < 1e-9:
        if d > 0:
            return [[1.0, 0, 0], [0, 1.0, 0], [0, 0, 1.0]], 0.0
        # antiparallel: any perpendicular axis is as good as another
        axis = unit([a[1], -a[0], 0.0] if abs(a[2]) > 0.9 else [-a[1], a[0], 0.0])
        n = 1.0
    axis = [c / n for c in axis]
    angle = math.acos(d)
    s, c = math.sin(angle), math.cos(angle)
    x, y, z = axis
    # Rodrigues, transposed into the row-vector convention v * M
    return [[c + x * x * (1 - c), x * y * (1 - c) + z * s, x * z * (1 - c) - y * s],
            [y * x * (1 - c) - z * s, c + y * y * (1 - c), y * z * (1 - c) + x * s],
            [z * x * (1 - c) + y * s, z * y * (1 - c) - x * s, c + z * z * (1 - c)]], math.degrees(angle)


def fit_rotation(pairs, seed):
    """The rotation best carrying every a onto its b (Horn's quaternion method).

    A bone with ONE child says nothing about the twist around itself, so those fall back to a
    swing; a bone with two or more - the pelvis with its spine and two thighs, the head with its
    eye and mount points - pins all three axes, which is what stops a head from being fitted
    sideways. Returns None when the directions do not span enough to say."""
    if len(pairs) < 2:
        return None
    s = [[sum(w * a[i] * b[j] for a, b, w in pairs) for j in range(3)] for i in range(3)]
    n = [[s[0][0] + s[1][1] + s[2][2], s[1][2] - s[2][1], s[2][0] - s[0][2], s[0][1] - s[1][0]],
         [s[1][2] - s[2][1], s[0][0] - s[1][1] - s[2][2], s[0][1] + s[1][0], s[2][0] + s[0][2]],
         [s[2][0] - s[0][2], s[0][1] + s[1][0], -s[0][0] + s[1][1] - s[2][2], s[1][2] + s[2][1]],
         [s[0][1] - s[1][0], s[2][0] + s[0][2], s[1][2] + s[2][1], -s[0][0] - s[1][1] + s[2][2]]]
    shift = max(sum(abs(v) for v in row) for row in n)      # Gershgorin: makes the top eigenvalue
    for i in range(4):                                      # the one of largest magnitude, so plain
        n[i][i] += shift                                    # power iteration converges to it
    v = [seed[3], seed[0], seed[1], seed[2]]                # (w, x, y, z), the parent as the seed
    for _ in range(200):
        v = [sum(n[i][j] * v[j] for j in range(4)) for i in range(4)]
        length = math.sqrt(sum(c * c for c in v))
        if length < 1e-12:
            return None
        v = [c / length for c in v]
    turn = mat_of_quat([v[1], v[2], v[3], v[0]])
    # refuse a fit the data does not support: if it does not actually carry the directions over,
    # the caller is better off with the swing it can defend
    for a, b, _ in pairs:
        if sum(x * y for x, y in zip(direction(a, rot4(turn, [0, 0, 0])), b)) < 0.7:
            return None
    return turn


def quat3(r):
    """Quaternion of a 3x3 rotation, in ppskel.decompose's own convention."""
    return decompose(rot4(r, [0, 0, 0]))[1]


def mat_of_quat(q):
    x, y, z, w = q
    return [[1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w)],
            [2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w)],
            [2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y)]]


# ------------------------------------------------------- glTF <-> Unity, GlbCodec's own involution
# src\Import\GlbCodec.cs:214-251, quoted rather than reinvented: the change of basis is
# S = diag(-1, 1, 1), so a vector negates X, a ROTATION keeps X and negates Y and Z, and a matrix
# goes S*M*S - which in this row convention negates exactly the entries with one index zero.
def cv3(v):
    return [-v[0], v[1], v[2]]


def cq(q):
    return [q[0], -q[1], -q[2], q[3]]


def cmat(m):
    return [[-m[i][j] if (i == 0) != (j == 0) else m[i][j] for j in range(4)] for i in range(4)]


# --------------------------------------------------------------------------------- glTF plumbing
def parents(nodes):
    return {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}


def node_local(n):
    return unflat(n["matrix"]) if "matrix" in n else ppskel.trs(n)


def tree_world(nodes):
    """Every node's world matrix from the node TRS, walked down from the roots.

    THIS, not inverse(inverseBindMatrix), is the frame a bone actually carries its flesh in: glTF
    skins by joint WORLD * inverseBindMatrix, and a file is free to make those two anything whose
    product is right. This one does - measured: the bind pose of `BW_Hair_Root_054` sits at the
    ORIGIN with its 13716 vertices around it, and the node tree is what puts the hair on the head.
    Reading the rest out of the bind poses alone put that hair 1.4 m off the body."""
    par = parents(nodes)
    out = {}

    def w(i):
        if i not in out:
            out[i] = node_local(nodes[i]) if i not in par else mul(node_local(nodes[i]), w(par[i]))
        return out[i]
    return {i: w(i) for i in range(len(nodes))}


def accessor_bytes(g, binc, index):
    a = g["accessors"][index]
    bv = g["bufferViews"][a["bufferView"]]
    return binc, bv.get("byteOffset", 0) + a.get("byteOffset", 0), bv.get("byteStride"), a


COMPONENT = {5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2), 5123: ("H", 2), 5125: ("I", 4), 5126: ("f", 4)}
ELEMENTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def read_accessor(g, binc, index):
    _, off, stride, a = accessor_bytes(g, binc, index)
    fmt, size = COMPONENT[a["componentType"]]
    n = ELEMENTS[a["type"]]
    step = stride or size * n
    out = []
    for i in range(a["count"]):
        out.append(list(struct.unpack_from("<" + fmt * n, binc, off + i * step)))
    return out


def write_accessor(g, binc, index, values):
    """In place - the element size never changes here, so nothing moves."""
    _, off, stride, a = accessor_bytes(g, binc, index)
    fmt, size = COMPONENT[a["componentType"]]
    n = ELEMENTS[a["type"]]
    step = stride or size * n
    for i, v in enumerate(values):
        struct.pack_into("<" + fmt * n, binc, off + i * step, *v)


class Appender(object):
    """New buffer data goes on the END, so every existing bufferView keeps its offset."""

    def __init__(self, g, binc):
        self.g, self.buf = g, bytearray(binc)

    def view(self, data):
        while len(self.buf) % 4:
            self.buf.append(0)
        off = len(self.buf)
        self.buf += data
        self.g["bufferViews"].append({"buffer": 0, "byteOffset": off, "byteLength": len(data)})
        return len(self.g["bufferViews"]) - 1

    def floats(self, rows, kind):
        n = ELEMENTS[kind]
        data = bytearray()
        for r in rows:
            data += struct.pack("<" + "f" * n, *r)
        acc = {"bufferView": self.view(bytes(data)), "componentType": 5126,
               "count": len(rows), "type": kind}
        if kind == "SCALAR" and rows:
            acc["min"] = [min(r[0] for r in rows)]
            acc["max"] = [max(r[0] for r in rows)]
        self.g["accessors"].append(acc)
        return len(self.g["accessors"]) - 1

    def done(self):
        self.g["buffers"][0]["byteLength"] = len(self.buf)
        return bytes(self.buf)


# --------------------------------------------------------------------- the two rigs, side by side
def pp_world():
    """Every PP path's rest world matrix, Unity space, composed from the prefab's own locals."""
    rest = ppskel.pp_rest()
    world = {"": IDENTITY}
    for path in sorted(rest, key=lambda p: p.count("/")):
        t, q = rest[path]
        parent = path.rsplit("/", 1)[0] if "/" in path else ""
        world[path] = mul(rot4(mat_of_quat(q), t), world[parent])
    return rest, world


def frames_of(g):
    """path -> node index, for every PP path plus the animator root as ''."""
    nodes = g["nodes"]
    root = [i for i, n in enumerate(nodes) if n.get("name") == ANIM_ROOT]
    assert len(root) == 1, "expected exactly one '%s' node" % ANIM_ROOT
    resolve = resolver(nodes, root[0])
    out = {"": root[0]}
    for p in ppskel.pp_paths():
        i = resolve(p)[0]
        assert i is not None, "PP path does not resolve: " + p
        out[p] = i
    return out


def segment_scales(pp_rest_world, node_of, src_t, original):
    """How much longer each PP segment is on the MODEL than on PP, and the metre factor.

    A segment is the span between two bones the model actually HAS: PP splits an upper arm into
    Arm/Arm_Roll_1/Arm_Roll_2 where the model has one bone, so the three PP links share one measured
    span and keep PP's internal proportions inside it. Every number comes off the two rigs' own rest
    world positions - there is no table of bone lengths anywhere in this file."""
    paths = [""] + ppskel.pp_paths()
    inpp = set(paths)
    ratio, scale = {}, {}
    for p in paths:
        if not p or node_of[p] not in original:
            continue
        anc = p
        while True:
            anc = anc.rsplit("/", 1)[0] if "/" in anc else ""
            if anc in inpp and node_of[anc] in original:
                break
            if anc == "":
                break
        sm = norm([src_t[node_of[p]][i] - src_t[node_of[anc]][i] for i in range(3)])
        sp = norm([pp_rest_world[p][3][i] - pp_rest_world[anc][3][i] for i in range(3)])
        if sp <= 1e-6 or sm <= 1e-9:
            continue
        ratio[p] = sm / sp
        # every PP link inside this span, so the roll bones scale with the bone they subdivide
        q = p
        while q != anc:
            scale[q] = sm / sp
            q = q.rsplit("/", 1)[0] if "/" in q else ""
    assert ratio, "not one PP segment could be measured on this model"
    values = sorted(ratio.values())
    k = values[len(values) // 2]        # model units per metre
    for p in paths:
        scale.setdefault(p, k)
    return scale, k, ratio


# ---------------------------------------------------------------------------------- the convert
def convert():
    g, binc = glb_read(SRC)
    nodes = g["nodes"]
    skin = g["skins"][0]
    joints = skin["joints"]
    original = set(glb_read(ORIGINAL)[0]["skins"][0]["joints"])
    node_of = frames_of(g)
    rest, ppw = pp_world()

    par = parents(nodes)

    # Where every bone sits, and in which frame its flesh is expressed: the node tree (see
    # tree_world) for the first, the bind pose for the second. They are NOT each other's inverse on
    # this file and taking them to be cost a 1.4 m hair chain.
    src_world = {i: cmat(m) for i, m in tree_world(nodes).items()}
    src_rot, src_t, src_scale = {}, {}, {}
    for node, m in src_world.items():
        r, s = rot3(m)
        src_rot[node], src_t[node], src_scale[node] = r, m[3][:3], s
    uneven = max(max(s) / min(s) for s in src_scale.values())
    assert uneven < 1.001, "a bone's rest carries a non-uniform scale (%.4f), which this repose cannot honour" % uneven

    scale, k, ratio = segment_scales(ppw, node_of, src_t, original)
    spread = sorted(v / k for v in ratio.values())
    # A wrong bone map scatters EVERY segment; a genuinely different anatomy scatters a few. So the
    # guard is on the bulk, and the outliers are named in the report instead of being smoothed away.
    inside = [v for v in spread if SCATTER[0] <= v <= SCATTER[1]]
    outliers = sorted(((v / k, p) for p, v in ratio.items() if not SCATTER[0] <= v / k <= SCATTER[1]),
                      key=lambda x: -abs(math.log(x[0])))
    assert len(inside) >= 0.8 * len(spread), \
        "only %d of %d segments are within %s of one metre factor, so the bone map is wrong, not " \
        "the anatomy" % (len(inside), len(spread), SCATTER)

    # ------------------------------------------------------------------ the new rest, top down
    pp_node = {node_of[p]: p for p in node_of}
    new_local = {}
    for p, node in node_of.items():
        if not p:
            continue
        t, q = rest[p]
        f = scale[p] / k
        new_local[node] = (mat_of_quat(q), [c * f for c in t])
    root = node_of[""]

    # the Animator's own object: PP's rest for it IS the identity, and root motion binds to it
    new_local[root] = ([[1.0, 0, 0], [0, 1.0, 0], [0, 0, 1.0]], [0.0, 0.0, 0.0])
    new_world, order = {}, []

    # PASS 1 - the PP bones only. Every PP path's ancestors are PP paths too, so the set is closed
    # and this one walk places all of them; a face or hair joint is skipped here and waits for the
    # swing chain below, which is the only thing that can place it consistently.
    def walk(i, parent_world):
        order.append(i)
        if i in new_local:
            r, t = new_local[i]
            parent_world = mul(rot4(r, t), parent_world)
            new_world[i] = parent_world
        for c in nodes[i].get("children", []):
            walk(c, parent_world)

    walk(root, IDENTITY)
    new_rot = {i: rot3(m)[0] for i, m in new_world.items()}
    new_t = {i: m[3][:3] for i, m in new_world.items()}

    # ------------------------------------------------------- where the FLESH has to end up
    # NOT "the model's flesh, re-expressed in PP's bone frames". That is the trap this walked into
    # first: the two rigs orient their bones differently, so re-expressing rolls every bone by the
    # convention difference - measured, it turned the pelvis and the head 90 degrees while the
    # limbs still landed in the right places, which no bounding box would have caught.
    #
    # What the flesh actually needs is the POSE change and nothing else: arms-down becomes PP's
    # A-pose. So each bone gets the MINIMAL (swing) rotation carrying its own rest direction onto
    # its new one, chained from its parent, and the flesh rides that. The bone FRAMES stay exactly
    # PP's, which is what makes PP's rotation curves correct; the flesh's attachment to them is
    # whatever falls out, and only the rest pose's appearance ever depended on it.
    # Only a bone BOTH rigs have says anything: a face or hair joint's new place was derived FROM
    # its parent's, so pairing it against its old place is circular and it pulls every fit back
    # towards doing nothing - measured, it flipped the thigh 153 degrees onto its side.
    speaks = {node_of[p] for p in ppskel.pp_paths() if node_of[p] in original}

    def links(i):
        """Every direction this bone states, measured on BOTH rigs against the SAME node: to each
        child, stepping past the zero-length links ppskel inserted (they sit ON their parent in the
        source, so they state nothing, but what hangs below them does)."""
        out = []
        if i not in new_world:
            return out
        for c in nodes[i].get("children", []):
            n = c
            while norm([src_t[n][j] - src_t[i][j] for j in range(3)]) / k < 1e-4 \
                    and len(nodes[n].get("children", [])) == 1:
                n = nodes[n]["children"][0]
            if n not in speaks:
                continue
            a = [src_t[n][j] - src_t[i][j] for j in range(3)]
            b = [new_t[n][j] - new_t[i][j] for j in range(3)]
            if min(norm(a) / k, norm(b)) > 1e-4:
                out.append((unit(a), unit(b), norm(b)))
        return out

    # Which way the model FACES is the one rotation no single bone states. Fitted as the yaw best
    # lining every measurable bone direction up with PP's, in closed form; it seeds the chain down
    # to the pelvis, where three children pin the rest.
    num = den = 0.0
    for i in order:
        for a, b, w in links(i):
            num += w * (a[2] * b[0] - a[0] * b[2])
            den += w * (a[0] * b[0] + a[2] * b[2])
    yaw = math.atan2(num, den)
    c, s = math.cos(yaw), math.sin(yaw)
    swing_of = {root: [[c, 0.0, -s], [0.0, 1.0, 0.0], [s, 0.0, c]]}

    swung = []
    for i in order:
        if i != root:
            swing_of[i] = swing_of[par[i]]
        pairs = links(i)
        if not pairs:
            continue
        turn = fit_rotation([(a, b, w) for a, b, w in pairs], quat3(swing_of[i]))
        if turn is None:
            a = direction(pairs[0][0], rot4(swing_of[i], [0, 0, 0]))
            turn, _ = shortest(a, pairs[0][1])
            turn = mul3(swing_of[i], turn)
        before = swing_of[i]
        swing_of[i] = turn
        rel = mul3(turn, transpose3(before))
        swung.append((math.degrees(math.acos(max(-1.0, min(1.0, (rel[0][0] + rel[1][1] + rel[2][2] - 1) / 2)))),
                      nodes[i].get("name")))
    swung.sort(reverse=True)

    # PASS 2 - the bones PP does not have: face, hair, cloth, the twist bones, and the neck link
    # ppskel collapsed out of the chain. They ride their parent's SWING, and that is the whole
    # point: the repose moves their flesh by swing_of[parent], so their FRAME has to move by the
    # same rotation. Placing them by the parent's raw source-to-PP frame change instead - which is
    # the convention-laden one - left flesh and bone disagreeing by up to 180 degrees. Measured on
    # this model: hair 179.8, face 175.4, eyes 89.8, and the collapsed neck link tore the throat.
    for i in order:
        if i in new_world:
            continue
        p = par[i]
        turn = rot4(swing_of[i], [0, 0, 0])
        off = direction([(src_t[i][j] - src_t[p][j]) / k for j in range(3)], turn)
        t = [new_t[p][j] + off[j] for j in range(3)]
        r = mul3(src_rot[i], swing_of[i])
        new_world[i] = rot4(r, t)
        new_rot[i], new_t[i] = r, t
        local = mul(new_world[i], inverse(new_world[p]))
        new_local[i] = (rot3(local)[0], local[3][:3])

    # ---------------------------------------------------------------------------- repose the skin
    # UNPOSE first: a vertex is not stored in the rig's space, it is stored in whatever space its
    # bind poses undo, so its rest position is glTF's own skinning sum, v * inverseBindMatrix *
    # jointWorld. Only then can it be reposed. (Skipping this put the hair - whose bind poses are
    # authored head-local - 1.4 m off the body.)
    bind = [mul(cmat(unflat(m)), src_world[joints[s]])
            for s, m in enumerate(read_accessor(g, binc, skin["inverseBindMatrices"]))]
    binc = bytearray(binc)
    moved = weightless = 0
    for mesh in g["meshes"]:
        for prim in mesh["primitives"]:
            att = prim["attributes"]
            pos = read_accessor(g, binc, att["POSITION"])
            jnt = read_accessor(g, binc, att["JOINTS_0"])
            wgt = read_accessor(g, binc, att["WEIGHTS_0"])
            nrm = read_accessor(g, binc, att["NORMAL"]) if "NORMAL" in att else None
            tan = read_accessor(g, binc, att["TANGENT"]) if "TANGENT" in att else None
            for i in range(len(pos)):
                v = cv3(pos[i])
                n = cv3(nrm[i]) if nrm else None
                tg = cv3(tan[i][:3]) if tan else None
                total = sum(w for w in wgt[i] if w > 0)
                if total <= 0:
                    weightless += 1
                    continue
                influence = [(s, joints[s], w / total) for s, w in zip(jnt[i], wgt[i]) if w > 0]
                vw = [0.0, 0.0, 0.0]
                nw = [0.0, 0.0, 0.0]
                tw = [0.0, 0.0, 0.0]
                for slot, node, w in influence:
                    p = point(v, bind[slot])
                    for j in range(3):
                        vw[j] += w * p[j]
                    if n is not None:
                        d = direction(n, bind[slot])
                        for j in range(3):
                            nw[j] += w * d[j]
                        if tg is not None:
                            d = direction(tg, bind[slot])
                            for j in range(3):
                                tw[j] += w * d[j]
                acc = [0.0, 0.0, 0.0]
                nac = [0.0, 0.0, 0.0]
                tac = [0.0, 0.0, 0.0]
                for _, node, w in influence:
                    turn = rot4(swing_of[node], [0, 0, 0])
                    world = direction([(vw[j] - src_t[node][j]) / k for j in range(3)], turn)
                    for j in range(3):
                        acc[j] += w * (world[j] + new_t[node][j])
                    if n is not None:
                        d = direction(nw, turn)
                        for j in range(3):
                            nac[j] += w * d[j]
                        if tg is not None:
                            d = direction(tw, turn)
                            for j in range(3):
                                tac[j] += w * d[j]
                pos[i] = cv3(acc)
                moved += 1
                if n is not None:
                    length = norm(nac) or 1.0
                    nrm[i] = cv3([c / length for c in nac])
                    if tg is not None:
                        length = norm(tac) or 1.0
                        tan[i] = cv3([c / length for c in tac]) + [tan[i][3]]
            write_accessor(g, binc, att["POSITION"], pos)
            acc = g["accessors"][att["POSITION"]]
            acc["min"] = [min(p[j] for p in pos) for j in range(3)]
            acc["max"] = [max(p[j] for p in pos) for j in range(3)]
            if nrm:
                write_accessor(g, binc, att["NORMAL"], nrm)
            if tan:
                write_accessor(g, binc, att["TANGENT"], tan)

    # ------------------------------------------------ one mesh, because the importer wants one
    # GlbReader picks "the mesh a skin drives" and REFUSES a file where that is not exactly one
    # (src\Import\GlbReader.cs:245-273). This model arrives as SEVEN skinned meshes - body, eyes,
    # lashes, hair - so it would not import at all. They all ride skin 0, so the fix is free: one
    # mesh carrying all seven PRIMITIVES, which is what a submesh already is. No vertex is touched
    # and no index is rebased; the seven materials stay seven submeshes.
    prims = [p for m in g["meshes"] for p in m["primitives"]]
    holder = None
    for i, n in enumerate(nodes):
        if "mesh" not in n:
            continue
        if holder is None:
            holder, n["mesh"] = i, 0
        else:
            n.pop("mesh", None)
            n.pop("skin", None)
    # ...AND ONE MESH MEANS ONE ATTRIBUTE SET. Merging seven primitives into one Unity mesh makes
    # their attributes a single question: GlbReader.cs:691 refuses a mesh where only SOME blocks
    # carry TANGENT ("a Unity mesh needs them for every vertex or none"). This model has tangents on
    # some of its seven and not on others, so the merge is what creates the inconsistency and the
    # merge is where it is settled - by DROPPING them, which is what GlbCodec.cs:958-965 already says
    # a consumer does with a tangent-less mesh: recompute from the UVs.
    # The rule is per ATTRIBUTE and not a list of names, because TANGENT was only the first one the
    # importer refused and TEXCOORD_1 was the next: an attribute every primitive carries is kept, one
    # only some carry is dropped from all. It cannot eat POSITION or the skin weights - those are on
    # every primitive by construction, so they are never the minority case.
    dropped_attrs = []
    for attr in sorted({a for p in prims for a in p.get("attributes", {})}):
        have = sum(1 for p in prims if attr in p.get("attributes", {}))
        if have == len(prims):
            continue
        for p in prims:
            p.get("attributes", {}).pop(attr, None)
        dropped_attrs.append("%s (on %d of %d)" % (attr, have, len(prims)))
    g["meshes"] = [{"name": g["meshes"][0].get("name", "model"), "primitives": prims}]
    assert holder is not None and len(prims) <= 256, "%d primitives, past the importer's 256" % len(prims)

    # ------------------------------------------------- the joints: PP's own links join the skin
    # GlbReader.Hierarchy keeps ONLY skin joints as bones and reparents everything else onto the
    # nearest joint ancestor, so a PP link that is not a joint does not survive import at all and
    # everything below it binds nothing. They go on the END - the original 148 stay a prefix.
    added = []
    for p in [""] + ppskel.pp_paths():
        node = node_of[p]
        if node not in joints:
            joints.append(node)
            added.append(p or ANIM_ROOT)

    app = Appender(g, binc)
    rows = []
    for node in joints:
        rows.append(flat(cmat(inverse(new_world[node]))))
    skin["inverseBindMatrices"] = app.floats(rows, "MAT4")
    skin["skeleton"] = root

    # node TRS follows the same rest, so the file reads the same in a viewer as it does to the bake
    for i in order:
        n = nodes[i]
        n.pop("matrix", None)
        r, t = new_local.get(i, ([[1.0, 0, 0], [0, 1.0, 0], [0, 0, 1.0]], [0.0, 0.0, 0.0]))
        n["translation"] = cv3(t)
        n["rotation"] = cq(quat3(r))
        n["scale"] = [1.0, 1.0, 1.0]
    for i in range(len(nodes)):
        if i in order:
            continue
        nodes[i].pop("matrix", None)
        nodes[i]["translation"] = [0.0, 0.0, 0.0]
        nodes[i]["rotation"] = [0.0, 0.0, 0.0, 1.0]
        nodes[i]["scale"] = [1.0, 1.0, 1.0]

    # ------------------------------------------------------------------------------- the clips
    doc = json.load(open(CLIPS, encoding="utf-8"))
    g["animations"] = []                # the model's own take was authored on the OLD rest
    written, dropped_pins = [], []
    for clip in doc["clips"]:
        channels, samplers = [], []
        # the exporter's OWN sample instants, not frame/fps: the clip's length is its m_StopTime and
        # rounding that onto a frame grid would quietly lengthen or shorten every animation.
        assert len(clip["times"]) == clip["frames"], "clip '%s': %d times for %d frames" \
                                                     % (clip["name"], len(clip["times"]), clip["frames"])
        tacc = app.floats([[t] for t in clip["times"]], "SCALAR")
        for track in clip["tracks"]:
            node = node_of.get(track["path"])
            assert node is not None, \
                "clip '%s' drives '%s', which this rig does not have - %s is stale, re-export it" \
                % (clip["name"], track["path"], os.path.basename(CLIPS))
            if "scl" in track:
                # a genuinely animated scale is a squash the animator meant; under S = diag(-1,1,1)
                # a scale vector is unchanged, so it crosses to glTF as it stands
                acc = app.floats(track["scl"], "VEC3")
                samplers.append({"input": tacc, "interpolation": "LINEAR", "output": acc})
                channels.append({"sampler": len(samplers) - 1,
                                 "target": {"node": node, "path": "scale"}})
            if "rot" in track:
                acc = app.floats([cq(q) for q in track["rot"]], "VEC4")
                samplers.append({"input": tacc, "interpolation": "LINEAR", "output": acc})
                channels.append({"sampler": len(samplers) - 1,
                                 "target": {"node": node, "path": "rotation"}})
            if "pos" in track:
                # A kept position curve still carries PP's own rest as its DC term - PP's hip
                # HEIGHT, which is a length. Keep the MOTION and re-base it onto this model's rest,
                # scaled by the same factor its own segment got, so the walk travels without
                # PP's leg length coming back in through the back door.
                base = rest[track["path"]][0] if track["path"] in rest else [0.0, 0.0, 0.0]
                f = scale.get(track["path"], k) / k
                mine = new_local[node][1]
                values = [cv3([mine[j] + (p[j] - base[j]) * f for j in range(3)]) for p in track["pos"]]
                # ...AND A CHANNEL THAT LANDS BACK ON OUR OWN REST IS NOT A CHANNEL. The exporter's
                # "still" test is taken in PP's space against PP's rest; a curve that jitters a tenth
                # of a millimetre survives it and then re-bases onto this model's rest exactly, which
                # is a translation channel restating the rig - the very thing check() refuses.
                # Measured on the full 331-clip export: 4 such channels, all gun_point_hand, moving
                # 0.15 mm over the whole clip. Dropped here, where our rest is known exactly.
                # The predicate is check()'s own, verbatim, so the writer and the gate cannot disagree.
                at_rest = cv3(mine)
                if max(norm([v[j] - values[0][j] for j in range(3)]) for v in values) <= PIN_TOL \
                   and norm([values[0][j] - at_rest[j] for j in range(3)]) <= PIN_TOL:
                    dropped_pins.append({"clip": clip["name"], "path": track["path"]})
                    continue
                acc = app.floats(values, "VEC3")
                samplers.append({"input": tacc, "interpolation": "LINEAR", "output": acc})
                channels.append({"sampler": len(samplers) - 1,
                                 "target": {"node": node, "path": "translation"}})
        g["animations"].append({"name": clip["name"], "channels": channels, "samplers": samplers})
        written.append((clip["name"], len(channels), clip["frames"]))

    glb_write(DST, g, app.done())
    json.dump({
        "source": os.path.basename(SRC),
        "clips": os.path.basename(CLIPS),
        # The channels the writer refused, so check() asks for the same clip it was actually handed.
        "positionChannelsDroppedOnOwnRest": dropped_pins,
        "attributesDroppedOnMerge": dropped_attrs,
        "metreFactorModelUnitsPerMetre": k,
        "perBoneLengthOverMetreFactor": {"min": spread[0], "median": spread[len(spread) // 2],
                                         "max": spread[-1], "segments": len(spread)},
        "perBoneLengthOutliers": [{"bone": p.split("/")[-1], "times": v} for v, p in outliers],
                "facingYawDegrees": math.degrees(yaw),
        "reposeDegreesAtJoint": [{"bone": b, "degrees": d} for d, b in swung[:12]],
        "verticesReposed": moved,
        "verticesWithNoWeight": weightless,
        "jointsBefore": len(original),
        "jointsAfter": len(joints),
        "jointsAdded": added,
        "animations": [{"name": n, "channels": c, "frames": f} for n, c, f in written],
    }, open(REPORT, "w", encoding="utf-8"), indent=1)

    print("metre factor %.6g model units per metre (per-bone length/factor %.3f..%.3f over %d segments)"
          % (k, spread[0], spread[-1], len(spread)))
    print("%d of %d segments within %s of it; outliers %s"
          % (len(inside), len(spread), SCATTER,
             ", ".join("%s x%.2f" % (p.split("/")[-1], v) for v, p in outliers[:6]) or "none"))
    print("facing yaw %.1f deg | repose at a joint: median %.1f deg, worst %s"
          % (math.degrees(yaw), swung[len(swung) // 2][0],
             ", ".join("%s %.0f" % (b, d) for d, b in swung[:5])))
    print("reposed %d vertices (%d had no weight)" % (moved, weightless))
    print("skin joints %d -> %d (added %d PP-only links)" % (len(original), len(joints), len(added)))
    print("clips %d, %d channels, %d frames total; e.g. %s"
          % (len(written), sum(w[1] for w in written), sum(w[2] for w in written),
             ", ".join("%s %dch/%df" % w for w in written[:3])))
    print("wrote " + DST)
    print("wrote " + REPORT)
    return 0


# ------------------------------------------------------------------------------------- the check
def check(path=DST, mutate=None):
    """Re-derives everything from the WRITTEN file, the way the importer would, and refuses it if
    any of option 3's four promises is broken. `mutate` is for the negative controls: it gets the
    parsed document before a single assertion runs."""
    g, binc = glb_read(path)
    binc = bytearray(binc)
    if mutate:
        mutate(g, binc)
    nodes, skin = g["nodes"], g["skins"][0]
    joints = skin["joints"]
    node_of = frames_of(g)
    rest, ppw = pp_world()

    src, sbin = glb_read(ORIGINAL)
    assert joints[:len(src["skins"][0]["joints"])] == src["skins"][0]["joints"], \
        "the original skin joints are no longer an unchanged prefix"
    for p in ppskel.pp_paths():
        assert node_of[p] in joints, "PP link '%s' is not a skin joint, so it will not survive import" % p
    skinned = {n["mesh"] for n in nodes if "mesh" in n and "skin" in n}
    assert len(skinned) == 1, \
        "%d skinned meshes - GlbReader picks 'the mesh a skin drives' and refuses anything but one" % len(skinned)

    # rest world exactly as GlbReader would read it: straight out of the bind poses
    ibm = read_accessor(g, binc, skin["inverseBindMatrices"])
    world = {}
    for slot, node in enumerate(joints):
        world[node] = cmat(inverse(unflat(ibm[slot])))
    par = parents(nodes)

    # 1. rest ORIENTATION is PP's
    worst_angle, worst_bone = 0.0, None
    for p in ppskel.pp_paths():
        node = node_of[p]
        parent = par[node]
        local = mul(world[node], inverse(world[parent])) if parent in world else world[node]
        q = quat3(rot3(local)[0])
        want = rest[p][1]
        dot = abs(sum(q[i] * want[i] for i in range(4)))
        if 1.0 - dot > worst_angle:
            worst_angle, worst_bone = 1.0 - dot, p
    assert worst_angle <= ANGLE_TOL, \
        "rest rotation is not PP's on '%s': 1-|dot| = %.3g > %.3g" % (worst_bone, worst_angle, ANGLE_TOL)

    # 2. segment LENGTHS are the model's own, and one metre factor explains all of them
    original = set(src["skins"][0]["joints"])
    # the UNTOUCHED download's own rest, read the way glTF skins: the node tree
    src_t = {i: cmat(m)[3][:3] for i, m in tree_world(src["nodes"]).items()}
    factors, worst_len, worst_len_bone = [], 0.0, None
    paths = [""] + ppskel.pp_paths()
    inpp = set(paths)
    for p in paths:
        if not p or node_of[p] not in original:
            continue
        anc = p
        while True:
            anc = anc.rsplit("/", 1)[0] if "/" in anc else ""
            if (anc in inpp and node_of[anc] in original) or anc == "":
                break
        sm = norm([src_t[node_of[p]][i] - src_t[node_of[anc]][i] for i in range(3)])
        sn = norm([world[node_of[p]][3][i] - world[node_of[anc]][3][i] for i in range(3)])
        if sm <= 1e-9 or sn <= 1e-9:
            continue
        factors.append(sm / sn)
    assert factors, "no segment could be measured"
    factors.sort()
    k = factors[len(factors) // 2]
    for f in factors:
        e = abs(f - k) / k
        if e > worst_len:
            worst_len = e
    assert worst_len <= LENGTH_TOL, \
        "a converted segment is not the model's own length: one bone is off by %.3g relative " \
        "(a single metre factor no longer explains every segment)" % worst_len

    # 3. the clips are rotation-driven: no position channel restates a rest offset. On the DISTANCE,
    #    which is what "on the rest offset" means - a per-component millimetre lets a bone sit
    #    sqrt(3) mm away and still pass.
    pinned, unit_scale, rots, poss, scls = [], [], 0, 0, 0
    for anim in g.get("animations", []):
        for ch in anim["channels"]:
            values = read_accessor(g, binc, anim["samplers"][ch["sampler"]]["output"])
            node = ch["target"]["node"]
            parent = par.get(node)
            local = mul(world[node], inverse(world[parent])) if parent in world else world[node]
            if ch["target"]["path"] == "rotation":
                rots += 1
            elif ch["target"]["path"] == "scale":
                scls += 1
                # a unit scale channel means the "drop the bake's unit scale" rule leaked
                if max(norm([v[j] - 1.0 for j in range(3)]) for v in values) <= UNIT_TOL:
                    unit_scale.append((anim["name"], nodes[node].get("name")))
            elif ch["target"]["path"] == "translation":
                poss += 1
                at_rest = cv3(local[3][:3])
                still = max(norm([v[j] - values[0][j] for j in range(3)]) for v in values) <= PIN_TOL
                if still and norm([values[0][j] - at_rest[j] for j in range(3)]) <= PIN_TOL:
                    pinned.append((anim["name"], nodes[node].get("name")))
    assert not pinned, \
        "%d position channel(s) only restate the rest offset, which pins a segment length: %s" \
        % (len(pinned), pinned[:3])
    assert not unit_scale, \
        "%d scale channel(s) are unit, which is bake noise the export must drop: %s" \
        % (len(unit_scale), unit_scale[:3])

    # 4. every exported clip arrived WHOLE, with its rotations unaltered, its animated scale carried
    #    and its own sample instants - a clip that lost curves on the way is the failure that looks
    #    exactly like a working one until a limb does not move.
    assert "" in rest, "the PP rest table has no row for the Animator's own empty path, so root motion is unbindable"
    doc = json.load(open(CLIPS, encoding="utf-8"))
    want = {c["name"]: c for c in doc["clips"]}
    # The one deliberate omission, read off the writer's own report rather than assumed: a translation
    # channel that re-based exactly onto THIS model's rest says nothing and would trip the pinning
    # assert above. Named per (clip, bone path) so it can only excuse the channels really dropped.
    try:
        dropped = {(d["clip"], d["path"])
                   for d in json.load(open(REPORT, encoding="utf-8"))
                             .get("positionChannelsDroppedOnOwnRest", [])}
    except (IOError, ValueError):
        dropped = set()
    assert len(g.get("animations", [])) == len(want), \
        "%d animation(s) in the file for %d exported clip(s)" % (len(g.get("animations", [])), len(want))
    for anim in g.get("animations", []):
        clip = want.get(anim["name"])
        assert clip, "clip '%s' is not in %s" % (anim["name"], CLIPS)
        by_node = {"rotation": {}, "translation": {}, "scale": {}}
        for t in clip["tracks"]:
            for key, kind in (("rot", "rotation"), ("pos", "translation"), ("scl", "scale")):
                if key in t and not (kind == "translation" and (anim["name"], t["path"]) in dropped):
                    by_node[kind][node_of[t["path"]]] = t[key]
        seen = {"rotation": 0, "translation": 0, "scale": 0}
        for ch in anim["channels"]:
            kind = ch["target"]["path"]
            seen[kind] += 1
            sampler = anim["samplers"][ch["sampler"]]
            times = [t[0] for t in read_accessor(g, binc, sampler["input"])]
            assert len(times) == clip["frames"] and \
                max(abs(a - b) for a, b in zip(times, clip["times"])) <= 1e-6, \
                "clip '%s': the .glb's sample instants are not the ones it was sampled at" % anim["name"]
            values = read_accessor(g, binc, sampler["output"])
            source = by_node[kind][ch["target"]["node"]]
            assert len(values) == len(source), "clip '%s': frame count changed" % anim["name"]
            if kind != "rotation":
                continue
            for f, (a, b) in enumerate(zip(values, source)):
                b = cq(b)
                assert max(abs(a[i] - b[i]) for i in range(4)) <= 1e-6, \
                    "clip '%s' frame %d: a rotation curve was altered" % (anim["name"], f)
        for kind in seen:
            assert seen[kind] == len(by_node[kind]), \
                "clip '%s': %d of %d %s curve(s) survived" % (anim["name"], seen[kind], len(by_node[kind]), kind)
        assert seen["rotation"] == clip["rot"], \
            "clip '%s': %d rotation channels for %d shipped rotation curves" % (anim["name"], seen["rotation"], clip["rot"])
        assert clip["frames"] > 1 or clip["times"][-1] == 0.0, "clip '%s': collapsed to one frame" % anim["name"]

    # 5. a bone NO CLIP DRIVES carries its flesh only through its parent, so its FRAME and its
    #    FLESH must have moved by the same rotation. When they disagree the vertices land rotated
    #    off the bone that carries them, which reads as a limb turned the wrong way and, where that
    #    flesh meets a driven bone's, as a torn seam. Measured from the two files, not asserted.
    driven = {ch["target"]["node"] for anim in g.get("animations", []) for ch in anim["channels"]}
    src_world = {i: cmat(m) for i, m in tree_world(src["nodes"]).items()}
    sibm = read_accessor(src, sbin, src["skins"][0]["inverseBindMatrices"])
    bind = [mul(cmat(unflat(m)), src_world[src["skins"][0]["joints"][s]]) for s, m in enumerate(sibm)]
    flesh = {}
    sprims = [p for m in src["meshes"] for p in m["primitives"]]
    nprims = [p for m in g["meshes"] for p in m["primitives"]]
    assert len(sprims) == len(nprims), "primitive count changed, so vertices cannot be paired"
    for sp, np_ in zip(sprims, nprims):
        spos = read_accessor(src, sbin, sp["attributes"]["POSITION"])
        npos = read_accessor(g, binc, np_["attributes"]["POSITION"])
        jnt = read_accessor(g, binc, np_["attributes"]["JOINTS_0"])
        wgt = read_accessor(g, binc, np_["attributes"]["WEIGHTS_0"])
        for i in range(len(npos)):
            if wgt[i][0] < 0.9:
                continue
            slot = jnt[i][0]
            node = joints[slot]
            if node in driven or len(flesh.setdefault(node, [])) >= 500:
                continue
            # both offsets in UNITY space, or the comparison measures the axis flip instead
            a = point(cv3(spos[i]), bind[slot])
            a = [a[j] - src_world[node][3][j] for j in range(3)]
            b = [cv3(npos[i])[j] - world[node][3][j] for j in range(3)]
            if norm(a) > 1e-6 and norm(b) > 1e-9:
                flesh[node].append((unit(a), unit(b), 1.0))
    orphans, adrift = [], []
    for node, pairs in flesh.items():
        up = node
        while up in par and up not in driven:
            up = par[up]
        if up not in driven:
            orphans.append(nodes[node].get("name"))
        if len(pairs) < 20:
            continue
        turn = fit_rotation(pairs, [0.0, 0.0, 0.0, 1.0])
        if turn is None:
            continue
        frame = mul3(transpose3(rot3(src_world[node])[0]), rot3(world[node])[0])
        rel = mul3(turn, transpose3(frame))
        off = math.degrees(math.acos(max(-1.0, min(1.0, (rel[0][0] + rel[1][1] + rel[2][2] - 1) / 2))))
        if off > 2.0:
            adrift.append((nodes[node].get("name"), off))
    assert not orphans, "%d weighted bone(s) no clip drives hang off no driven bone at all: %s" \
                        % (len(orphans), orphans[:5])
    adrift.sort(key=lambda x: -x[1])
    assert not adrift, \
        "%d undriven weighted bone(s) carry flesh rotated off their own rest frame - the vertices " \
        "will tear away from the bones around them: %s" \
        % (len(adrift), ["%s %.0f deg" % x for x in adrift[:5]])

    height = max(world[n][3][1] for n in joints) - min(world[n][3][1] for n in joints)
    print("ppretarget check OK: rest rotation == PP's (worst 1-|dot| %.2g), every segment the "
          "model's own (worst %.2g relative, metre factor %.6g), %d clip(s) whole - %d rotation "
          "channel(s) byte-equal to the shipped clips on their own sample instants, %d position and "
          "%d scale channel(s), none pinning and none unit; %d undriven weighted bone(s) all "
          "carrying their flesh square on their own frame; rig %.3f m tall, %d joints"
          % (worst_angle, worst_len, k, len(want), rots, poss, scls, len(flesh), height, len(joints)))
    return 0


# ------------------------------------------------------------- the negative controls for the check
def selftest():
    check()

    def bend_a_rest_rotation(g, binc):
        """Turn ONE bone's bind pose by 5 degrees. Promise 1 must catch it."""
        skin = g["skins"][0]
        ibm = read_accessor(g, binc, skin["inverseBindMatrices"])
        node = frames_of(g)["BaseManReference/Root/Spine_1"]
        slot = skin["joints"].index(node)
        a = math.radians(5.0)
        turn = [[1, 0, 0, 0], [0, math.cos(a), math.sin(a), 0], [0, -math.sin(a), math.cos(a), 0], [0, 0, 0, 1]]
        ibm[slot] = flat(mul(unflat(ibm[slot]), turn))
        write_accessor(g, binc, skin["inverseBindMatrices"], ibm)

    def put_a_pinning_curve_back(g, binc):
        """Give a bone back the constant localPosition curve the export dropped. Promise 3 must
        catch it - this is the exact curve that would impose PP's segment length."""
        node_of = frames_of(g)
        node = node_of["BaseManReference/Root/Spine_1/Spine_2/Spine_3/Chest/L.Shoulder/L.Arm"]
        anim = g["animations"][0]
        frames = len(read_accessor(g, binc, anim["samplers"][0]["input"]))
        par = parents(g["nodes"])
        skin = g["skins"][0]
        ibm = read_accessor(g, binc, skin["inverseBindMatrices"])
        world = {n: cmat(inverse(unflat(ibm[s]))) for s, n in enumerate(skin["joints"])}
        local = mul(world[node], inverse(world[par[node]]))
        at_rest = cv3(local[3][:3])
        app = Appender(g, bytes(binc))
        acc = app.floats([at_rest] * frames, "VEC3")
        anim["samplers"].append({"input": anim["samplers"][0]["input"], "interpolation": "LINEAR",
                                 "output": acc})
        anim["channels"].append({"sampler": len(anim["samplers"]) - 1,
                                 "target": {"node": node, "path": "translation"}})
        binc[:] = app.done()

    def drop_one_curve(g, binc):
        """Take one rotation channel out of one clip - the shape a half-decoded clip has. Nothing
        about the file looks wrong afterwards; only the count does."""
        anim = g["animations"][0]
        for i, ch in enumerate(anim["channels"]):
            if ch["target"]["path"] == "rotation":
                del anim["channels"][i]
                return

    def add_a_unit_scale_curve(g, binc):
        """Give a bone back the unit scale curve the export drops. It animates nothing and costs a
        curve on every bone of every clip."""
        anim = g["animations"][0]
        frames = len(read_accessor(g, binc, anim["samplers"][0]["input"]))
        app = Appender(g, bytes(binc))
        acc = app.floats([[1.0, 1.0, 1.0]] * frames, "VEC3")
        anim["samplers"].append({"input": anim["samplers"][0]["input"], "interpolation": "LINEAR",
                                 "output": acc})
        anim["channels"].append({"sampler": len(anim["samplers"]) - 1,
                                 "target": {"node": anim["channels"][0]["target"]["node"], "path": "scale"}})
        binc[:] = app.done()

    def turn_an_undriven_bone(g, binc):
        """Twist ONE hair bone's rest frame 20 degrees away from the flesh it carries - the shape
        every misplaced non-PP bone has, and the one that tore the throat open."""
        skin = g["skins"][0]
        ibm = read_accessor(g, binc, skin["inverseBindMatrices"])
        slot = skin["joints"].index([i for i, n in enumerate(g["nodes"])
                                     if n.get("name") == "BW_Hair_Root_054"][0])
        a = math.radians(20.0)
        turn = [[math.cos(a), 0, -math.sin(a), 0], [0, 1, 0, 0], [math.sin(a), 0, math.cos(a), 0], [0, 0, 0, 1]]
        ibm[slot] = flat(mul(unflat(ibm[slot]), turn))
        write_accessor(g, binc, skin["inverseBindMatrices"], ibm)

    for what, mutate in (("an undriven bone's frame turned 20 degrees off its flesh", turn_an_undriven_bone),
                         ("a rest rotation bent by 5 degrees", bend_a_rest_rotation),
                         ("one pinning position curve left in", put_a_pinning_curve_back),
                         ("one rotation curve missing from a clip", drop_one_curve),
                         ("a unit scale curve left in", add_a_unit_scale_curve)):
        try:
            check(mutate=mutate)
        except AssertionError as e:
            print("negative control RED as it must be (%s): %s" % (what, str(e).split(";")[0][:110]))
            continue
        raise AssertionError("negative control stayed GREEN with " + what + " - the check is asleep")
    print("ppretarget selftest OK: the check passes the real file and fails every control")
    return 0


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        sys.exit(selftest())
    if "--check" in sys.argv:
        sys.exit(check())
    convert()
    sys.exit(check())

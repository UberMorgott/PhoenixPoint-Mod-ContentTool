#!/usr/bin/env python3
"""
Turn the Sketchfab "Nerf Gun" download into a file Phoenix Point can carry in a soldier's hand.

WHY THIS EXISTS. The download is 211 120 triangles and 119 511 vertices across 11 meshes, with six
embedded 1024x1024 PNGs (10 112 412 B). Phoenix Point's own weapons are two orders of magnitude
lighter - the CC0 sniper this demo already ships is 8 728 triangles - and the bake writes every
texture UNCOMPRESSED RGBA32 with a single mip (BundleBaker.FillTexture2D), so three 1024 atlases
alone would be 12 MB of VRAM for a pistol that is a few hundred pixels tall on screen. Nothing is
wrong with the model; it is a render asset being asked to be a game asset.

WHAT IT DOES, in one pass, so the step re-runs from the original at any time:

  1. bakes the node hierarchy (Sketchfab's root carries the Z-up -> Y-up matrix) into the vertices;
  2. turns the gun onto Phoenix Point's axes - barrel down +Z, +Y up - and centres it, so `fit: auto`
     is left with nothing to do but scale, and tools\\render_icon.py sees the side view it expects;
  3. DECIMATES by vertex clustering: a grid over the model, one representative vertex per occupied
     cell, triangles whose corners land in three distinct cells. The cell size is bisected until the
     triangle count lands on the target. Clustering rather than quadric edge collapse because the
     target is a 25x reduction, where the cell grid IS the shape and the extra machinery buys
     nothing you can see on a gun held at arm's length;
  4. keeps the three materials as three meshes, so their UV islands never average into each other,
     and MeshMerge.Static rejoins them at bake as one mesh with three submeshes;
  5. downsamples the three base-colour atlases to 512 and drops the metallic-roughness, normal and
     clearcoat maps, because the bake binds exactly one texture (`_MainTex`, ProjectBake.cs:98-100).

    python reduce_nerf.py [nerf_gun.glb] [out.glb] [target-triangles] [texture-size]

Stdlib only.
"""
import json
import math
import os
import struct
import sys
import zlib

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tools"))
from glbfit import agreement, reverse_triangles, write_glb
from pngopt import png_read, png_write

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_IN = os.path.join(HERE, "source", "nerf_gun.glb")
DEFAULT_OUT = os.path.join(os.path.dirname(HERE), "Content", "Models", "nerf.glb")
TARGET_TRIS = 8000
TEX_SIZE = 512

CHUNK_JSON = 0x4E4F534A
CHUNK_BIN = 0x004E4942


# --------------------------------------------------------------------------- glTF in

def read_glb(path):
    data = open(path, "rb").read()
    magic, version, _ = struct.unpack_from("<III", data, 0)
    assert magic == 0x46546C67 and version == 2, "not a GLB2 file: %s" % path
    at, js, blob = 12, None, None
    while at < len(data):
        length, kind = struct.unpack_from("<II", data, at)
        chunk = data[at + 8: at + 8 + length]
        if kind == CHUNK_JSON:
            js = json.loads(chunk.decode("utf-8"))
        elif kind == CHUNK_BIN:
            blob = chunk
        at += 8 + length + (-length % 4)
    assert js is not None and blob is not None, "GLB is missing its JSON or BIN chunk"
    return js, blob, len(data)


_COMPONENT = {5120: ("<b", 1), 5121: ("<B", 1), 5122: ("<h", 2),
              5123: ("<H", 2), 5125: ("<I", 4), 5126: ("<f", 4)}
_COUNT = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def accessor(gltf, blob, index):
    acc = gltf["accessors"][index]
    view = gltf["bufferViews"][acc["bufferView"]]
    fmt, width = _COMPONENT[acc["componentType"]]
    n = _COUNT[acc["type"]]
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = view.get("byteStride") or n * width
    return [tuple(struct.unpack_from(fmt, blob, start + i * stride + k * width)[0] for k in range(n))
            for i in range(acc["count"])]


def world_matrices(gltf):
    """Every mesh node's world matrix, composed down the scene graph. Column-major, as glTF stores."""
    nodes = gltf["nodes"]
    out = {}

    def local(node):
        if "matrix" in node:
            return list(node["matrix"])
        m = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]
        if "rotation" in node:
            x, y, z, w = node["rotation"]
            m = [1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w), 0,
                 2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w), 0,
                 2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y), 0,
                 0, 0, 0, 1]
        if "scale" in node:
            for c in range(3):
                for r in range(3):
                    m[c * 4 + r] *= node["scale"][c]
        if "translation" in node:
            m[12], m[13], m[14] = node["translation"]
        return m

    def mul(a, b):
        return [sum(a[k * 4 + r] * b[c * 4 + k] for k in range(4)) for c in range(4) for r in range(4)]

    def walk(index, parent):
        node = nodes[index]
        here = mul(parent, local(node))
        if "mesh" in node:
            out[index] = here
        for kid in node.get("children", []):
            walk(kid, here)

    ident = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]
    for root in gltf["scenes"][gltf.get("scene", 0)]["nodes"]:
        walk(root, ident)
    return out


def apply(m, v, point):
    w = 1.0 if point else 0.0
    return tuple(m[0 * 4 + r] * v[0] + m[1 * 4 + r] * v[1] + m[2 * 4 + r] * v[2] + m[3 * 4 + r] * w
                 for r in range(3))


def to_unity(v):
    """
    glTF world (gun along X, +Y up, +Z across) -> Unity (barrel +Z, +Y up, +X across).

        -X (muzzle) -> +Z        +Y (up) -> +Y        +Z (across) -> +X

    det = +1, so this is a rotation and the gun is not mirrored. The MUZZLE is the -X end: the
    source box runs x = -4.53 .. 10.56 and the short end is the barrel, which is the one bit a
    bounding box cannot supply (FitBox.RotationToZ calls it "flip"). Baking it in here rather than
    writing "flip": true in the manifest keeps the rendered icon and the gun in the hand pointing
    the same way - render_icon.py draws +Z to the right, as every shipped weapon icon does.
    """
    x, y, z = v
    return (z, y, -x)


# --------------------------------------------------------------------------- textures

def downsample(data, size):
    """Box-average an atlas down to size x size. Integer factor only, which 1024 -> 512 is."""
    w, h, bpp, pix = png_read(data)
    assert w % size == 0 and h % size == 0, "%dx%d does not divide by %d" % (w, h, size)
    fx, fy = w // size, h // size
    n = fx * fy
    out = bytearray(size * size * 3)
    for y in range(size):
        for x in range(size):
            r = g = b = 0
            for sy in range(fy):
                row = ((y * fy + sy) * w + x * fx) * bpp
                for sx in range(fx):
                    o = row + sx * bpp
                    r += pix[o]
                    g += pix[o + 1]
                    b += pix[o + 2]
            o = (y * size + x) * 3
            out[o] = r // n
            out[o + 1] = g // n
            out[o + 2] = b // n
    # pngopt chooses a filter per row instead of writing filter 0 everywhere: a reversible
    # prediction step, so the pixels are bit-identical, and it takes 64 KB off the finished .glb.
    return png_write(size, size, 3, bytes(out))


# --------------------------------------------------------------------------- decimation

def cluster(verts, tris, cell, lo):
    """
    One vertex per occupied grid cell (Rossignac-Borrel), keyed by cell AND material so two
    materials sharing a cell keep their own UVs. Returns (new verts, new tris) with degenerate and
    duplicate faces dropped.
    """
    ids = {}
    order = []
    of = []
    for p, _n, _uv, mat in verts:
        key = (int(math.floor((p[0] - lo[0]) / cell)),
               int(math.floor((p[1] - lo[1]) / cell)),
               int(math.floor((p[2] - lo[2]) / cell)), mat)
        at = ids.get(key)
        if at is None:
            at = ids[key] = len(order)
            order.append(key)
        of.append(at)

    sums = [[0.0] * 8 for _ in order]
    for i, (p, n, uv, _m) in enumerate(verts):
        s = sums[of[i]]
        s[0] += p[0]; s[1] += p[1]; s[2] += p[2]
        s[3] += n[0]; s[4] += n[1]; s[5] += n[2]
        s[6] += uv[0]; s[7] += uv[1]
    counts = [0] * len(order)
    for i in range(len(verts)):
        counts[of[i]] += 1

    out_verts = []
    for k, s in enumerate(sums):
        c = float(counts[k])
        nl = math.sqrt(s[3] * s[3] + s[4] * s[4] + s[5] * s[5]) or 1.0
        out_verts.append(((s[0] / c, s[1] / c, s[2] / c),
                          (s[3] / nl, s[4] / nl, s[5] / nl),
                          (s[6] / c, s[7] / c),
                          order[k][3]))

    seen = set()
    out_tris = []
    for a, b, c in tris:
        ia, ib, ic = of[a], of[b], of[c]
        if ia == ib or ib == ic or ia == ic:
            continue
        key = (ia, ib, ic) if ia < ib and ia < ic else (ib, ic, ia) if ib < ic else (ic, ia, ib)
        if key in seen:
            continue
        seen.add(key)
        # Moving three corners onto three cell centres can turn a sliver inside out, and a face
        # whose winding disagrees with its own normals is invisible under back-face culling. The
        # averaged normals are the surviving evidence of which way the surface faced, so re-orient
        # against them rather than trusting the source winding through the collapse.
        pa, pb, pc = out_verts[ia][0], out_verts[ib][0], out_verts[ic][0]
        u = (pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2])
        v = (pc[0] - pa[0], pc[1] - pa[1], pc[2] - pa[2])
        f = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
        n = tuple(sum(out_verts[k][1][i] for k in (ia, ib, ic)) for i in range(3))
        if f[0] * n[0] + f[1] * n[1] + f[2] * n[2] < 0.0:
            ib, ic = ic, ib
        out_tris.append((ia, ib, ic))
    return out_verts, out_tris


def decimate(verts, tris, target):
    """Bisect the cell size until the triangle count lands on the target. Coarser cell = fewer."""
    lo = [min(v[0][i] for v in verts) for i in range(3)]
    hi = [max(v[0][i] for v in verts) for i in range(3)]
    span = max(hi[i] - lo[i] for i in range(3))
    small, big = span / 4096.0, span
    best = None
    for _ in range(24):
        cell = math.sqrt(small * big)
        nv, nt = cluster(verts, tris, cell, lo)
        if best is None or abs(len(nt) - target) < abs(len(best[1]) - target):
            best = (nv, nt, cell)
        if len(nt) > target:
            small = cell
        else:
            big = cell
        if abs(len(nt) - target) <= target * 0.05:
            break
    return best


# --------------------------------------------------------------------------- write

def build_glb(groups, images, out_path):
    """Three meshes under three nodes, one material each - the shape MeshMerge.Static expects."""
    blob = bytearray()
    views = []
    accs = []

    def add(values, fmt, width, comp, kind, minmax=False):
        while len(blob) % 4:
            blob.append(0)
        start = len(blob)
        for v in values:
            for part in (v if isinstance(v, (tuple, list)) else (v,)):
                blob.extend(struct.pack(fmt, part))
        views.append({"buffer": 0, "byteOffset": start, "byteLength": len(blob) - start})
        acc = {"bufferView": len(views) - 1, "componentType": comp, "count": len(values), "type": kind}
        if minmax:
            n = len(values[0])
            acc["min"] = [min(v[i] for v in values) for i in range(n)]
            acc["max"] = [max(v[i] for v in values) for i in range(n)]
        accs.append(acc)
        return len(accs) - 1

    meshes, nodes = [], []
    for mat, (pos, nrm, uv, idx) in sorted(groups.items()):
        prim = {"attributes": {"POSITION": add(pos, "<f", 4, 5126, "VEC3", True),
                               "NORMAL": add(nrm, "<f", 4, 5126, "VEC3"),
                               "TEXCOORD_0": add(uv, "<f", 4, 5126, "VEC2")},
                "indices": add(idx, "<I", 4, 5125, "SCALAR"),
                "material": mat, "mode": 4}
        meshes.append({"name": "nerf_%d" % mat, "primitives": [prim]})
        nodes.append({"mesh": len(meshes) - 1, "name": "nerf_%d" % mat})

    image_views = []
    for png in images:
        while len(blob) % 4:
            blob.append(0)
        start = len(blob)
        blob.extend(png)
        views.append({"buffer": 0, "byteOffset": start, "byteLength": len(png) - 0})
        image_views.append(len(views) - 1)

    gltf = {
        "asset": {"version": "2.0", "generator": "ContentTool demos/WeaponAdd/tools/reduce_nerf.py",
                  "extras": {"title": "Nerf Gun", "author": "Paulo.liv (https://sketchfab.com/Reivilodius)",
                             "license": "CC-BY-4.0 (http://creativecommons.org/licenses/by/4.0/)",
                             "source": "https://sketchfab.com/3d-models/nerf-gun-ba0ad4ac188147548e1574c7fe4ea87b"}},
        "scene": 0,
        "scenes": [{"nodes": list(range(len(nodes)))}],
        "nodes": nodes,
        "meshes": meshes,
        "materials": [{"name": "nerf_%d" % i,
                       "pbrMetallicRoughness": {"baseColorTexture": {"index": i},
                                                "metallicFactor": 0.0, "roughnessFactor": 0.5}}
                      for i in range(len(images))],
        "textures": [{"source": i} for i in range(len(images))],
        "images": [{"mimeType": "image/png", "bufferView": v} for v in image_views],
        "accessors": accs,
        "bufferViews": views,
    }
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    return write_glb(gltf, blob, out_path)


def main(src_path, out_path, target=TARGET_TRIS, tex=TEX_SIZE):
    gltf, blob, src_bytes = read_glb(src_path)
    worlds = world_matrices(gltf)

    # --- every primitive, in glTF WORLD space, tagged with its material.
    verts, tris = [], []
    src_tris = 0
    for node_index, m in sorted(worlds.items()):
        mesh = gltf["meshes"][gltf["nodes"][node_index]["mesh"]]
        for prim in mesh["primitives"]:
            assert prim.get("mode", 4) == 4, "only triangle primitives are handled"
            pos = accessor(gltf, blob, prim["attributes"]["POSITION"])
            nrm = accessor(gltf, blob, prim["attributes"]["NORMAL"])
            uv = accessor(gltf, blob, prim["attributes"]["TEXCOORD_0"])
            idx = [i[0] for i in accessor(gltf, blob, prim["indices"])]
            mat = prim["material"]
            base = len(verts)
            for i in range(len(pos)):
                verts.append((to_unity(apply(m, pos[i], True)),
                              to_unity(apply(m, nrm[i], False)), uv[i], mat))
            for t in range(0, len(idx) - 2, 3):
                tris.append((base + idx[t], base + idx[t + 1], base + idx[t + 2]))
            src_tris += len(idx) // 3

    src_agree = agreement([v[0] for v in verts], [v[1] for v in verts],
                          [i for t in tris for i in t])

    # --- decimate, then centre what survived. Centring last so the box is the SHIPPED box.
    kept, ktris, cell = decimate(verts, tris, target)
    lo = [min(v[0][i] for v in kept) for i in range(3)]
    hi = [max(v[0][i] for v in kept) for i in range(3)]
    ctr = [(lo[i] + hi[i]) / 2.0 for i in range(3)]
    kept = [((p[0] - ctr[0], p[1] - ctr[1], p[2] - ctr[2]), n, uv, mat) for p, n, uv, mat in kept]
    size = [hi[i] - lo[i] for i in range(3)]
    assert max(range(3), key=lambda i: size[i]) == 2, (
        "the barrel must end up on +Z so `fit: auto` only has to scale; long axis is %d"
        % max(range(3), key=lambda i: size[i]))

    # --- the winding arm glbfit exists for: the reader reverses every triangle unconditionally, so
    #     the file must carry the reversal pre-applied or the gun renders inside-out.
    flat = [i for t in ktris for i in t]
    out_idx = reverse_triangles(flat)
    got_agree = agreement([v[0] for v in kept], [v[1] for v in kept], reverse_triangles(out_idx))
    assert got_agree > 0.9 and abs(got_agree - src_agree) < 0.05, (
        "winding/normal agreement moved: source %.4f -> decimated %.4f" % (src_agree, got_agree))

    # --- split by material and pre-apply S = diag(-1,1,1), which the reader applies again.
    groups = {}
    remap = {}
    for i, (p, n, uv, mat) in enumerate(kept):
        g = groups.setdefault(mat, ([], [], [], []))
        remap[i] = len(g[0])
        g[0].append((-p[0], p[1], p[2]))
        g[1].append((-n[0], n[1], n[2]))
        g[2].append(uv)
    for t in range(0, len(out_idx), 3):
        mat = kept[out_idx[t]][3]
        groups[mat][3].extend(remap[out_idx[t + k]] for k in range(3))

    # --- the three base-colour atlases, downsampled; every other map is dropped.
    used = sorted(groups.keys())
    images = []
    for mat in used:
        im = gltf["materials"][mat]["pbrMetallicRoughness"]["baseColorTexture"]["index"]
        src = gltf["images"][gltf["textures"][im]["source"]]
        view = gltf["bufferViews"][src["bufferView"]]
        off = view.get("byteOffset", 0)
        images.append(downsample(blob[off:off + view["byteLength"]], tex))
    groups = {used.index(mat): groups[mat] for mat in used}

    out_bytes = build_glb(groups, images, out_path)

    print("source     %s" % os.path.normpath(src_path))
    print("           %d meshes / %d verts / %d tris / %d materials / %d images / %d bytes"
          % (len(gltf["meshes"]), len(verts), src_tris, len(gltf["materials"]),
             len(gltf["images"]), src_bytes))
    print("cluster    cell %.5f over a %.3f x %.3f x %.3f box" % (cell, size[0], size[1], size[2]))
    print("winding    faces agree with normals: source %.4f, after the reader %.4f"
          % (src_agree, got_agree))
    print("textures   %d base-colour atlases at %d (metallic-roughness, normal and clearcoat dropped)"
          % (len(images), tex))
    print("OK  %s  %d verts / %d tris / %d bytes  (%.1f%% of the triangles, %.1f%% of the bytes)"
          % (os.path.normpath(out_path), len(kept), len(ktris), out_bytes,
             100.0 * len(ktris) / src_tris, 100.0 * out_bytes / src_bytes))


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_IN,
         sys.argv[2] if len(sys.argv) > 2 else DEFAULT_OUT,
         int(sys.argv[3]) if len(sys.argv) > 3 else TARGET_TRIS,
         int(sys.argv[4]) if len(sys.argv) > 4 else TEX_SIZE)

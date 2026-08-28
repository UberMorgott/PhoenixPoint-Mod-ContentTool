#!/usr/bin/env python3
"""
Render a weapon's INVENTORY ICON straight from the .glb the mod ships, offline.

Why this exists: Phoenix Point's inventory cell does NOT render the weapon model. It draws a
PRE-RENDERED Sprite that the item's def points at -

    UIInventorySlot.cs:445   ImageNode.sprite = _item.ItemDef.ViewElementDef.InventoryIcon
    ItemDef.cs:228-236       GetSmallIcon()/GetLargeIcon() -> ViewElementDef?.InventoryIcon
    ViewElementDef.cs:33     public Sprite InventoryIcon

so swapping a weapon's MESH leaves the cell showing the old gun's picture until a matching image
is supplied. The shipped ones are 450x450 (measured off `UI_PX_WeaponIcon_AssaultRifle_INV` in
sharedassets0.assets with UnityPy: Rect 450x450, full-sheet; the LR variant is 800x450).

This renders that image from the SAME geometry the game will put in the soldier's hand, so the
cell and the hand can never disagree - no Blender, no Unity, no dependency: an orthographic
z-buffered rasteriser over the .glb's own positions and normals.

    python render_icon.py <model.glb> <out.png> [size]

Stdlib only (json, struct, zlib, math).
"""
import json
import math
import os
import struct
import sys
import zlib

SIZE = 450
SS = 3                       # supersampling factor - the alpha edge is the whole look
MARGIN = 0.06                # fraction of the frame left empty around the silhouette
BASE = (168, 176, 188)       # cold steel; the shipped PX icons are near-monochrome
AMBIENT = 0.28
# Light from the upper left, slightly toward the viewer. Unity axes: X = across the barrel (and
# the view direction), Y = up, Z = down the barrel.
LIGHT = (0.62, 0.66, -0.42)


def read_glb(path):
    """The GLB2 container: 12-byte header, then chunks. Returns (gltf json, BIN bytes)."""
    data = open(path, "rb").read()
    magic, version, _ = struct.unpack_from("<III", data, 0)
    assert magic == 0x46546C67 and version == 2, "not a GLB2 file: %s" % path
    at, js, blob = 12, None, None
    while at < len(data):
        length, kind = struct.unpack_from("<II", data, at)
        chunk = data[at + 8: at + 8 + length]
        if kind == 0x4E4F534A:
            js = json.loads(chunk.decode("utf-8"))
        elif kind == 0x004E4942:
            blob = chunk
        at += 8 + length + (-length % 4)
    assert js is not None and blob is not None, "GLB is missing its JSON or BIN chunk"
    return js, blob


def accessor(gltf, blob, index):
    acc = gltf["accessors"][index]
    view = gltf["bufferViews"][acc["bufferView"]]
    fmt, width = {5126: ("<f", 4), 5123: ("<H", 2), 5125: ("<I", 4), 5121: ("<B", 1)}[acc["componentType"]]
    n = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[acc["type"]]
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = view.get("byteStride") or n * width
    out = []
    for i in range(acc["count"]):
        at = start + i * stride
        out.append(tuple(struct.unpack_from(fmt, blob, at + k * width)[0] for k in range(n)))
    return out


def png(path, width, height, rgba):
    """Minimal RGBA8 PNG. zlib is stdlib, so no image library is pulled in for one file."""
    raw = bytearray()
    for y in range(height):
        raw.append(0)                                    # filter type 0 (None)
        raw += rgba[y * width * 4:(y + 1) * width * 4]

    def chunk(tag, payload):
        return (struct.pack(">I", len(payload)) + tag + payload +
                struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    body = (b"\x89PNG\r\n\x1a\n" +
            chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(bytes(raw), 9)) +
            chunk(b"IEND", b""))
    open(path, "wb").write(body)
    return len(body)


def main(src, out, size=SIZE):
    gltf, blob = read_glb(src)
    prim = gltf["meshes"][0]["primitives"][0]
    # ContentTool's reader applies S = diag(-1,1,1) on read (GlbCodec.Convert), so the file carries
    # S-applied coordinates. Undo it here and we are looking at exactly what the game will show.
    pos = [(-p[0], p[1], p[2]) for p in accessor(gltf, blob, prim["attributes"]["POSITION"])]
    nrm = [(-n[0], n[1], n[2]) for n in accessor(gltf, blob, prim["attributes"]["NORMAL"])]
    idx = [i[0] for i in accessor(gltf, blob, prim["indices"])]

    # Side view: the barrel (+Z) runs left-to-right across the icon, +Y is up, and we look down
    # -X, which is the view every shipped weapon icon uses.
    def screen(p):
        return (p[2], p[1], p[0])

    pts = [screen(p) for p in pos]
    lo = [min(q[i] for q in pts) for i in range(2)]
    hi = [max(q[i] for q in pts) for i in range(2)]
    span = max(hi[0] - lo[0], hi[1] - lo[1])
    assert span > 0, "the model has no extent to draw"

    w = h = size * SS
    scale = (w * (1.0 - 2.0 * MARGIN)) / span
    ox = w * 0.5 - scale * (lo[0] + hi[0]) * 0.5
    oy = h * 0.5 + scale * (lo[1] + hi[1]) * 0.5      # +Y up, screen y down

    lm = math.sqrt(sum(c * c for c in LIGHT))
    light = tuple(c / lm for c in LIGHT)

    colour = bytearray(w * h * 4)
    depth = [-1e30] * (w * h)

    for t in range(0, len(idx) - 2, 3):
        a, b, c = idx[t], idx[t + 1], idx[t + 2]
        # Flat shading off the face normal: the icon reads as a solid object, and a per-vertex
        # gradient would only fight the 450px downsample.
        nx = sum(nrm[v][0] for v in (a, b, c)) / 3.0
        ny = sum(nrm[v][1] for v in (a, b, c)) / 3.0
        nz = sum(nrm[v][2] for v in (a, b, c)) / 3.0
        nl = math.sqrt(nx * nx + ny * ny + nz * nz) or 1.0
        lam = (nx * light[0] + ny * light[1] + nz * light[2]) / nl
        shade = AMBIENT + (1.0 - AMBIENT) * max(0.0, lam)
        r = min(255, int(BASE[0] * shade))
        g = min(255, int(BASE[1] * shade))
        bl = min(255, int(BASE[2] * shade))

        p0, p1, p2 = pts[a], pts[b], pts[c]
        x0, y0 = ox + scale * p0[0], oy - scale * p0[1]
        x1, y1 = ox + scale * p1[0], oy - scale * p1[1]
        x2, y2 = ox + scale * p2[0], oy - scale * p2[1]
        area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
        if area == 0.0:
            continue

        minx = max(0, int(math.floor(min(x0, x1, x2))))
        maxx = min(w - 1, int(math.ceil(max(x0, x1, x2))))
        miny = max(0, int(math.floor(min(y0, y1, y2))))
        maxy = min(h - 1, int(math.ceil(max(y0, y1, y2))))
        if minx > maxx or miny > maxy:
            continue

        for py in range(miny, maxy + 1):
            fy = py + 0.5
            row = py * w
            for px in range(minx, maxx + 1):
                fx = px + 0.5
                w0 = ((x1 - fx) * (y2 - fy) - (x2 - fx) * (y1 - fy)) / area
                w1 = ((x2 - fx) * (y0 - fy) - (x0 - fx) * (y2 - fy)) / area
                w2 = 1.0 - w0 - w1
                if w0 < 0.0 or w1 < 0.0 or w2 < 0.0:
                    continue
                z = w0 * p0[2] + w1 * p1[2] + w2 * p2[2]      # +X is toward the viewer
                at = row + px
                if z <= depth[at]:
                    continue
                depth[at] = z
                o = at * 4
                colour[o] = r
                colour[o + 1] = g
                colour[o + 2] = bl
                colour[o + 3] = 255

    # Box-downsample the supersampled buffer. Averaging the alpha is what gives the silhouette its
    # antialiased edge, and premultiplying keeps the transparent pixels from bleeding black.
    final = bytearray(size * size * 4)
    covered = 0
    for y in range(size):
        for x in range(size):
            rs = gs = bs = a_s = 0
            for sy in range(SS):
                base = ((y * SS + sy) * w + x * SS) * 4
                for sx in range(SS):
                    o = base + sx * 4
                    al = colour[o + 3]
                    if al:
                        rs += colour[o] * al
                        gs += colour[o + 1] * al
                        bs += colour[o + 2] * al
                        a_s += al
            o = (y * size + x) * 4
            if a_s:
                final[o] = rs // a_s
                final[o + 1] = gs // a_s
                final[o + 2] = bs // a_s
                final[o + 3] = a_s // (SS * SS)
                covered += 1
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    written = png(out, size, size, final)

    # Self-check: an icon that is empty, or one that is a solid block, is a broken render and must
    # not ship silently. A gun seen from the side fills a modest slice of a square frame.
    fill = covered / float(size * size)
    assert 0.04 < fill < 0.75, "silhouette covers %.1f%% of the icon - that is not a weapon" % (fill * 100)
    print("OK  %s  %dx%d  %d bytes  %d tris  silhouette %.1f%% of the frame"
          % (out, size, size, written, len(idx) // 3, fill * 100))


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, "..", "Content", "Meshes", "rifle.glb"),
         sys.argv[2] if len(sys.argv) > 2 else os.path.join(here, "..", "Icons", "rifle_inv.png"),
         int(sys.argv[3]) if len(sys.argv) > 3 else SIZE)

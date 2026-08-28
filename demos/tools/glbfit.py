#!/usr/bin/env python3
"""
The .glb bits every weapon-fitting script in demos\\ needs, in ONE place - because getting them
wrong is invisible until someone looks at the gun in game.

THE TRAP THIS MODULE EXISTS FOR. ContentTool converts glTF to Unity with S = diag(-1, 1, 1) and,
because det(S) = -1, ALSO reverses every triangle:

    GlbCodec.cs:177         "Because det(S) = -1, triangle winding must be reversed too ...
                             doing one without the other is what silently mirrors a model"
    GlbReader.cs:1043-1067  ToUnity: GlbCodec.Convert over Positions/Normals/..., then
                             for each triangle  swap indices [i+1] and [i+2]

A fitting script pre-applies S to the positions it writes, so that the reader's S cancels and the
mesh lands on the Unity coordinates the script computed. S is an involution, so that half works.
The winding half does NOT: the reader reverses the triangles unconditionally, and there is nothing
to cancel it. The result is a mesh whose faces point the opposite way from its normals - which
back-face culling draws inside-out or not at all, while every bounding-box check passes happily.

So a script that pre-applies S must ALSO pre-reverse its triangles. And because a bbox assert
cannot see this, <see cref="agreement"/> exists: it measures whether faces and normals agree, in
the SOURCE file and in what the reader will actually hand Unity, and the two must match.

Stdlib only.
"""
import json
import struct

GLB_MAGIC = 0x46546C67
CHUNK_JSON = 0x4E4F534A
CHUNK_BIN = 0x004E4942

_COMPONENT = {5120: ("<b", 1), 5121: ("<B", 1), 5122: ("<h", 2),
              5123: ("<H", 2), 5125: ("<I", 4), 5126: ("<f", 4)}
_COUNT = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def _layout(gltf, index):
    acc = gltf["accessors"][index]
    view = gltf["bufferViews"][acc["bufferView"]]
    fmt, width = _COMPONENT[acc["componentType"]]
    n = _COUNT[acc["type"]]
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = view.get("byteStride") or n * width
    return acc, fmt, width, n, start, stride


def read_accessor(gltf, blob, index):
    """One accessor as a list of tuples, honouring byteStride."""
    acc, fmt, width, n, start, stride = _layout(gltf, index)
    return [tuple(struct.unpack_from(fmt, blob, start + i * stride + k * width)[0] for k in range(n))
            for i in range(acc["count"])]


def write_accessor(gltf, blob, index, values):
    """Writes tuples (or scalars) back over the same accessor, in place. `blob` must be mutable."""
    acc, fmt, width, n, start, stride = _layout(gltf, index)
    assert len(values) == acc["count"], "accessor %d holds %d elements, got %d" % (
        index, acc["count"], len(values))
    for i, v in enumerate(values):
        parts = v if isinstance(v, (tuple, list)) else (v,)
        for k in range(n):
            struct.pack_into(fmt, blob, start + i * stride + k * width, parts[k])


def reverse_triangles(indices):
    """
    Swap each triangle's last two indices - the exact operation GlbReader.ToUnity will perform on
    the way in (GlbReader.cs:1063-1066). Applying it here means the reader's copy cancels it, so
    the mesh Unity receives keeps the source file's own winding.
    """
    assert len(indices) % 3 == 0, "%d indices is not a whole number of triangles" % len(indices)
    out = list(indices)
    for i in range(0, len(out), 3):
        out[i + 1], out[i + 2] = out[i + 2], out[i + 1]
    return out


def agreement(positions, normals, indices):
    """
    Fraction of triangles whose FACE normal - cross(b-a, c-a), i.e. the one the winding implies -
    points the same way as the triangle's own vertex normals.

    This is the arm a bounding box cannot be: a mesh that is inside-out has exactly the same box as
    one that is not. Degenerate triangles (zero area, or vertices with no normal) are skipped rather
    than counted as either, so a model with a few slivers cannot drift the number.
    """
    agree = total = 0
    for t in range(0, len(indices) - 2, 3):
        a, b, c = indices[t], indices[t + 1], indices[t + 2]
        pa, pb, pc = positions[a], positions[b], positions[c]
        u = (pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2])
        v = (pc[0] - pa[0], pc[1] - pa[1], pc[2] - pa[2])
        f = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
        if f[0] * f[0] + f[1] * f[1] + f[2] * f[2] < 1e-24:
            continue
        n = tuple(sum(normals[k][i] for k in (a, b, c)) for i in range(3))
        d = f[0] * n[0] + f[1] * n[1] + f[2] * n[2]
        if d == 0.0:
            continue
        total += 1
        if d > 0.0:
            agree += 1
    assert total > 0, "no non-degenerate triangle to measure"
    return agree / float(total)


def write_glb(gltf, blob, out_path):
    """The GLB2 container, 4-aligned, with the buffer inlined as the BIN chunk. Returns its size."""
    gltf["buffers"] = [{"byteLength": len(blob)}]
    js = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    js += b" " * (-len(js) % 4)
    body = bytes(blob) + b"\0" * (-len(blob) % 4)
    glb = struct.pack("<III", GLB_MAGIC, 2, 12 + 8 + len(js) + 8 + len(body))
    glb += struct.pack("<II", len(js), CHUNK_JSON) + js
    glb += struct.pack("<II", len(body), CHUNK_BIN) + body
    open(out_path, "wb").write(glb)
    return len(glb)


def _self_check():
    """One triangle, front-facing, is still front-facing after a reverse-and-reverse round trip -
    and is NOT after a single reverse. The second half is what makes this arm able to fail."""
    pos = [(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)]
    nrm = [(0.0, 0.0, 1.0)] * 3
    idx = [0, 1, 2]
    assert agreement(pos, nrm, idx) == 1.0
    assert agreement(pos, nrm, reverse_triangles(idx)) == 0.0
    assert agreement(pos, nrm, reverse_triangles(reverse_triangles(idx))) == 1.0
    print("OK  glbfit self-check: agreement() distinguishes a reversed triangle from an intact one")


if __name__ == "__main__":
    _self_check()

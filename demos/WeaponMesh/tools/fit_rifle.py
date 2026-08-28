#!/usr/bin/env python3
"""
Fit an imported .gltf into a SHIPPED Phoenix Point mesh's own local box, and write the .glb that
Content\\Meshes\\ takes.

Why this script exists at all: a mesh replacement changes GEOMETRY and nothing else. The weapon is
mounted by parenting the shipped `*_Ready` prefab to a named attachment-point Transform on the
soldier's rig (AddonDef.ProvidedSlotBind.AttachmentPointName), so the pose, the hand IK and every
animation keep addressing the prefab's own Transform. The new geometry therefore has to arrive
ALREADY in the shipped mesh's local coordinates - metres, +Z down the barrel, +Y up - or it lands
in the soldier's hand at the wrong size, pointing the wrong way.

Every number below is measured, not tuned. Run with no arguments; it prints what it derived.

    python fit_rifle.py <Gun_Rifle.gltf> <out.glb>

Stdlib only, so anyone who cloned this repo can run it.
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tools"))
from glbfit import agreement, read_accessor, reverse_triangles, write_accessor, write_glb

# ---------------------------------------------------------------------------------------------
# The TARGET box: m_LocalAABB of the shipped Mesh `WPN_PX_RG_Assault_Rifle_T01_V01`, read straight
# out of px_equipment_assets_all.bundle. `ct_extract` prints the same mesh; these are its numbers.
#   centre (0, 0.03419609, 0.15292451)   extent (0.03384929, 0.12561084, 0.30141166)
# So in the shipped mesh's own local space the rifle is 0.603 m long down +Z (the barrel), 0.251 m
# tall on +Y, 0.068 m thick on X, with the grip sitting near the origin.
# ---------------------------------------------------------------------------------------------
PP_CENTER = (0.0, 0.03419608995318413, 0.15292450785636902)
PP_EXTENT = (0.033849287778139114, 0.1256108433008194, 0.3014116585254669)

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_IN = os.path.join(HERE, "source", "Gun_Rifle.gltf")
DEFAULT_OUT = os.path.join(os.path.dirname(HERE), "Content", "Meshes", "rifle.glb")


def rotate(v):
    """
    The one orientation decision, written as a basis mapping instead of three Euler angles.

    The source rifle (Quaternius, glTF space) lies along -X with the muzzle at x = -0.752, +Y up,
    thickness on Z. The shipped rifle lies along +Z, +Y up, thickness on X. Keeping "up" fixed,
    that is exactly:

        glTF -X (muzzle)  ->  Unity +Z (muzzle)
        glTF +Y (up)      ->  Unity +Y
        glTF +Z (side)    ->  Unity +X

    det = +1, so this is a ROTATION and the model is not mirrored. Getting that wrong is how an
    imported gun ends up with its ejection port on the wrong side and nobody notices for a week.
    """
    x, y, z = v
    return (z, y, -x)


def main(src_path, out_path):
    gltf = json.load(open(src_path, encoding="utf-8"))
    blob = open(os.path.join(os.path.dirname(src_path), gltf["buffers"][0]["uri"]), "rb").read()

    prim = gltf["meshes"][0]["primitives"][0]
    positions = read_accessor(gltf, blob, prim["attributes"]["POSITION"])
    normals = read_accessor(gltf, blob, prim["attributes"]["NORMAL"])
    indices = [i[0] for i in read_accessor(gltf, blob, prim["indices"])]

    lo = [min(p[i] for p in positions) for i in range(3)]
    hi = [max(p[i] for p in positions) for i in range(3)]
    src_center = [(lo[i] + hi[i]) / 2.0 for i in range(3)]

    # Size of the rotated source, axis for axis against the shipped box. abs(): `rotate` is a basis
    # mapping and negates one component, which is meaningless for a SIZE - and a negative uniform
    # scale is a point reflection, i.e. a silently mirrored, inside-out model.
    src_size = [abs(v) for v in rotate([hi[i] - lo[i] for i in range(3)])]
    pp_size = [2.0 * e for e in PP_EXTENT]

    # ONE uniform scale, and the rule that picks it: the smallest per-axis ratio, so the new
    # geometry fits INSIDE the silhouette the game already reserved for this weapon on every axis.
    # A gun that overflows its own box clips through the soldier's hands and through cover.
    ratios = [pp_size[i] / src_size[i] for i in range(3)]
    scale = min(ratios)
    assert scale > 0.0, "a negative uniform scale mirrors the model - check rotate()/abs()"

    # Centre on centre. The shipped AABB is the only statement the game makes about where this
    # weapon sits relative to its grip, so matching centres is the one placement that needs no
    # taste.
    rc = rotate(src_center)
    translate = [PP_CENTER[i] - scale * rc[i] for i in range(3)]

    def to_unity(p):
        r = rotate(p)
        return [scale * r[i] + translate[i] for i in range(3)]

    # ContentTool's reader converts glTF -> Unity with S = diag(-1, 1, 1) (GlbCodec.Convert) and
    # reverses triangle winding because det(S) = -1. S is an involution, so to make the reader
    # land on the Unity coordinates computed above, the file must carry S applied to them.
    unity_pos = [to_unity(p) for p in positions]
    out_pos = [(-p[0], p[1], p[2]) for p in unity_pos]
    out_nrm = []
    for n in normals:
        r = rotate(n)
        out_nrm.append((-r[0], r[1], r[2]))

    # --- self-check: this is the whole point of the script, so it fails loudly rather than
    #     shipping a rifle that is inside-out or the size of a car.
    ulo = [min(p[i] for p in unity_pos) for i in range(3)]
    uhi = [max(p[i] for p in unity_pos) for i in range(3)]
    for i in range(3):
        assert ulo[i] >= PP_CENTER[i] - PP_EXTENT[i] - 1e-4, "axis %d overflows the shipped box" % i
        assert uhi[i] <= PP_CENTER[i] + PP_EXTENT[i] + 1e-4, "axis %d overflows the shipped box" % i
    long_axis = max(range(3), key=lambda i: uhi[i] - ulo[i])
    assert long_axis == 2, "the barrel must end up on +Z, got axis %d" % long_axis
    assert abs((uhi[2] - ulo[2]) - pp_size[2]) < 1e-4, "the barrel is not the shipped length"

    # --- WINDING. GlbReader.ToUnity reverses every triangle unconditionally (GlbReader.cs:1063-1066)
    #     because det(S) = -1. This file already carries S pre-applied, so the reader's S cancels and
    #     there is nothing left for its reversal to compensate - it would just flip the faces. So the
    #     triangles are pre-reversed here too, and the reader's copy cancels THAT.
    out_idx = reverse_triangles(indices)

    # The arm a bounding box cannot be: an inside-out mesh has exactly the same box. Measure whether
    # faces and normals agree in the SOURCE, then in what the reader will actually hand Unity, and
    # require the same answer. `rotate` has det +1, so a rotation cannot change it - only a lost or
    # doubled winding reversal can. Drop the reverse_triangles call above and this FAILS.
    reader_idx = reverse_triangles(out_idx)          # exactly what GlbReader.ToUnity will produce
    src_agree = agreement(positions, normals, indices)
    unity_nrm = [rotate(n) for n in normals]
    got_agree = agreement(unity_pos, unity_nrm, reader_idx)
    assert abs(got_agree - src_agree) < 1e-9, (
        "winding/normal agreement changed: source %.4f -> Unity %.4f. The mesh would render "
        "inside-out under back-face culling." % (src_agree, got_agree))

    # --- write the .glb. Same structure Blender emitted, minus the external images (a mesh
    #     replacement paints through the SHIPPED material, so the glTF material is never read).
    out_blob = bytearray(blob)
    write_accessor(gltf, out_blob, prim["attributes"]["POSITION"], out_pos)
    write_accessor(gltf, out_blob, prim["attributes"]["NORMAL"], out_nrm)
    write_accessor(gltf, out_blob, prim["indices"], out_idx)

    acc = gltf["accessors"][prim["attributes"]["POSITION"]]
    acc["min"] = [min(p[i] for p in out_pos) for i in range(3)]
    acc["max"] = [max(p[i] for p in out_pos) for i in range(3)]
    for key in ("images", "textures", "samplers"):
        gltf.pop(key, None)
    gltf["materials"] = [{"name": m.get("name", "material")} for m in gltf.get("materials", [])]
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    glb_size = write_glb(gltf, out_blob, out_path)

    print("source  bbox min %s max %s" % (["%.4f" % v for v in lo], ["%.4f" % v for v in hi]))
    print("per-axis ratios  x=%.4f y=%.4f z=%.4f  ->  uniform scale %.6f (smallest wins)"
          % (ratios[0], ratios[1], ratios[2], scale))
    print("translate        %s" % ["%.6f" % v for v in translate])
    print("unity bbox       min %s max %s" % (["%.4f" % v for v in ulo], ["%.4f" % v for v in uhi]))
    print("shipped bbox     min %s max %s"
          % (["%.4f" % (PP_CENTER[i] - PP_EXTENT[i]) for i in range(3)],
             ["%.4f" % (PP_CENTER[i] + PP_EXTENT[i]) for i in range(3)]))
    print("winding          faces agree with normals: source %.4f, after the reader %.4f"
          % (src_agree, got_agree))
    print("OK  %s  %d verts / %d tris / %d bytes"
          % (out_path, len(out_pos), len(out_idx) // 3, glb_size))


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_IN,
         sys.argv[2] if len(sys.argv) > 2 else DEFAULT_OUT)

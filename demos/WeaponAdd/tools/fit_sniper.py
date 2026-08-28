#!/usr/bin/env python3
"""
Fit the CC0 sniper rifle into the local box Phoenix Point reserves for a Phoenix sniper, and write
the .glb that Content\\Models\\ takes.

Different job from WeaponMesh's fit_rifle.py in ONE respect and no others: nothing here replaces a
shipped mesh, so the box is not a constraint the engine will enforce - it is the SPECIFICATION.
A weapon is an Addon parented to a named attachment transform on the soldier's rig
(AddonDef.ProvidedSlotBind.AttachmentPointName -> Addon.cs:49-53 -> AddonsManager.cs:120), so a
brand-new weapon prefab lands in the hand at whatever coordinates its mesh carries. Copying the
shipped sniper's own m_LocalAABB is therefore the only way to arrive in the hand at the right size,
the right way round, with the grip at the origin.

Measured 2026-08-23 with UnityPy off px_equipment_assets_all.bundle, Mesh
`WPN_PX_RG_Sniper_Rifle_T01_V01` (7676 verts):
    m_LocalAABB centre (0.00435247, 0.02574386, 0.30868560)
                extent (0.03773832, 0.11355425, 0.46011382)
-> 0.920 m of rifle down +Z, 0.227 m on +Y, 0.075 m on X.

It also prints the three EXT_ socket positions the new weapon needs, DERIVED from the fitted box
rather than typed: Phoenix Point resolves `EXT_ShootPoint` / `EXT_AimPoint` / `EXT_AimIKPoint` by
NAME off the weapon's visual root (WeaponDef fields ProjectileOrigin / AimPoint / AimTransform;
TacticalLevelController.cs:1533-1549 logs "Can't find ... projectile origin" and Weapon.cs:425
then indexes an empty array), and a prefab baked by ContentTool is root + one mesh child, so those
three empties have to be added at runtime. The numbers below are what WeaponAddMain.cs carries.

    python fit_sniper.py <Gun_Sniper.gltf> <out.glb>

Stdlib only.
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tools"))
from glbfit import agreement, read_accessor, reverse_triangles, write_accessor, write_glb

PP_CENTER = (0.004352474585175514, 0.02574385702610016, 0.3086856007575989)
PP_EXTENT = (0.03773832321166992, 0.11355425417423248, 0.4601138234138489)

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_IN = os.path.join(HERE, "source", "Gun_Sniper.gltf")
DEFAULT_OUT = os.path.join(os.path.dirname(HERE), "Content", "Models", "sniper.glb")


def rotate(v):
    """
    Same basis mapping fit_rifle.py derives, and for the same reason: this kit models its guns
    lying along -X with +Y up, and Phoenix Point holds them along +Z with +Y up.

        glTF -X (muzzle) -> Unity +Z (muzzle)
        glTF +Y (up)     -> Unity +Y
        glTF +Z (side)   -> Unity +X

    det = +1, so it is a rotation and the gun is not mirrored. Verified against this file: the
    source bbox is min x=-1.2769 max x=0.4408, i.e. the barrel really is the -X end.
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
    src_size = [abs(v) for v in rotate([hi[i] - lo[i] for i in range(3)])]
    pp_size = [2.0 * e for e in PP_EXTENT]

    ratios = [pp_size[i] / src_size[i] for i in range(3)]
    scale = min(ratios)
    assert scale > 0.0, "a negative uniform scale mirrors the model - check rotate()/abs()"

    rc = rotate(src_center)
    translate = [PP_CENTER[i] - scale * rc[i] for i in range(3)]

    def to_unity(p):
        r = rotate(p)
        return [scale * r[i] + translate[i] for i in range(3)]

    # ContentTool's reader applies S = diag(-1,1,1) (GlbCodec.Convert) and reverses winding, and S
    # is an involution, so the FILE must carry S applied to the Unity coordinates we want.
    unity_pos = [to_unity(p) for p in positions]
    out_pos = [(-p[0], p[1], p[2]) for p in unity_pos]
    out_nrm = []
    for n in normals:
        r = rotate(n)
        out_nrm.append((-r[0], r[1], r[2]))

    ulo = [min(p[i] for p in unity_pos) for i in range(3)]
    uhi = [max(p[i] for p in unity_pos) for i in range(3)]
    for i in range(3):
        assert ulo[i] >= PP_CENTER[i] - PP_EXTENT[i] - 1e-4, "axis %d overflows the shipped box" % i
        assert uhi[i] <= PP_CENTER[i] + PP_EXTENT[i] + 1e-4, "axis %d overflows the shipped box" % i
    long_axis = max(range(3), key=lambda i: uhi[i] - ulo[i])
    assert long_axis == 2, "the barrel must end up on +Z, got axis %d" % long_axis

    # --- the three EXT_ sockets, derived from the FITTED box and nothing else.
    #   ShootPoint : the muzzle - front face of the box (max Z), on the barrel line.
    #   AimPoint / AimIKPoint : where the soldier's eye lines up, i.e. the sights - same barrel
    #     line, at the rear of the receiver rather than at the muzzle.
    # The barrel of a rifle in this kit sits ABOVE the mid-height of the silhouette (the stock and
    # the magazine fill the lower half), so the line is taken at 70% of the box height, not 50%.
    cx = (ulo[0] + uhi[0]) / 2.0
    barrel_y = ulo[1] + 0.70 * (uhi[1] - ulo[1])
    shoot = (cx, barrel_y, uhi[2])
    aim = (cx, barrel_y, ulo[2] + 0.62 * (uhi[2] - ulo[2]))
    # EXT_ShellPoint - where the spent case leaves. Weapon.SpawnShell (Weapon.cs:408-421) looks it
    # up by name and logs "has a shell prefab but invalid shell ejection point" every single shot
    # when it is missing, and the shipped sniper's effects def DOES carry a Shell prefab. Right side
    # of the receiver: the +X face of the box, at the same station as the sights.
    shell = (uhi[0], barrel_y, aim[2])

    # --- WINDING, and the arm that can actually catch it going wrong. GlbReader.ToUnity reverses
    #     every triangle unconditionally (GlbReader.cs:1063-1066) because det(S) = -1; this file
    #     already carries S pre-applied, so that reversal has nothing to compensate and would simply
    #     flip the faces. Pre-reverse here and the reader's copy cancels it.
    out_idx = reverse_triangles(indices)
    reader_idx = reverse_triangles(out_idx)          # exactly what GlbReader.ToUnity will produce
    src_agree = agreement(positions, normals, indices)
    unity_nrm = [rotate(n) for n in normals]
    got_agree = agreement(unity_pos, unity_nrm, reader_idx)
    assert abs(got_agree - src_agree) < 1e-9, (
        "winding/normal agreement changed: source %.4f -> Unity %.4f. The mesh would render "
        "inside-out under back-face culling." % (src_agree, got_agree))

    # --- write the .glb
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
    print("EXT_ShootPoint            (%.5ff, %.5ff, %.5ff)" % shoot)
    print("EXT_AimPoint/EXT_AimIKPoint (%.5ff, %.5ff, %.5ff)" % aim)
    print("EXT_ShellPoint            (%.5ff, %.5ff, %.5ff)" % shell)
    print("OK  %s  %d verts / %d tris / %d bytes"
          % (out_path, len(out_pos), len(out_idx) // 3, glb_size))


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_IN,
         sys.argv[2] if len(sys.argv) > 2 else DEFAULT_OUT)

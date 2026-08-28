#!/usr/bin/env python3
"""
Write the four 4x4 neutral maps this demo puts over the shipped rifle's detail textures.

A mesh replacement changes geometry only. The shipped material keeps pointing at the shipped
2048x2048 normal / metallic-gloss / occlusion / emissive maps, and those were painted for the
Ares AR-1's UV layout - which the imported mesh does not have. Left alone they smear another gun's
panel lines, wear and glow over the new surface, and it reads as a bug rather than as a mod.

Neutralising them is four solid-colour textures. They are 4x4 because a uniform texture needs no
resolution and 100 bytes each keeps a cloned repo small.

Stdlib + Pillow. Values, and why each one:

  normal    (128,128,255,128)  flat under BOTH normal-map unpack conventions: the DXT5nm path
                               reads x from A and y from G (128 -> 0), the plain-RGB path reads
                               (128,128,255) -> (0,0,1). Whichever the shader compiled to, the
                               perturbation is zero. The replaced mesh also carries no tangents
                               (ContentTool's mesh buffers are position/normal/uv0), so a live
                               normal map would have nothing correct to lean on anyway.
  metallic  (179,179,179,102)  Unity's Standard layout: metallic in R (0.70), smoothness in A
                               (0.40). Cold iron with a dull sheen - the look the grimdark albedo
                               was recoloured toward. One byte to change if you want a shinier gun.
  occlusion (255,255,255,255)  fully lit; ambient occlusion belongs to the mesh that was baked
                               with it.
  emissive  (0,0,0,255)        the Ares has glowing bits. The new mesh does not.
"""
import os

from PIL import Image

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Content", "Textures")
MAPS = {
    "rifle_normal_flat.png": (128, 128, 255, 128),
    "rifle_metallic_flat.png": (179, 179, 179, 102),
    "rifle_occlusion_white.png": (255, 255, 255, 255),
    "rifle_emissive_off.png": (0, 0, 0, 255),
}

os.makedirs(OUT, exist_ok=True)
for name, rgba in MAPS.items():
    path = os.path.join(OUT, name)
    Image.new("RGBA", (4, 4), rgba).save(path)
    back = Image.open(path).convert("RGBA")
    assert back.size == (4, 4) and back.getpixel((0, 0)) == rgba, name
    print("OK  %s  %s  %d bytes" % (name, rgba, os.path.getsize(path)))

"""Pack pp_wwise_index.json into the compact binary the in-game validator embeds.

The JSON is 517 KB of names we never need at runtime - the validator only asks "is this
uint32 taken". Two sorted u32 arrays (~35 KB) answer that with no JSON parser in the mod.

Usage: python pack_id_index.py ..\\data\\pp_wwise_index.json ..\\lib\\ppids.bin

Layout: "CTID" | u32 mediaCount | media ids ... | u32 computedCount | computed ids ...
  media    = _media_ids_all, the COMPLETE occupied media set (7697) - never _media_ids (7691)
  computed = every name-hashed id (events, banks, switches, states, RTPCs, triggers, ...)
"""
import json, struct, sys

src, out = sys.argv[1], sys.argv[2]
idx = json.load(open(src))

media = sorted(set(idx["_media_ids_all"]))
computed = sorted({i for k, v in idx.items()
                   if isinstance(v, dict) and k != "File(media)"
                   for i in v.values()})

with open(out, "wb") as f:
    f.write(b"CTID")
    for arr in (media, computed):
        f.write(struct.pack("<I", len(arr)))
        f.write(struct.pack("<%dI" % len(arr), *arr))

print("media", len(media), "computed", len(computed), "->", out)

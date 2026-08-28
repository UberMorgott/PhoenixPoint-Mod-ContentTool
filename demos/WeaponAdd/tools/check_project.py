#!/usr/bin/env python3
"""
Check this mod offline, the way the TOOL will read it - before anything is baked or installed.

Five things can be wrong here without the game saying so:
  1. the "publish" row does not parse the way ContentProject.ParsePublish reads it (a regex over
     the raw text, NOT a JSON parser - see the note in ContentProject.cs:404);
  2. "asset" does not name what ProjectBake will actually put in the bundle. A model becomes
     "models/<stem>" (ProjectBake.cs:164 -> BundleBaker.AddModel), and CatalogApply refuses a key
     whose asset is not in the bundle (Route7.cs:313) - but only at install time, on the player's
     machine;
  3. the texture is not named after the model, so ProjectBake never binds it to _MainTex
     (ProjectBake.cs:98-100 matches Textures[i].Name == model.Name);
  4. "deps" names a bundle the shipped catalog does not have - CatalogKeys.Deps throws
     (CatalogKeys.cs:225);
  5. the key is not shaped like an Addressables runtime key, so AssetReference.RuntimeKeyIsValid
     rejects it and AddonSkinDataBase.GetPrefabAsset returns null forever, silently.

Stdlib only. Exits non-zero on the first failure.
"""
import json
import os
import re
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
GAME_BUNDLES = r"D:\Steam\steamapps\common\Phoenix Point\PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64"


def field(obj, name):
    """ContentProject.Field, restated: the value is taken verbatim out of the raw text."""
    return re.search('"' + name + r'"\s*:\s*"([^"]*)"', obj).group(1) if \
        re.search('"' + name + r'"\s*:\s*"([^"]*)"', obj) else ""


def fail(msg):
    print("FAIL " + msg)
    sys.exit(1)


def embedded_images(glb_path):
    """How many images the .glb carries inside itself, read the way the tool reads it."""
    data = open(glb_path, "rb").read()
    at, js = 12, None
    while at < len(data):
        clen, ckind = struct.unpack_from("<II", data, at)
        if ckind == 0x4E4F534A:
            js = json.loads(data[at + 8: at + 8 + clen].decode("utf-8"))
        at += 8 + clen + (-clen % 4)
    return len(js.get("images", [])) if js else 0


def main():
    text = open(os.path.join(ROOT, "ppcontent.json"), encoding="utf-8").read()
    arr = re.search(r'"publish"\s*:\s*\[(.*?)\]', text, re.S)
    if not arr:
        fail("ppcontent.json declares no \"publish\" array")
    rows = re.findall(r"\{[^{}]*\}", arr.group(1), re.S)
    if not rows:
        fail("\"publish\" is present but no complete entry was read from it")

    for row in rows:
        key, asset, kind, deps = (field(row, "key"), field(row, "asset"),
                                  field(row, "type"), field(row, "deps"))
        if not key or not asset:
            fail("a publish row needs both \"key\" and \"asset\": " + row)

        # (5) shaped like the game's own AssetReference guids - 32 lowercase hex.
        if not re.fullmatch("[0-9a-f]{32}", key):
            fail("key '%s' is not 32 lowercase hex digits; Phoenix Point's own AssetReferences are "
                 "(e.g. 604561be7de7cb6479711b4e31bdc02d), and a skin def's DefaultPrefab has to "
                 "pass AssetReference.RuntimeKeyIsValid" % key)

        # (2)+(3) the asset the bake will really write, and the texture that binds to it.
        folder, _, stem = asset.partition("/")
        if folder != "models":
            fail("asset '%s' does not start with 'models/' - ProjectBake writes a model as "
                 "\"models/<stem>\"" % asset)
        glb = os.path.join(ROOT, "Content", "Models", stem + ".glb")
        if not os.path.exists(glb):
            fail("asset '%s' names no file: %s is missing" % (asset, glb))
        png = os.path.join(ROOT, "Content", "Textures", stem + ".png")
        # A model may now be painted by its OWN .glb: the bake decodes the embedded base-colour
        # image and binds it to _MainTex. So a missing .png is only a problem when the file carries
        # no image either - otherwise this rule would reject exactly the downloaded models the
        # embedded-texture support exists to make work.
        embedded = embedded_images(glb)
        if not os.path.exists(png) and embedded == 0:
            fail("Content\\Textures\\%s.png is missing AND %s.glb carries no embedded image, so "
                 "nothing paints this model and it would render pure white" % (stem, stem))
        if kind != "GameObject":
            fail("type '%s' - a published prefab must be declared as GameObject" % kind)

        # (4) the shipped bundle the material's external shader PPtr needs mounted.
        for dep in [d for d in deps.split(";") if d]:
            if not os.path.exists(os.path.join(GAME_BUNDLES, dep)):
                print("WARN dep '%s' was not found under %s - check the path, or this machine's "
                      "install differs" % (dep, GAME_BUNDLES))

        # the .glb itself: valid GLB2, self-contained, non-degenerate.
        data = open(glb, "rb").read()
        magic, version, length = struct.unpack_from("<III", data, 0)
        if magic != 0x46546C67 or version != 2 or length != len(data):
            fail("%s is not a well-formed GLB2 (magic/version/length)" % glb)
        at, js, blob = 12, None, None
        while at < len(data):
            clen, ckind = struct.unpack_from("<II", data, at)
            if at % 4:
                fail("%s has a chunk that is not 4-aligned" % glb)
            if ckind == 0x4E4F534A:
                js = json.loads(data[at + 8: at + 8 + clen].decode("utf-8"))
            elif ckind == 0x004E4942:
                blob = data[at + 8: at + 8 + clen]
            at += 8 + clen + (-clen % 4)
        if js is None or blob is None:
            fail("%s is missing its JSON or BIN chunk" % glb)
        if any("uri" in b for b in js.get("buffers", [])):
            fail("%s references an EXTERNAL buffer - a mod must ship one self-contained file" % glb)
        # Embedded images are now a FEATURE, not a fault: the bake decodes them and binds them to
        # _MainTex, which is what lets a downloaded model arrive painted. This used to fail here.
        prim = js["meshes"][0]["primitives"][0]
        verts = js["accessors"][prim["attributes"]["POSITION"]]["count"]
        tris = js["accessors"][prim["indices"]]["count"] // 3
        print("ok   publish  key %s  ->  %s   (%s)" % (key, asset, kind))
        print("ok   model    %s  %d verts / %d tris / %d bytes" % (stem + ".glb", verts, tris, len(data)))
        print("ok   texture  %s" % (("%s.png  %d bytes" % (stem, os.path.getsize(png)))
                                    if os.path.exists(png)
                                    else "from the .glb itself, %d embedded image(s)" % embedded))

    keys = {field(r, "key") for r in rows}
    weapons(text, keys)
    print("OK  %d publish row(s)" % len(rows))


def weapons(text, published):
    """
    The "weapons" array, read the way WeaponBuild.Parse reads it - same two regexes, same rules,
    so a manifest that would be refused in-game is refused HERE, before a launch is spent on it.

    Five things can be wrong without the game saying so:
      1. an entry is missing id / clone / guid - WeaponBuild.Parse throws;
      2. an entry names a "model" whose key is not in "publish", so AddonSkinDataBase resolves it
         to null forever and the weapon holds nothing;
      3. an entry names a "model" but no "shoot" socket - projectiles then leave from nowhere and
         the muzzle flash spawns at the origin (Weapon.cs:389-397, :425);
      4. an "icon" names a file that is not there, so the cell silently shows the CLONED weapon's
         picture instead;
      5. two entries share a guid, which means the second def overwrites the first.
    """
    arr = re.search(r'"weapons"\s*:\s*\[(.*?)\]', text, re.S)
    if not arr:
        print("ok   weapons  (none declared)")
        return
    entries = re.findall(r"\{[^{}]*\}", arr.group(1), re.S)
    if not entries:
        fail('"weapons" is present but no complete entry was read from it')

    seen = {}
    for e in entries:
        wid, clone, guid = field(e, "id"), field(e, "clone"), field(e, "guid")
        if not wid or not clone or not guid:
            fail('every "weapons" entry needs "id", "clone" and "guid": ' + e)
        if guid in seen:
            fail("guid %s is used by both '%s' and '%s' - the second def would overwrite the first"
                 % (guid, seen[guid], wid))
        seen[guid] = wid

        model = field(e, "model")
        if model:
            if model not in published:
                fail("'%s' names model key '%s', which no \"publish\" row declares - "
                     "AddonSkinDataBase.GetPrefabAsset would resolve it to null forever" % (wid, model))
            # Mirrors WeaponBuild.Parse exactly: a model needs sockets, and there are two honest ways
            # to have them. NOT a zero check - "0,0,0" is a legal muzzle position, and using it as the
            # "absent" sentinel is what made a placeholder indistinguishable from a real value.
            if not field(e, "shoot") and field(e, "fit") != "auto":
                fail("'%s' declares a \"model\" but no \"shoot\" socket and does not ask for one. "
                     "Either add \"shoot\" (tools\\fit_*.py prints all three), or set "
                     "\"fit\": \"auto\" and the engine derives them from the box it fits into" % wid)
        # "keywords" is DEFNAME=VALUE separated by ';' - WeaponBuild.Parse throws on anything else,
        # and a manifest that would be refused in-game is refused HERE instead of costing a launch.
        for clause in [c.strip() for c in field(e, "keywords").split(";") if c.strip()]:
            if "=" not in clause or not clause.split("=", 1)[0].strip():
                fail("'%s' has a \"keywords\" clause that is not DEFNAME=VALUE: '%s'" % (wid, clause))
            try:
                float(clause.split("=", 1)[1].strip())
            except ValueError:
                fail("'%s' keyword '%s' has a value that is not a number" % (wid, clause))
            if not clause.split("=", 1)[0].strip().endswith("Def"):
                fail("'%s' names damage keyword '%s', which is not a def name - it must be the DEF, "
                     "e.g. Burning_DamageKeywordEffectorDef" % (wid, clause.split("=", 1)[0].strip()))
        dt = field(e, "damagetype")
        if dt and not dt.endswith("Def"):
            fail("'%s' damagetype '%s' must be a def name, e.g. Fire_StandardDamageTypeEffectDef"
                 % (wid, dt))

        icon = field(e, "icon")
        if icon and not os.path.exists(os.path.join(ROOT, icon.replace("\\\\", os.sep).replace("\\", os.sep))):
            fail("'%s' names icon '%s', which is not in this mod - the inventory cell would "
                 "silently show %s's picture" % (wid, icon, clone))
        print("ok   weapon   %-34s clone=%-28s model=%s icon=%s"
              % (wid, clone, model or "(clone's own art)", icon or "(clone's own)"))
    print("ok   weapons  %d entry(ies), no duplicate guid" % len(entries))


if __name__ == "__main__":
    main()

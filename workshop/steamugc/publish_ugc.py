"""
Headless Steam Workshop publisher for the ContentTool Phoenix Point mod.

Rides the ALREADY-RUNNING, logged-in Steam client via SteamworksPy (no username
and no password stored anywhere) -- the same auth model the official
PPWorkshopTool uses. Steam must be running as the account that will OWN the item.

THIS SCRIPT HAS NEVER BEEN RUN AGAINST STEAM. It was written offline. Read
../WORKSHOP.md before the first invocation.

ONE item is ever published: ContentTool itself. The demo mods under demos/ are
GitHub downloads reached from the documentation site - there is deliberately no
per-demo path here, and adding one would be wrong.

Usage:
  # First publish (creates a brand-new item; do it PRIVATE first, look at it, then flip):
  python publish_ugc.py --create --changenote "v1.0.0 initial release" --visibility private

  # Subsequent updates (re-upload content to the existing item):
  python publish_ugc.py --update --item <publishedfileid> --changenote "v1.1.0"

  # Store description(s) only, no re-upload:
  python publish_ugc.py --localize-descriptions --item <publishedfileid>

On success: prints the publishedfileid + item URL, writes published_id.txt, and
stamps the id into ../contenttool.vdf (the SteamCMD path used by update.ps1).
"""
import argparse
import glob
import os
import sys
import time

# --- Resolve paths BEFORE any chdir (content/preview must be absolute) --------
HERE = os.path.dirname(os.path.abspath(__file__))             # workshop/steamugc
WORKSHOP_DIR = os.path.dirname(HERE)                          # workshop
REPO_ROOT = os.path.dirname(WORKSHOP_DIR)                     # ContentTool repo root

CONTENT_FOLDER = os.path.join(WORKSHOP_DIR, "Dist")
LOCALE_DIR = os.path.join(WORKSHOP_DIR, "locale")
VDF_FILE = os.path.join(WORKSHOP_DIR, "contenttool.vdf")
PUBLISHED_ID_FILE = os.path.join(HERE, "published_id.txt")

# The preview lives beside the other Workshop inputs, in workshop\image\ (same base
# as Dist/, locale/ and the vdf - NOT the repo root). Either extension is accepted;
# the item is never submitted without one (see resolve_preview).
PREVIEW_DIR = os.path.join(WORKSHOP_DIR, "image")
PREVIEW_CANDIDATES = [
    os.path.join(PREVIEW_DIR, "steam_preview.png"),
    os.path.join(PREVIEW_DIR, "steam_preview.jpg"),
]

APP_ID = 839770
TITLE = "Content Tool"

# Phoenix Point (appid 839770) accepts a FIXED tag set: Geoscape, Tactical,
# Difficulty, Gameplay, Augments, Bionics, Mutations, plus the DLC tags. There is
# no "Tools", no "UI" and no "Quality of Life" - an unknown tag makes
# SubmitItemUpdate FAIL outright, so this list stays hardcoded rather than being
# a free-text CLI option. Tags are item-GLOBAL, not per-language.
WORKSHOP_TAGS = ["Gameplay"]

# SteamworksPy runtime: the vendored `steamworks` package plus SteamworksPy64.dll,
# steam_api64.dll and steam_appid.txt. All four are environment-local binaries and
# are git-ignored, so they are NOT in this repo. If a copy already exists in the
# sibling PerkOracle checkout it is reused as-is - same appid, same binaries -
# rather than duplicating half a megabyte of native DLLs into a second repo.
DEFAULT_RUNTIME_DIRS = [
    HERE,
    os.path.join(os.path.dirname(REPO_ROOT), "PerkOracle", "workshop", "steamugc"),
]


def resolve_runtime(explicit: str = "") -> str:
    """Return the directory holding the SteamworksPy package + native DLLs."""
    candidates = [explicit] if explicit else DEFAULT_RUNTIME_DIRS
    for d in candidates:
        if os.path.isdir(os.path.join(d, "steamworks")) and \
           os.path.exists(os.path.join(d, "SteamworksPy64.dll")) and \
           os.path.exists(os.path.join(d, "steam_api64.dll")) and \
           os.path.exists(os.path.join(d, "steam_appid.txt")):
            return d
    raise SystemExit(
        "STEAMWORKSPY RUNTIME MISSING: none of these folders holds steamworks/ + "
        "SteamworksPy64.dll + steam_api64.dll + steam_appid.txt:\n  " +
        "\n  ".join(candidates) +
        "\nSee steamugc/README.md for how to put them in place, or pass --runtime <dir>."
    )


ERESULT_OK = 1  # steamworks.enums.EResult.OK
VISIBILITIES = ["public", "friends", "private"]

# Filled in by load_steamworks(); nothing imports SteamworksPy at module scope so that
# --help, and every validation below, work on a machine with no Steam runtime at all.
STEAMWORKS = EWorkshopFileType = VISIBILITY_MAP = None


def load_steamworks(runtime: str = "") -> None:
    """chdir into the runtime dir and import SteamworksPy from it.

    The native shim resolves SteamworksPy64.dll / steam_api64.dll / steam_appid.txt
    relative to the CWD, so the chdir is required. Every path this script uses is
    absolute and was resolved before this call.
    """
    global STEAMWORKS, EWorkshopFileType, VISIBILITY_MAP
    d = resolve_runtime(runtime)
    print(f"[init] SteamworksPy runtime: {d}")
    os.chdir(d)
    sys.path.insert(0, d)
    from steamworks import STEAMWORKS as _SW
    from steamworks.enums import (
        EWorkshopFileType as _EWFT,
        ERemoteStoragePublishedFileVisibility as _VIS,
    )
    STEAMWORKS = _SW
    EWorkshopFileType = _EWFT
    VISIBILITY_MAP = {
        "public": _VIS.PUBLIC,
        "friends": _VIS.FRIENDS_ONLY,
        "private": _VIS.PRIVATE,
    }


def resolve_preview() -> str:
    """Return the preview image path, or refuse. Never submit without one.

    Steam's limit is 1 MB; the Workshop renders the preview square, so the image
    is authored 1024x1024 JPG or PNG. The image is produced separately and is not
    part of this rig.
    """
    for p in PREVIEW_CANDIDATES:
        if os.path.exists(p):
            size = os.path.getsize(p)
            if size > 1_000_000:
                raise SystemExit(
                    f"PREVIEW IMAGE TOO LARGE: {p} is {size:,} bytes, Steam's limit is 1 MB "
                    f"(1,000,000). Re-export it smaller."
                )
            return p
    raise SystemExit(
        "PREVIEW IMAGE MISSING: expected one of\n  " + "\n  ".join(PREVIEW_CANDIDATES) +
        "\nRequirements: <= 1 MB, 1024x1024, JPG or PNG. Nothing is uploaded without it."
    )


def locale_descriptions() -> list:
    """[(steam_language_code, absolute path)] discovered from workshop/locale/.

    Adding a translation = dropping description.<language>.txt into that folder;
    english must exist and is pushed first because it is Steam's fallback.
    Language codes: https://partner.steamgames.com/doc/store/localization/languages
    """
    found = {}
    for path in sorted(glob.glob(os.path.join(LOCALE_DIR, "description.*.txt"))):
        lang = os.path.basename(path).split(".")[1]
        found[lang] = path
    if "english" not in found:
        raise SystemExit(f"Missing {os.path.join(LOCALE_DIR, 'description.english.txt')}")
    ordered = [("english", found.pop("english"))]
    ordered += sorted(found.items())
    return ordered


def read_description(path: str) -> str:
    with open(path, "r", encoding="utf-8") as f:
        text = f.read().strip()
    # Steam's limit is 8000 *bytes*: it says "ASCII characters" but counts the
    # UTF-8 byte length, and Cyrillic/CJK cost more than one byte per character.
    nbytes = len(text.encode("utf-8"))
    if nbytes > 8000:
        raise SystemExit(
            f"{os.path.basename(path)} is {nbytes} UTF-8 bytes ({len(text)} chars), "
            f"over Steam's 8000-byte limit."
        )
    return text


def pump_until(steam, holder: dict, timeout: float, label: str) -> dict:
    """Pump RunCallbacks until `holder` is populated by the async callback."""
    deadline = time.time() + timeout
    while not holder:
        steam.run_callbacks()
        if time.time() > deadline:
            raise SystemExit(f"TIMEOUT waiting for {label} after {timeout:.0f}s")
        time.sleep(0.1)
    return holder


def create_item(steam) -> dict:
    holder: dict = {}

    def on_created(result):
        holder["result"] = int(result.result)
        holder["id"] = int(result.publishedFileId)
        holder["needs_legal"] = bool(result.userNeedsToAcceptWorkshopLegalAgreement)

    print(f"[create] CreateItem(app={APP_ID}, COMMUNITY) ...")
    steam.Workshop.CreateItem(
        APP_ID, EWorkshopFileType.COMMUNITY,
        callback=on_created, override_callback=True,
    )
    pump_until(steam, holder, timeout=60.0, label="CreateItemResult_t")
    if holder["result"] != ERESULT_OK:
        raise SystemExit(
            f"CreateItem FAILED: EResult={holder['result']} "
            f"(see steamworks.enums.EResult). No item was created."
        )
    print(f"[create] OK -> publishedfileid={holder['id']}")
    return holder


def submit_update(steam, published_file_id: int, description: str, preview: str,
                  visibility, changenote: str) -> dict:
    """StartItemUpdate -> set fields -> SubmitItemUpdate (async, uploads)."""
    handle = steam.Workshop.StartItemUpdate(APP_ID, published_file_id)
    print(f"[update] StartItemUpdate handle={handle}")

    steam.Workshop.SetItemTitle(handle, TITLE)
    steam.Workshop.SetItemDescription(handle, description)
    steam.Workshop.SetItemContent(handle, CONTENT_FOLDER)
    steam.Workshop.SetItemPreview(handle, preview)
    steam.Workshop.SetItemVisibility(handle, visibility)
    steam.Workshop.SetItemTags(handle, WORKSHOP_TAGS)
    print(f"[update] content={CONTENT_FOLDER} preview={preview} "
          f"visibility={visibility.name} tags={WORKSHOP_TAGS}")

    holder: dict = {}

    def on_submitted(result):
        holder["result"] = int(result.result)
        holder["id"] = int(result.publishedFileId)
        holder["needs_legal"] = bool(result.userNeedsToAcceptWorkshopLegalAgreement)

    print(f"[update] SubmitItemUpdate(changenote={changenote!r}) ... (uploading)")
    steam.Workshop.SubmitItemUpdate(
        handle, changenote, callback=on_submitted, override_callback=True,
    )

    deadline = time.time() + 900.0  # 15 min for the upload
    last_pct = -1
    while not holder:
        steam.run_callbacks()
        try:
            prog = steam.Workshop.GetItemUpdateProgress(handle)
            pct = int(prog["progress"] * 100)
            if pct != last_pct and prog["total"]:
                print(f"[update] {prog['status'].name} "
                      f"{prog['processed']}/{prog['total']} ({pct}%)")
                last_pct = pct
        except Exception:
            pass
        if time.time() > deadline:
            raise SystemExit("TIMEOUT waiting for SubmitItemUpdateResult_t after 900s")
        time.sleep(0.25)

    if holder["result"] != ERESULT_OK:
        raise SystemExit(f"SubmitItemUpdate FAILED: EResult={holder['result']}.")
    print(f"[update] OK -> upload committed for id={holder['id']}")
    return holder


def submit_description_for_language(steam, published_file_id: int, lang_code: str,
                                    description: str, changenote: str,
                                    tags: list = None) -> int:
    """Push ONE localized store description and return its EResult.

    SetItemTitle is MANDATORY here and is not redundant: scoping an update with
    SetItemUpdateLanguage makes EVERY field on that update per-language, so a
    handle that sets only the description writes an EMPTY title for that language
    and the store page heading comes out blank for those players. Setting the
    same global TITLE on every pass is what keeps the name on all of them. (Paid
    for once already on another mod - do not "simplify" this line away.)

    Content, preview and visibility are deliberately untouched, so nothing is
    re-uploaded. `tags` is for the english pass only, tags being item-global.
    """
    handle = steam.Workshop.StartItemUpdate(APP_ID, published_file_id)
    steam.Workshop.SetItemUpdateLanguage(handle, lang_code)
    steam.Workshop.SetItemTitle(handle, TITLE)
    steam.Workshop.SetItemDescription(handle, description)
    if tags:
        ok = steam.Workshop.SetItemTags(handle, tags)
        print(f"[locale] {lang_code:<9}    SetItemTags({tags}) -> {ok}", flush=True)

    holder: dict = {}

    def on_submitted(result):
        holder["result"] = int(result.result)

    steam.Workshop.SubmitItemUpdate(
        handle, changenote, callback=on_submitted, override_callback=True,
    )
    pump_until(steam, holder, timeout=120.0,
               label=f"SubmitItemUpdateResult_t[{lang_code}]")
    return holder["result"]


def localize_descriptions(steam, published_file_id: int, changenote: str) -> dict:
    """Push every description.<lang>.txt in workshop/locale/. Returns {lang: eresult}."""
    results: dict = {}
    for lang_code, path in locale_descriptions():
        text = read_description(path)
        print(f"[locale] {lang_code:<9} <- {os.path.basename(path)} "
              f"({len(text.encode('utf-8'))} bytes) ...", flush=True)
        pass_tags = WORKSHOP_TAGS if lang_code == "english" else None
        try:
            eresult = submit_description_for_language(
                steam, published_file_id, lang_code, text, changenote, pass_tags)
        except SystemExit as e:
            print(f"[locale] {lang_code:<9} ERROR: {e}")
            results[lang_code] = -1
            continue
        results[lang_code] = eresult
        print(f"[locale] {lang_code:<9} -> "
              f"{'OK' if eresult == ERESULT_OK else f'FAILED (EResult={eresult})'}")
    return results


def persist_id(published_file_id: int) -> None:
    with open(PUBLISHED_ID_FILE, "w", encoding="utf-8") as f:
        f.write(str(published_file_id))
    print(f"[persist] wrote {PUBLISHED_ID_FILE}")
    try:
        with open(VDF_FILE, "r", encoding="utf-8") as f:
            vdf = f.read()
        import re
        new_vdf = re.sub(
            r'("publishedfileid"\s*")[^"]*(")',
            lambda m: f'{m.group(1)}{published_file_id}{m.group(2)}',
            vdf,
        )
        if new_vdf != vdf:
            with open(VDF_FILE, "w", encoding="utf-8") as f:
                f.write(new_vdf)
            print(f"[persist] stamped publishedfileid={published_file_id} into {VDF_FILE}")
        else:
            print(f"[persist] WARNING: no publishedfileid line replaced in {VDF_FILE}")
    except FileNotFoundError:
        print(f"[persist] WARNING: {VDF_FILE} not found; skipped vdf stamp")


def main() -> int:
    ap = argparse.ArgumentParser(
        description="Headless SteamworksPy Workshop publisher for ContentTool")
    mode = ap.add_mutually_exclusive_group(required=True)
    mode.add_argument("--create", action="store_true",
                      help="Create a brand-new Workshop item, then upload content.")
    mode.add_argument("--update", action="store_true",
                      help="Update the existing item (requires --item).")
    mode.add_argument("--localize-descriptions", action="store_true",
                      help="Push store descriptions from workshop/locale/ for an existing "
                           "item (requires --item). Touches no content, preview or visibility.")
    ap.add_argument("--item", type=int, default=0,
                    help="Existing publishedfileid (required with --update / "
                         "--localize-descriptions).")
    ap.add_argument("--changenote", default="v1.0.0 initial release",
                    help="Change note shown in the item's history.")
    ap.add_argument("--visibility", choices=VISIBILITIES, default="private",
                    help="Item visibility (default: private - look at the page before "
                         "making it public).")
    ap.add_argument("--runtime", default="",
                    help="Directory holding the SteamworksPy package + native DLLs.")
    args = ap.parse_args()

    if (args.update or args.localize_descriptions) and not args.item:
        ap.error("--update / --localize-descriptions requires --item <publishedfileid>")

    # Refuse EVERY missing input BEFORE a single call reaches Steam, so a bad run
    # cannot leave a half-created item behind.
    preview = description = visibility = None
    if not args.localize_descriptions:
        if not os.path.isdir(CONTENT_FOLDER) or not os.listdir(CONTENT_FOLDER):
            raise SystemExit(
                f"CONTENT FOLDER EMPTY OR MISSING: {CONTENT_FOLDER}\n"
                f"Run workshop\\pack-dist.ps1 first."
            )
        preview = resolve_preview()
        description = read_description(os.path.join(LOCALE_DIR, "description.english.txt"))
    else:
        for _, p in locale_descriptions():
            read_description(p)

    load_steamworks(args.runtime)
    if not args.localize_descriptions:
        visibility = VISIBILITY_MAP[args.visibility]

    steam = STEAMWORKS()
    steam.initialize()
    print(f"[init] SteamworksPy ready: appid={steam.app_id}, "
          f"SteamID={steam.Users.GetSteamID()}, "
          f"user={steam.Friends.GetPlayerName().decode(errors='replace')}")
    if steam.app_id != APP_ID:
        raise SystemExit(f"Bound to wrong appid {steam.app_id}, expected {APP_ID}")

    if args.localize_descriptions:
        results = localize_descriptions(steam, args.item, args.changenote)
        url = f"https://steamcommunity.com/sharedfiles/filedetails/?id={args.item}"
        print("=" * 70)
        print(f"LOCALIZE RESULT  item URL: {url}")
        for lang, r in results.items():
            print(f"  {lang:<9}: {'EResult.OK' if r == ERESULT_OK else f'FAILED (EResult={r})'}")
        print("=" * 70)
        steam.unload()
        return 0 if all(r == ERESULT_OK for r in results.values()) else 1

    print("=" * 70)
    print("ContentTool Workshop publisher (SteamworksPy, headless)")
    print(f"  mode        : {'create' if args.create else 'update'}")
    print(f"  app_id      : {APP_ID}   title: {TITLE}")
    print(f"  content     : {CONTENT_FOLDER}")
    print(f"  preview     : {preview}")
    print(f"  visibility  : {args.visibility}")
    print(f"  tags        : {WORKSHOP_TAGS}")
    print(f"  changenote  : {args.changenote}")
    print("=" * 70)

    needs_legal = False
    if args.create:
        created = create_item(steam)
        published_file_id = created["id"]
        needs_legal = created["needs_legal"]
    else:
        published_file_id = args.item
        print(f"[update] using existing publishedfileid={published_file_id}")

    submitted = submit_update(steam, published_file_id, description, preview,
                              visibility, args.changenote)
    needs_legal = needs_legal or submitted["needs_legal"]
    published_file_id = submitted["id"] or published_file_id

    persist_id(published_file_id)

    url = f"https://steamcommunity.com/sharedfiles/filedetails/?id={published_file_id}"
    print("=" * 70)
    print("PUBLISH RESULT")
    print(f"  publishedfileid : {published_file_id}")
    print(f"  item URL        : {url}")
    print(f"  upload          : committed (SubmitItemUpdate returned EResult.OK)")
    if needs_legal:
        print("  ACTION REQUIRED : Steam reports you must ACCEPT THE WORKSHOP LEGAL")
        print("                    AGREEMENT once. Open the item URL above and accept it,")
        print("                    or the item may stay hidden. One-time, per account.")
    print("=" * 70)

    steam.unload()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

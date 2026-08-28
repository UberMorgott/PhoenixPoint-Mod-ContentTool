# Headless Steam Workshop publisher (SteamworksPy)

Publishes / updates the **ContentTool** Workshop item by riding the **already-running,
logged-in Steam client** — no username, no password, no stored credential. Auth is your
active Steam session, exactly like the official PPWorkshopTool.

> **Nothing has been published yet.** This folder has never talked to Steam. There is no
> `published_id.txt` and `../contenttool.vdf` still holds `PUBLISHEDFILEID_PLACEHOLDER`.
> Read `../WORKSHOP.md` before the first run.

Only ContentTool is ever a Workshop item. The demos are GitHub downloads reached from the
documentation site — there is no per-demo path in this script.

## The runtime is not in this repo

The SteamworksPy python package and the native shim are environment-local binaries and are
git-ignored. `publish_ugc.py` looks for a folder holding all four of

```
steamworks/           the SteamworksPy python package
SteamworksPy64.dll    native ctypes shim, Steamworks SDK 1.64
steam_api64.dll       must export SteamInternal_SteamAPI_Init (SDK >= ~1.57)
steam_appid.txt       exactly: 839770
```

and it checks, in order: **this folder**, then the sibling
`..\..\..\PerkOracle\workshop\steamugc\` (same appid, same binaries — reused rather than
duplicating half a megabyte of native DLLs into a second repo). `--runtime <dir>` overrides.
If none qualifies the script stops with `STEAMWORKSPY RUNTIME MISSING` and names every path
it tried.

To build one from scratch:

1. From <https://github.com/philippj/SteamworksPy>: copy the `steamworks/` package folder and
   `redist/windows/SteamworksPy64.dll` here.
2. `steam_api64.dll` — Phoenix Point's own copy in
   `…\Phoenix Point\PhoenixPointWin64_Data\Plugins\x86_64\` is **too old** and fails the shim
   with `WinError 127`. Take a newer one from any recent Steam game and verify with `pefile`
   that it exports `SteamInternal_SteamAPI_Init`.
3. `steam_appid.txt` containing exactly `839770`, no newline.

## Preview image

Not in the repo — produced separately. Expected at `..\..\image\steam_preview.png` (or `.jpg`),
**≤ 1 MB, 1024×1024, JPG or PNG**. Absent or oversized, the script refuses with
`PREVIEW IMAGE MISSING` / `PREVIEW IMAGE TOO LARGE` before anything is sent to Steam.

## Run

Steam running, logged in as the account that will own the item. Any folder — paths are absolute.

```powershell
..\pack-dist.ps1                     # build + assemble ..\Dist first

# First publish. PRIVATE on purpose: create it, look at the page, then flip it public.
python publish_ugc.py --create --changenote "1.0.0 initial release" --visibility private

# Later updates:
python publish_ugc.py --update --item <publishedfileid> --changenote "1.0.1 ..."

# Store description only, no re-upload:
python publish_ugc.py --localize-descriptions --item <publishedfileid>
```

On success it writes `published_id.txt` and stamps the id into `../contenttool.vdf`, after which
`..\update.ps1` (SteamCMD) also works.

## Tags

Hardcoded `["Gameplay"]`, not a CLI option. Phoenix Point accepts a **fixed** tag set —
Geoscape, Tactical, Difficulty, Gameplay, Augments, Bionics, Mutations, plus DLC tags. There is
no `Tools`, no `UI` and no `Quality of Life`; an unknown tag makes `SubmitItemUpdate` fail.

## The per-language title trap

`SetItemUpdateLanguage` makes **every** field on that update handle per-language. A handle that
sets only the description therefore writes an **empty title** for that language and the store
page heading comes out blank for those players. `submit_description_for_language` calls
`SetItemTitle` on every pass for exactly this reason — that line looks redundant and is not.

Only `description.english.txt` ships today. Adding a translation is dropping
`description.<language>.txt` into `..\locale\`; the script discovers them.

## Notes

- If `CreateItem` reports the **workshop legal agreement** flag, accept it once at the item URL;
  the script prints a clear notice.
- Gallery screenshots are not set through the UGC API — add them on the item's web page.

# ContentTool — Steam Workshop publishing playbook

Everything needed to put **ContentTool** on the Steam Workshop for **Phoenix Point**
(appid **839770**), and to update it afterwards.

> **This rig has never been run.** No Workshop item exists, no id has been recorded, nothing has
> been uploaded. It was written offline so that you can read it first and run it yourself.

## Two rules that shape this whole folder

1. **Exactly one Workshop item, ever: ContentTool.** The demo mods under `demos\` are **not**
   Workshop items. They are GitHub downloads reached from the documentation site
   <https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/>. There is no per-demo publish
   path in any script here, and adding one would be a mistake.
2. **The store page's job is to send the reader to the documentation.** A player who installs
   ContentTool alone gets no content at all, and the description says so in its second line. It
   is an engine, not a mod with features.

---

## 0. What gets uploaded

A Phoenix Point Workshop item is a plain folder. `pack-dist.ps1` assembles it into
`workshop\Dist\` — the same set of files `deploy.ps1` puts into `Mods\ContentTool`, minus the
`.pdb`:

```
ContentTool.dll
meta.json
u8_probe.glb
u10_probe.glb
```

The file list is not written down twice: `pack-dist.ps1` copies `bin\Release\ContentTool\`, and
what lands there is decided by `ContentTool.csproj`. Change what ships by changing the csproj.

`Dist\` is generated and git-ignored.

---

## 1. Before the very first publish — the things only you can do

| # | Step | Why |
|---|---|---|
| 1 | **Produce the preview image** at `image\steam_preview.png` (or `.jpg`) | Not part of this rig. **≤ 1 MB, 1024×1024, JPG or PNG.** Both scripts refuse to upload without it — `PREVIEW IMAGE MISSING`. |
| 2 | **Read `locale\description.english.txt`** end to end | It is what the store page will say. Steam descriptions are **BBCode**, not Markdown. Limit is 8000 **bytes** (the scripts check). |
| 3 | **Have the documentation site live** at <https://ubermorgott.github.io/PhoenixPoint-Mod-ContentTool/> | The description sends every reader there for everything beyond "install this". |
| 4 | **Put the SteamworksPy runtime in place** | See `steamugc\README.md`. Native binaries, git-ignored, not in this repo. Skip this if you publish the first version through the PPWorkshopTool GUI instead. |
| 5 | **Steam client running**, logged in as the account that will own the item | The publisher rides that session; no password is ever typed into these scripts. |

---

## 2. First publish

```powershell
.\workshop\pack-dist.ps1
cd .\workshop\steamugc
python publish_ugc.py --create --changenote "1.0.0 initial release" --visibility private
```

`--visibility private` is the default on purpose: the item is created, the content and preview
are uploaded, and **you look at the page** before anyone else can. Nothing about this is
irreversible except the item's existence.

What that one command does, in order — it refuses on the first problem and nothing reaches Steam
until every check has passed:

1. checks `workshop\Dist\` exists and is not empty (`Run workshop\pack-dist.ps1 first`),
2. resolves the preview image and its size,
3. reads and byte-checks `locale\description.english.txt`,
4. finds the SteamworksPy runtime, binds to appid 839770, prints the logged-in account,
5. `CreateItem` → a fresh `publishedfileid`,
6. `SubmitItemUpdate` with title, description, content folder, preview, visibility and the tags,
   printing upload progress,
7. writes `steamugc\published_id.txt` and stamps the id into `contenttool.vdf`.

**Alternative first publish, no python:** the official
[PPWorkshopTool](https://github.com/SnapshotGames/PPWorkshopTool) GUI creates the item from
`workshop\Dist\` too. Then paste the id it gives you into `contenttool.vdf` by hand.

### One-time things that only happen on a brand-new item

- **Workshop legal agreement.** Steam may report that your account has never accepted it. The
  script prints `ACTION REQUIRED` with the item URL — open it, accept once, and the item stops
  being hidden. Per account, not per item.
- **Recording the id.** `publish_ugc.py` does it for you. If you used the GUI, edit
  `contenttool.vdf`: replace `PUBLISHEDFILEID_PLACEHOLDER` with the real number. Never paste an
  id from another mod into that file — SteamCMD would upload ContentTool over that item.
- **Going public.** Flip visibility on the item's web page once you are happy with it, or re-run
  with `--visibility public`.
- **Gallery screenshots.** Not reachable through the UGC API. Add them on the item's *Add/Edit
  Images* web page.
- **The GitHub side.** The releases the documentation site links to are a separate flow
  (`package.ps1` + a GitHub release); the Workshop item does not carry the demos.

---

## 3. Updating, from then on

Either path works; they use the same backend.

```powershell
# SteamworksPy, no password prompt, rides the running Steam client:
cd .\workshop\steamugc
python publish_ugc.py --update --item <publishedfileid> --changenote "1.0.1 - what changed"

# or SteamCMD, which prompts for password + 2FA in its own console:
.\workshop\update.ps1 -ChangeNote "1.0.1 - what changed" -SteamUser <yoursteamname>
```

`update.ps1` refuses to run while `contenttool.vdf` still holds the placeholder, refuses without
a preview image, then rebuilds `Dist\`, stamps the change note into the vdf and calls
`steamcmd +login <user> +workshop_build_item <abs vdf> +quit`. No credential is stored anywhere.

Install SteamCMD if needed: <https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip>
extracted to `C:\steamcmd`, or pass `-SteamCmd <path>`.

### Changing only the store text

```powershell
python publish_ugc.py --localize-descriptions --item <publishedfileid>
```

Pushes every `locale\description.<language>.txt`, touching no content, preview or visibility, so
nothing is re-uploaded.

---

## 4. Tags — a fixed set, and a wrong one fails the submit

Phoenix Point accepts **only**: `Geoscape`, `Tactical`, `Difficulty`, `Gameplay`, `Augments`,
`Bionics`, `Mutations`, plus the DLC tags. There is no `Tools`, no `UI` and no
`Quality of Life`. An unknown tag makes `SubmitItemUpdate` **fail**.

The rig sends **`["Gameplay"]`** and nothing else. It is hardcoded in `publish_ugc.py`, not a CLI
option, so a typo at the prompt cannot fail an upload.

---

## 5. The per-language title trap

`SetItemUpdateLanguage` scopes **every** field on that update handle to that language. A handle
that sets only the description writes an **empty title** for it, and the store page heading comes
out blank for players in that language.

`submit_description_for_language()` therefore calls `SetItemTitle` on **every** language pass.
That line looks redundant; it is not, and it has already been paid for once on another mod.

Today only `description.english.txt` exists, so the trap cannot fire yet. Adding a translation is
dropping `description.<language>.txt` into `locale\` — the script discovers the files, pushes
english first (Steam's fallback), and sets the tags once on that english pass because tags are
item-global.

---

## File map

| Path | What it is |
|---|---|
| `pack-dist.ps1` | Build Release, assemble `Dist\` from `bin\Release\ContentTool` minus the `.pdb`. |
| `Dist\` | Generated upload folder. Git-ignored. |
| `contenttool.vdf` | SteamCMD descriptor. Holds the placeholder until the item exists. |
| `update.ps1` | SteamCMD update path: check, pack, stamp the note, upload. |
| `locale\description.english.txt` | The store description, BBCode, ≤ 8000 bytes. |
| `steamugc\publish_ugc.py` | Headless create / update / description publisher. |
| `steamugc\README.md` | The SteamworksPy runtime, and how to recreate it. |
| `..\image\steam_preview.png` | **Missing on purpose.** ≤ 1 MB, 1024×1024, JPG or PNG. |

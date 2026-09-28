# Model Doctor: check a mesh before building

!!! note "Screenshots updating"
    The bench was redesigned. New screenshots will follow.

Use `Replace a model` to check how your GLB will bind to a shipped soldier or creature part. Open [the bench](index.md) from a loaded geoscape first.

## Choose the file and game part

1. Choose the `Replace a model` card. The step bar reads `Model file`, `Game part`, `Check`, `Build`.
2. Press `Choose your model file (.glb)...` and select your GLB. Later, use `Browse...` beside `Your model` to change it.
3. Press `Choose the game part it replaces...`. Type into `Find` to search for a part and select its result. You can also open `Browse all <n> game models`. Later, use `Change...` beside `Replaces` to pick again.
4. Check the selected part and `Mode`. `Replace the part` checks a replacement against that part’s bones. `Add to the part` is a different operation; `Build mod & apply` requires a replaceable part.

The main button changes with the step you need next. When it is disabled, read the reason directly beneath it.

!!! note "Screenshot updating"
    The new screenshot will show the game-part picker with `Find` containing a search and a matching part ready to select.

## Read the check

Wait for `checking the model...` to finish. The badge gives one sentence:

| Badge | What to do |
|---|---|
| `PASS` | The bones match by name. Your model can use its skin weights. |
| `WARN` | Read `Details and log`. A name mismatch can import the mesh while replacing its skin weights; other warnings can flag material parts. |
| `FAIL` | The model or target cannot be used here yet. Open `Details and log` for the blocking reasons. |

Open `Details and log` for the technical verdict, mesh counts, diagnostic rows, and remedies. Read `WARNING` rows even when the bones match: a mesh can bind correctly while its material parts land in the wrong order. `Copy report` copies the full report.

If `Bones to fix` appears, open it to see counts by body region. Map each extra file bone to the intended game bone, then use `Save bone map` when the mapping is valid. Recheck the result before building.

!!! note "Screenshot updating"
    The new screenshot will show a checked GLB with its `PASS`, `WARN`, or `FAIL` badge and one-sentence result, plus `Details and log` open on the diagnostic rows.

## Preview and build

1. Use `Preview on the model` to inspect an accepted mesh on the live part. This preview writes no mod files. Use `Undo preview` to compare with the original.
2. Under `Build the mod`, check `Mod name` and the `replaces the game file` hint.
3. Press `Build mod & apply`. It creates or updates a mod folder beside ContentTool, copies the GLB, adds its replacement row, then bakes and applies it. A saved bone map is written with the copied mesh when needed.
4. Read the result beneath the button. Open `Details and log` for the mod folder and full result. A successful install moves to [Build & share](lifecycle-tab.md). If the result asks for a restart, restart and enable the mod before testing it.

`Build mod & apply` requires a completed check that binds by name, a replaceable game part, and a valid mod name. Its disabled state names the missing condition. A preview by itself does not prove that the baked replacement is installed.

!!! note "Screenshot updating"
    The new screenshot will show a by-name check, `Build the mod` with a valid `Mod name`, and the enabled `Build mod & apply` button.

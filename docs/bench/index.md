# The in-game bench

!!! note "Screenshots updating"
    The bench was redesigned. New screenshots will follow.

The bench lets you check a model, fit a weapon in a soldier’s hand, and build a mod while seeing the result in game.

## Open the bench

1. Load or start a campaign and wait for the geoscape to finish loading. The bench uses its squad bay.
2. Open the developer console and enter:

    ```text
    ct_bench open
    ```

    You can also press `Ctrl+Alt+B`. If that hotkey is unavailable, use the console command.
3. Choose a task card. `ct_bench` with no argument toggles the bench.

!!! note "Screenshot updating"
    The new screenshot will show the task-card home screen with its available and unavailable cards.

## Choose a task

| Card | What it opens |
|---|---|
| `Replace a model` | Check a GLB against a soldier or creature part, then build a replacement. Follow [Model Doctor](model-doctor.md). |
| `Fit a weapon` | Position a weapon your mod adds in a soldier’s hand, then save its fit. |
| `Build & share` | Build, install, test, and package a selected mod. Follow [Build & share](lifecycle-tab.md). |

The `Add a weapon`, `Sounds`, and `Videos` cards explain that those tasks currently use console commands. Use [Find game content](../find-content/index.md) to locate shipped assets.

## Fit a weapon

1. Choose `Fit a weapon`. The step bar reads `Soldier`, `Weapon`, `Adjust`, `Save`.
2. Open `Soldier` and choose who will hold the weapon. Open `Weapon` and choose a weapon. The list marks weapons made by your mod with `*`; `* live` means their fit is loaded in this session.
3. Inspect the weapon on the model. Drag its arrows to move it or its rings to turn it. Middle-drag to orbit, use the wheel to zoom, and press `F` to frame the view.
4. Use `Save to file` to write a mod weapon’s fit. The line beneath a disabled button says what is missing. A shipped game weapon can be held for comparison, but has no mod file to save into.

The badge says `Changed - not saved yet.` or `Saved - the file matches what you see.` Use `Revert` to return to the file’s values or `Reset to automatic` to return to the measured fit. Both leave the file untouched until you save. Open `Details` for the full result of the last action.

!!! note "Screenshot updating"
    The new screenshot will show `Fit a weapon` with a selected soldier, a mod weapon marked `* live`, the step bar, and the changed fit badge above `Save to file`.

## Leave or reset the view

Use `< Tasks` to return to the cards. Use `Reset view` or press `Home` to reset the camera. Use `Close (Ctrl+Alt+B)`, the hotkey, or `ct_bench close` to leave.

If opening is refused, check that a geoscape campaign has finished loading; the main menu does not have the squad bay.
